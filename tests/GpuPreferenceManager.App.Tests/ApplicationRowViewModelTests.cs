using GpuPreferenceManager.App.ViewModels;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.Metrics;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.App.Tests;

public sealed class ApplicationRowViewModelTests
{
    [Fact]
    public void MultiExecutableGroupExposesAndUsesAnExplicitPreferenceTarget()
    {
        const string hostPath = @"C:\Fixture\Host.exe";
        const string helperPath = @"C:\Fixture\Helper.exe";
        ApplicationRowViewModel row = new() { ExecutablePath = hostPath };
        ExecutableGpuUsage source = new(
            hostPath,
            "Fixture Host",
            [new(10, 100), new(11, 101)],
            new Dictionary<GpuAdapterId, AggregatedAdapterUsage>(),
            RegistryRuleParser.Parse(null),
            false,
            true,
            0,
            ApplicationCategory.Pending,
            DateTimeOffset.UnixEpoch,
            ProcessUsages:
            [
                new(new(10, 100), "Fixture Host", "Host.exe", new Dictionary<GpuAdapterId, AggregatedAdapterUsage>(), hostPath, RegistryRuleParser.Parse(null)),
                new(new(11, 101), "Fixture Helper", "Helper.exe", new Dictionary<GpuAdapterId, AggregatedAdapterUsage>(), helperPath, RegistryRuleParser.Parse("GpuPreference=1;")),
            ]);

        row.Update(source, []);
        PreferenceTargetViewModel helper = Assert.Single(
            row.PreferenceTargets,
            target => string.Equals(target.ExecutablePath, helperPath, StringComparison.OrdinalIgnoreCase));
        row.SelectedPreferenceTarget = helper;

        Assert.True(row.HasMultiplePreferenceTargets);
        Assert.Equal(helperPath, row.EffectivePreferencePath, ignoreCase: true);
        Assert.Equal("通用节能", row.Preference);
        Assert.Equal("多个 EXE：2 种规则", row.PreferenceSummary);
        Assert.True(row.HasReadablePath);
    }

    [Fact]
    public void UpdatePreservesSelectionAndUsesHighPerformanceAdapterUsage()
    {
        const long MiB = 1024 * 1024;
        GpuAdapterId integrated = new(1, 0, 0x1002, 0x164E, 0x164E1002);
        GpuAdapterId discrete = new(2, 0, 0x1002, 0x73EF, 0x1EFE);
        ApplicationRowViewModel row = new() { ExecutablePath = @"C:\Fixture\Game.exe", IsSelected = true };
        ExecutableGpuUsage source = new(
            row.ExecutablePath,
            "Fixture Game",
            [new(7, 123), new(8, 124)],
            new Dictionary<GpuAdapterId, AggregatedAdapterUsage>
            {
                [integrated] = new(2 * MiB, 3 * MiB, new Dictionary<string, double>()),
                [discrete] = new(10 * MiB, 20 * MiB, new Dictionary<string, double> { ["3D"] = 72.5 }),
            },
            RegistryRuleParser.Parse("GpuPreference=2;"),
            false,
            true,
            99,
            ApplicationCategory.Assigned,
            DateTimeOffset.UtcNow,
            IsForegroundApplication: true);

        GpuAdapterInfo integratedInfo = new(
            integrated,
            "AMD Radeon(TM) Graphics",
            512 * 1024 * 1024,
            8L * 1024 * 1024 * 1024,
            "1002&164E&164E1002",
            GpuAdapterRole.IntegratedOrPowerSaving,
            AdapterIdentityConfidence.DerivedFromDxgi,
            false,
            false,
            true);
        GpuAdapterInfo discreteInfo = new(
            discrete,
            "AMD Radeon RX 6650 XT",
            8L * 1024 * 1024 * 1024,
            16L * 1024 * 1024 * 1024,
            "1002&73EF&1EFE",
            GpuAdapterRole.DiscreteOrHighPerformance,
            AdapterIdentityConfidence.DerivedFromDxgi,
            false,
            false,
            true);

        row.Update(source, [integratedInfo, discreteInfo]);

        Assert.True(row.IsSelected);
        Assert.Equal(10 * MiB, row.DedicatedBytes);
        Assert.Equal(2 * MiB, row.OtherDedicatedBytes);
        Assert.Equal(23 * MiB, row.SharedBytes);
        Assert.Equal(ApplicationCategory.Assigned, row.Category);
        Assert.Equal("前台应用", row.ProcessSection);
        Assert.Equal("3D 72.5%", row.Engine);
        Assert.Contains("AMD Radeon RX 6650 XT", row.ActualGpu);
        Assert.Contains("AMD Radeon(TM) Graphics", row.ActualGpu);
        Assert.Contains("2 个相关进程", row.ProcessDetails);
        Assert.True(row.HasMultipleProcesses);
        Assert.Equal(2, row.Processes.Count);
        Assert.Contains('\n', row.ActualGpuTable);
        Assert.Equal(0, row.MissingSamples);
    }
}
