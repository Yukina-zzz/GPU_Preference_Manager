using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Tests;

public sealed class GpuAdapterMappingServiceTests
{
    [Fact]
    public void MapsCurrentMachineKeysAndGlobalHighPerformanceRoleWithoutUsingNames()
    {
        RegistrySnapshot snapshot = CreateSnapshot("1002&73EF&1EFE", "1002&164E&164E1002");
        GpuAdapterDescriptor integrated = Descriptor(1, 0x164E, 0x164E1002, 512L << 20, 16L << 30);
        GpuAdapterDescriptor discrete = Descriptor(2, 0x73EF, 0x00001EFE, 8L << 30, 16L << 30);

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map([integrated, discrete], snapshot);

        Assert.Equal(GpuAdapterRole.IntegratedOrPowerSaving, result[0].Role);
        Assert.Equal(GpuAdapterRole.DiscreteOrHighPerformance, result[1].Role);
        Assert.All(result, adapter => Assert.True(adapter.IsAssignable));
        Assert.All(result, adapter => Assert.Equal(AdapterIdentityConfidence.VerifiedByExistingRule, adapter.IdentityConfidence));
    }

    [Fact]
    public void DuplicateSpecificKeysAreAmbiguousAndNotAssignable()
    {
        RegistrySnapshot snapshot = CreateSnapshot("1002&73EF&1EFE");
        GpuAdapterDescriptor first = Descriptor(1, 0x73EF, 0x1EFE, 8L << 30, 16L << 30);
        GpuAdapterDescriptor second = Descriptor(2, 0x73EF, 0x1EFE, 8L << 30, 16L << 30);

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map([first, second], snapshot);

        Assert.All(result, adapter => Assert.False(adapter.IsAssignable));
        Assert.All(result, adapter => Assert.Equal(AdapterIdentityConfidence.Ambiguous, adapter.IdentityConfidence));
    }

    [Fact]
    public void SoftwareRemoteVirtualOrIdentitylessAdaptersAreExcluded()
    {
        RegistrySnapshot snapshot = CreateSnapshot(null);
        GpuAdapterDescriptor software = Descriptor(1, 0x008C, 0, 0, 16L << 30) with { IsSoftware = true };
        GpuAdapterDescriptor remote = Descriptor(2, 0x008C, 0, 0, 16L << 30) with { IsRemote = true };
        GpuAdapterDescriptor identityless = Descriptor(3, 0, 0, 0, 0);
        GpuAdapterDescriptor virtualAdapter = Descriptor(4, 0x164E, 0x164E1002, 1L << 30, 16L << 30) with
        {
            IsVirtual = true,
            DeviceInstancePath = @"ROOT\DISPLAY\0000",
        };

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map(
            [software, remote, identityless, virtualAdapter],
            snapshot);

        Assert.All(result, adapter => Assert.Equal(GpuAdapterRole.Excluded, adapter.Role));
        Assert.All(result, adapter => Assert.False(adapter.IsAssignable));
    }

    [Fact]
    public void ExcludedVirtualAdapterDoesNotMakePhysicalAdapterAmbiguous()
    {
        RegistrySnapshot snapshot = CreateSnapshot("1002&73EF&1EFE", "1002&164E&164E1002");
        GpuAdapterDescriptor integrated = Descriptor(1, 0x164E, 0x164E1002, 1L << 30, 16L << 30);
        GpuAdapterDescriptor discrete = Descriptor(2, 0x73EF, 0x1EFE, 8L << 30, 16L << 30);
        GpuAdapterDescriptor virtualAdapter = Descriptor(3, 0x164E, 0x164E1002, 1L << 30, 16L << 30) with
        {
            IsVirtual = true,
            DeviceInstancePath = @"ROOT\DISPLAY\0000",
        };

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map(
            [integrated, discrete, virtualAdapter],
            snapshot);

        Assert.Equal(GpuAdapterRole.IntegratedOrPowerSaving, result[0].Role);
        Assert.True(result[0].IsAssignable);
        Assert.Equal(GpuAdapterRole.Excluded, result[2].Role);
        Assert.False(result[2].IsAssignable);
    }

    [Fact]
    public void ManualRolesCorrectAReversedWindowsHighPerformanceMapping()
    {
        RegistrySnapshot snapshot = CreateSnapshot("1002&164E&164E1002");
        GpuAdapterDescriptor integrated = Descriptor(1, 0x164E, 0x164E1002, 1L << 30, 16L << 30) with
        {
            DeviceInstancePath = @"PCI\VEN_1002&DEV_164E\IGPU",
        };
        GpuAdapterDescriptor discrete = Descriptor(2, 0x73EF, 0x1EFE, 8L << 30, 16L << 30) with
        {
            DeviceInstancePath = @"PCI\VEN_1002&DEV_73EF\DGPU",
        };
        GpuAdapterOverride[] overrides =
        [
            new("PATH:" + integrated.DeviceInstancePath, AdapterOverrideRole.IntegratedOrPowerSaving),
            new("PATH:" + discrete.DeviceInstancePath, AdapterOverrideRole.DiscreteOrHighPerformance),
        ];

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map([integrated, discrete], snapshot, overrides);

        Assert.Equal(GpuAdapterRole.IntegratedOrPowerSaving, result[0].Role);
        Assert.Equal(GpuAdapterRole.DiscreteOrHighPerformance, result[1].Role);
        Assert.All(result, adapter => Assert.Equal(AdapterRoleSource.UserOverride, adapter.RoleSource));
    }

    [Fact]
    public void OneManualRoleComplementsExactlyTwoAssignableAdapters()
    {
        RegistrySnapshot snapshot = CreateSnapshot(null);
        GpuAdapterDescriptor first = Descriptor(1, 0x164E, 0x164E1002, 1L << 30, 16L << 30) with
        {
            DeviceInstancePath = @"PCI\FIRST",
        };
        GpuAdapterDescriptor second = Descriptor(2, 0x73EF, 0x1EFE, 8L << 30, 16L << 30) with
        {
            DeviceInstancePath = @"PCI\SECOND",
        };

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map(
            [first, second],
            snapshot,
            [new("PATH:" + second.DeviceInstancePath, AdapterOverrideRole.DiscreteOrHighPerformance)]);

        Assert.Equal(GpuAdapterRole.IntegratedOrPowerSaving, result[0].Role);
        Assert.Equal(GpuAdapterRole.DiscreteOrHighPerformance, result[1].Role);
        Assert.Equal(AdapterRoleSource.Automatic, result[0].RoleSource);
        Assert.Equal(AdapterRoleSource.UserOverride, result[1].RoleSource);
    }

    [Fact]
    public void OneManualRoleDoesNotGuessOtherRolesWithThreeAdapters()
    {
        RegistrySnapshot snapshot = CreateSnapshot(null);
        GpuAdapterDescriptor[] descriptors = Enumerable.Range(1, 3)
            .Select(index => Descriptor((uint)index, (uint)(0x1000 + index), (uint)index, 1L << 30, 16L << 30) with
            {
                DeviceInstancePath = $"PCI\\ADAPTER{index}",
            })
            .ToArray();

        IReadOnlyList<GpuAdapterInfo> result = GpuAdapterMappingService.Map(
            descriptors,
            snapshot,
            [new("PATH:" + descriptors[0].DeviceInstancePath, AdapterOverrideRole.DiscreteOrHighPerformance)]);

        Assert.Equal(GpuAdapterRole.DiscreteOrHighPerformance, result[0].Role);
        Assert.All(result.Skip(1), adapter => Assert.Equal(GpuAdapterRole.Unknown, adapter.Role));
    }

    [Fact]
    public void ForceIncludedDuplicateRemainsAmbiguousAndManualExclusionResolvesIt()
    {
        RegistrySnapshot snapshot = CreateSnapshot(null);
        GpuAdapterDescriptor physical = Descriptor(1, 0x164E, 0x164E1002, 1L << 30, 16L << 30) with
        {
            DeviceInstancePath = @"PCI\PHYSICAL",
        };
        GpuAdapterDescriptor virtualAdapter = Descriptor(2, 0x164E, 0x164E1002, 1L << 30, 16L << 30) with
        {
            IsVirtual = true,
            DeviceInstancePath = @"ROOT\DISPLAY\0000",
        };

        IReadOnlyList<GpuAdapterInfo> restored = GpuAdapterMappingService.Map(
            [physical, virtualAdapter],
            snapshot,
            [new("PATH:" + virtualAdapter.DeviceInstancePath, ExclusionMode: AdapterExclusionMode.ForceIncluded)]);
        Assert.All(restored, adapter => Assert.False(adapter.IsAssignable));
        Assert.All(restored, adapter => Assert.Equal(AdapterIdentityConfidence.Ambiguous, adapter.IdentityConfidence));

        IReadOnlyList<GpuAdapterInfo> excluded = GpuAdapterMappingService.Map(
            [physical, virtualAdapter],
            snapshot,
            [new("PATH:" + virtualAdapter.DeviceInstancePath, ExclusionMode: AdapterExclusionMode.Excluded)]);
        Assert.True(excluded[0].IsAssignable);
        Assert.Equal(GpuAdapterRole.Excluded, excluded[1].Role);
    }

    [Fact]
    public void ForceIncludeCannotMakeMissingPciIdentityAssignable()
    {
        GpuAdapterDescriptor identityless = Descriptor(1, 0, 0, 0, 0) with
        {
            DeviceInstancePath = @"ROOT\DISPLAY\IDENTITYLESS",
            IsVirtual = true,
        };

        GpuAdapterInfo result = GpuAdapterMappingService.Map(
            [identityless],
            CreateSnapshot(null),
            [new("PATH:" + identityless.DeviceInstancePath, ExclusionMode: AdapterExclusionMode.ForceIncluded)]).Single();

        Assert.NotEqual(GpuAdapterRole.Excluded, result.Role);
        Assert.False(result.IsAssignable);
        Assert.True(result.AutomaticExclusionReasons.HasFlag(AdapterAutomaticExclusionReason.MissingIdentity));
    }

    private static GpuAdapterDescriptor Descriptor(
        uint luid,
        uint device,
        uint subsystem,
        long dedicated,
        long shared) => new(
            new GpuAdapterId(luid, 0, 0x1002, device, subsystem),
            $"Adapter {luid}",
            dedicated,
            shared,
            false,
            false);

    private static RegistrySnapshot CreateSnapshot(string? highPerformanceKey, params string[] existingKeys)
    {
        IReadOnlyList<RegistryValueSnapshot> values = existingKeys.Select((key, index) =>
        {
            string raw = $"SpecificAdapter={key};GpuPreference=1073741824;";
            return new RegistryValueSnapshot(
                $"C:\\Fixture\\App{index}.exe",
                new RegistryValueData(RegistryDataKind.Text, StringValue: raw),
                RegistryRuleParser.Parse(raw),
                false);
        }).ToList();

        DirectXGlobalSettings? global = highPerformanceKey is null
            ? null
            : new(
                $"HighPerfAdapter={highPerformanceKey};",
                highPerformanceKey,
                RegistryRuleParser.Tokenize($"HighPerfAdapter={highPerformanceKey};"));
        return new(DateTimeOffset.UnixEpoch, true, values, global);
    }
}
