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
