using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Adapters;

/// <summary>
/// 把 DXGI 身份与现有注册表规则匹配，并检测 SpecificAdapter 键歧义。
/// </summary>
public static class GpuAdapterMappingService
{
    /// <summary>
    /// 映射适配器并推断当前机器角色。名称仅用于展示，不参与身份或角色判断。
    /// </summary>
    public static IReadOnlyList<GpuAdapterInfo> Map(
        IReadOnlyList<GpuAdapterDescriptor> descriptors,
        RegistrySnapshot registrySnapshot) => Map(descriptors, registrySnapshot, []);

    /// <summary>
    /// 映射适配器，并在自动判断之上应用本地用户覆盖。
    /// </summary>
    public static IReadOnlyList<GpuAdapterInfo> Map(
        IReadOnlyList<GpuAdapterDescriptor> descriptors,
        RegistrySnapshot registrySnapshot,
        IReadOnlyCollection<GpuAdapterOverride> overrides)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(registrySnapshot);
        ArgumentNullException.ThrowIfNull(overrides);

        HashSet<string> existingSpecificKeys = registrySnapshot.ApplicationValues
            .Select(static value => value.Rule?.SpecificAdapterKey)
            .Where(static key => !string.IsNullOrWhiteSpace(key))
            .Select(static key => key!)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        string? highPerformanceKey = registrySnapshot.GlobalSettings?.HighPerformanceAdapterKey;
        var keyed = descriptors.Select(descriptor => new
        {
            Descriptor = descriptor,
            Key = SpecificAdapterKey.Build(
                descriptor.Id.VendorId,
                descriptor.Id.DeviceId,
                descriptor.Id.SubSystemId),
        }).ToList();

        Dictionary<string, int> keyCounts = keyed
            .Where(static item => GpuAdapterIdentity.HasValidPciIdentity(item.Descriptor))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Count(), StringComparer.OrdinalIgnoreCase);

        var configured = keyed.Select(item =>
        {
            string? identity = GpuAdapterIdentity.Build(
                item.Descriptor,
                item.Key,
                keyCounts.TryGetValue(item.Key, out int count) && count == 1);
            GpuAdapterOverride? adapterOverride = identity is null
                ? null
                : overrides.LastOrDefault(candidate => GpuAdapterIdentity.Matches(candidate.AdapterIdentity, identity));
            AdapterAutomaticExclusionReason reasons = GetAutomaticExclusionReasons(item.Descriptor);
            bool isIncluded = adapterOverride?.ExclusionMode switch
            {
                AdapterExclusionMode.Excluded => false,
                AdapterExclusionMode.ForceIncluded => true,
                _ => reasons == AdapterAutomaticExclusionReason.None,
            };
            return new
            {
                item.Descriptor,
                item.Key,
                Identity = identity,
                Override = adapterOverride,
                AutomaticExclusionReasons = reasons,
                IsIncluded = isIncluded,
            };
        }).ToList();

        HashSet<string> ambiguousKeys = configured
            .Where(static item => item.IsIncluded && GpuAdapterIdentity.HasValidPciIdentity(item.Descriptor))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int assignableCount = configured.Count(item => item.IsIncluded
            && GpuAdapterIdentity.HasValidPciIdentity(item.Descriptor)
            && !ambiguousKeys.Contains(item.Key));
        bool hasUniqueHighPerformanceAdapter = configured.Count(
            item => item.IsIncluded
                && GpuAdapterIdentity.HasValidPciIdentity(item.Descriptor)
                && SpecificAdapterKey.Matches(item.Key, highPerformanceKey)
                && !ambiguousKeys.Contains(item.Key)) == 1;

        var manuallyAssigned = configured
            .Where(item => item.IsIncluded
                && GpuAdapterIdentity.HasValidPciIdentity(item.Descriptor)
                && !ambiguousKeys.Contains(item.Key)
                && item.Override?.Role is not null and not AdapterOverrideRole.Automatic)
            .ToList();
        var manualHigh = manuallyAssigned
            .Where(static item => item.Override!.Role == AdapterOverrideRole.DiscreteOrHighPerformance)
            .ToList();
        var manualPower = manuallyAssigned
            .Where(static item => item.Override!.Role == AdapterOverrideRole.IntegratedOrPowerSaving)
            .ToList();
        bool hasValidManualHigh = manualHigh.Count == 1;
        bool hasValidManualPower = manualPower.Count == 1;
        bool hasAnyManualRole = hasValidManualHigh || hasValidManualPower;

        return configured.Select(item =>
        {
            bool hasValidIdentity = GpuAdapterIdentity.HasValidPciIdentity(item.Descriptor);
            bool isAmbiguous = item.IsIncluded && hasValidIdentity && ambiguousKeys.Contains(item.Key);
            bool isAssignable = item.IsIncluded && hasValidIdentity && !isAmbiguous;
            bool isHighPerformance = isAssignable
                && SpecificAdapterKey.Matches(item.Key, highPerformanceKey);

            bool isManualHigh = hasValidManualHigh && ReferenceEquals(item, manualHigh[0]);
            bool isManualPower = hasValidManualPower && ReferenceEquals(item, manualPower[0]);
            bool isComplementaryHigh = hasAnyManualRole && assignableCount == 2
                && hasValidManualPower && !hasValidManualHigh && isAssignable && !isManualPower;
            bool isComplementaryPower = hasAnyManualRole && assignableCount == 2
                && hasValidManualHigh && !hasValidManualPower && isAssignable && !isManualHigh;

            GpuAdapterRole role = !item.IsIncluded
                ? GpuAdapterRole.Excluded
                : !isAssignable
                    ? GpuAdapterRole.Other
                    : hasAnyManualRole
                        ? isManualHigh || isComplementaryHigh
                            ? GpuAdapterRole.DiscreteOrHighPerformance
                            : isManualPower || isComplementaryPower
                                ? GpuAdapterRole.IntegratedOrPowerSaving
                                : GpuAdapterRole.Unknown
                        : isHighPerformance
                            ? GpuAdapterRole.DiscreteOrHighPerformance
                            : hasUniqueHighPerformanceAdapter && assignableCount == 2
                                ? GpuAdapterRole.IntegratedOrPowerSaving
                                : GpuAdapterRole.Unknown;

            AdapterIdentityConfidence confidence = isAmbiguous
                ? AdapterIdentityConfidence.Ambiguous
                : item.Override is not null
                    ? AdapterIdentityConfidence.UserConfirmed
                    : existingSpecificKeys.Contains(item.Key)
                    || SpecificAdapterKey.Matches(item.Key, highPerformanceKey)
                    ? AdapterIdentityConfidence.VerifiedByExistingRule
                    : AdapterIdentityConfidence.DerivedFromDxgi;

            return new GpuAdapterInfo(
                item.Descriptor.Id,
                item.Descriptor.Name,
                item.Descriptor.DedicatedVideoMemoryBytes,
                item.Descriptor.SharedSystemMemoryBytes,
                item.Key,
                role,
                confidence,
                item.Descriptor.IsSoftware,
                item.Descriptor.IsRemote,
                isAssignable,
                item.Descriptor.DeviceInstancePath,
                item.Descriptor.IsVirtual,
                item.Identity ?? string.Empty,
                item.Identity is not null,
                item.AutomaticExclusionReasons,
                item.Override?.ExclusionMode ?? AdapterExclusionMode.Automatic,
                isManualHigh || isManualPower ? AdapterRoleSource.UserOverride : AdapterRoleSource.Automatic);
        }).ToList();
    }

    private static AdapterAutomaticExclusionReason GetAutomaticExclusionReasons(GpuAdapterDescriptor descriptor)
    {
        AdapterAutomaticExclusionReason reasons = AdapterAutomaticExclusionReason.None;
        if (descriptor.IsSoftware)
        {
            reasons |= AdapterAutomaticExclusionReason.Software;
        }

        if (descriptor.IsRemote)
        {
            reasons |= AdapterAutomaticExclusionReason.Remote;
        }

        if (descriptor.IsVirtual)
        {
            reasons |= AdapterAutomaticExclusionReason.Virtual;
        }

        if (!GpuAdapterIdentity.HasValidPciIdentity(descriptor))
        {
            reasons |= AdapterAutomaticExclusionReason.MissingIdentity;
        }

        if (descriptor.DedicatedVideoMemoryBytes <= 0 && descriptor.SharedSystemMemoryBytes <= 0)
        {
            reasons |= AdapterAutomaticExclusionReason.NoUsableMemory;
        }

        return reasons;
    }
}
