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
        RegistrySnapshot registrySnapshot)
    {
        ArgumentNullException.ThrowIfNull(descriptors);
        ArgumentNullException.ThrowIfNull(registrySnapshot);

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

        HashSet<string> ambiguousKeys = keyed
            .Where(static item => IsHardwareCandidate(item.Descriptor))
            .GroupBy(static item => item.Key, StringComparer.OrdinalIgnoreCase)
            .Where(static group => group.Count() > 1)
            .Select(static group => group.Key)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        int hardwareCandidateCount = keyed.Count(item => IsHardwareCandidate(item.Descriptor)
            && !ambiguousKeys.Contains(item.Key));
        bool hasUniqueHighPerformanceAdapter = keyed.Count(
            item => IsHardwareCandidate(item.Descriptor)
                && SpecificAdapterKey.Matches(item.Key, highPerformanceKey)
                && !ambiguousKeys.Contains(item.Key)) == 1;

        return keyed.Select(item =>
        {
            bool isHardwareCandidate = IsHardwareCandidate(item.Descriptor);
            bool isAmbiguous = isHardwareCandidate && ambiguousKeys.Contains(item.Key);
            bool isAssignable = isHardwareCandidate && !isAmbiguous;
            bool isHighPerformance = isAssignable
                && SpecificAdapterKey.Matches(item.Key, highPerformanceKey);

            GpuAdapterRole role = !isHardwareCandidate
                ? GpuAdapterRole.Excluded
                : !isAssignable
                    ? GpuAdapterRole.Other
                    : isHighPerformance
                        ? GpuAdapterRole.DiscreteOrHighPerformance
                        : hasUniqueHighPerformanceAdapter && hardwareCandidateCount == 2
                            ? GpuAdapterRole.IntegratedOrPowerSaving
                            : GpuAdapterRole.Unknown;

            AdapterIdentityConfidence confidence = isAmbiguous
                ? AdapterIdentityConfidence.Ambiguous
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
                item.Descriptor.IsVirtual);
        }).ToList();
    }

    private static bool IsHardwareCandidate(GpuAdapterDescriptor descriptor) =>
        !descriptor.IsSoftware
        && !descriptor.IsRemote
        && !descriptor.IsVirtual
        && descriptor.Id.VendorId != 0
        && descriptor.Id.DeviceId != 0
        && (descriptor.DedicatedVideoMemoryBytes > 0 || descriptor.SharedSystemMemoryBytes > 0);
}
