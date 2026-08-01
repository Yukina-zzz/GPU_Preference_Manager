using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.Metrics;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Tests;

public sealed class ApplicationInventoryBuilderTests
{
    [Fact]
    public void PreservesPerProcessGpuUsageInsideExecutableGroup()
    {
        ProcessInstanceKey first = new(101, 1001);
        ProcessInstanceKey second = new(202, 2002);
        GpuAdapterId integrated = new(1, 0, 0x1002, 0x164E, 0x164E1002);
        GpuAdapterId discrete = new(2, 0, 0x1002, 0x73EF, 0x1EFE);
        const string path = @"C:\Fixture\browser.exe";
        ProcessInfoSnapshot[] processes =
        [
            new(first, "browser", path, "Fixture Browser", false),
            new(second, "browser", path, "Fixture Browser", false),
        ];
        ProcessAdapterGpuUsage[] usages =
        [
            new(first, integrated, 10, 20, new Dictionary<string, double> { ["3D"] = 1 }, DateTimeOffset.UnixEpoch),
            new(second, discrete, 30, 40, new Dictionary<string, double> { ["Copy"] = 2 }, DateTimeOffset.UnixEpoch),
        ];
        RegistrySnapshot registry = new(DateTimeOffset.UnixEpoch, true, [], null);

        ExecutableGpuUsage result = Assert.Single(ApplicationInventoryBuilder.Build(
            processes,
            usages,
            registry,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, long>()));

        Assert.Equal(2, result.ProcessUsages?.Count);
        Assert.Equal(10, result.ProcessUsages![0].AdapterUsages[integrated].DedicatedBytes);
        Assert.Equal(30, result.ProcessUsages[1].AdapterUsages[discrete].DedicatedBytes);
    }

    [Fact]
    public void GroupsHostedHelperWithParentAndKeepsZeroUsageSiblings()
    {
        ProcessInstanceKey host = new(100, 1000);
        ProcessInstanceKey gpuChild = new(101, 1001);
        ProcessInstanceKey idleChild = new(102, 1002);
        GpuAdapterId adapter = new(2, 0, 0x1002, 0x73EF, 0x1EFE);
        const string hostPath = @"C:\Windows\SystemApps\SearchHost.exe";
        const string helperPath = @"C:\Program Files\WebView2\msedgewebview2.exe";
        ProcessInfoSnapshot[] processes =
        [
            new(host, "SearchHost", hostPath, "搜索", false, null, true),
            new(gpuChild, "msedgewebview2", helperPath, "Microsoft Edge WebView2", false, 100),
            new(idleChild, "msedgewebview2", helperPath, "Microsoft Edge WebView2", false, 100),
        ];
        ProcessAdapterGpuUsage[] usages =
        [
            new(gpuChild, adapter, 12_000_000, 0, new Dictionary<string, double> { ["3D"] = 4 }, DateTimeOffset.UnixEpoch),
        ];

        const string helperRule = "GpuPreference=1;";
        RegistrySnapshot registry = new(
            DateTimeOffset.UnixEpoch,
            true,
            [new(helperPath, new(RegistryDataKind.Text, StringValue: helperRule), RegistryRuleParser.Parse(helperRule), false)],
            null);
        ExecutableGpuUsage result = Assert.Single(ApplicationInventoryBuilder.Build(
            processes,
            usages,
            registry,
            new HashSet<string>(StringComparer.OrdinalIgnoreCase),
            new Dictionary<string, long>()));

        Assert.Equal(hostPath, result.ExecutablePath, ignoreCase: true);
        Assert.Equal("搜索", result.DisplayName);
        Assert.True(result.IsForegroundApplication);
        Assert.Equal(ApplicationCategory.Assigned, result.Category);
        Assert.Equal(3, result.Processes.Count);
        IReadOnlyList<ProcessGpuUsage> processUsages = Assert.IsAssignableFrom<IReadOnlyList<ProcessGpuUsage>>(result.ProcessUsages);
        Assert.Equal(3, processUsages.Count);
        Assert.Empty(processUsages.Single(process => process.Process.ProcessId == 100).AdapterUsages);
        Assert.Equal(helperPath, processUsages.Single(process => process.Process.ProcessId == 101).ExecutablePath);
        Assert.Equal(GpuPreferenceKind.GenericPowerSaving, processUsages.Single(process => process.Process.ProcessId == 101).Rule?.Kind);
        Assert.Empty(processUsages.Single(process => process.Process.ProcessId == 102).AdapterUsages);
    }
}
