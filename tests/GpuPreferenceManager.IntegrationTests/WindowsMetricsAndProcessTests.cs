using System.Diagnostics;
using GpuPreferenceManager.Windows.Metrics;
using GpuPreferenceManager.Windows.Processes;

namespace GpuPreferenceManager.IntegrationTests;

public sealed class WindowsMetricsAndProcessTests
{
    [WindowsHardwareFact]
    public async Task ReadsCurrentProcessPathAndCreationIdentity()
    {
        WindowsProcessInfoProvider provider = new();
        var process = await provider.GetAsync(Environment.ProcessId, CancellationToken.None);

        Assert.False(process.IsProtectedOrInaccessible);
        Assert.NotNull(process.ExecutablePath);
        Assert.True(File.Exists(process.ExecutablePath));
        Assert.True(process.Key.CreationTimeFileTime > 0);
    }

    [WindowsHardwareFact]
    public async Task PdhQueryInitializesAndReturnsASnapshot()
    {
        await using PdhGpuMetricsSampler sampler = new();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(5));
        await foreach (var snapshot in sampler.SampleAsync(TimeSpan.FromMilliseconds(250), timeout.Token))
        {
            Assert.NotEqual(default, snapshot.SampleTimeUtc);
            return;
        }

        Assert.Fail("PDH 未返回快照。");
    }
}
