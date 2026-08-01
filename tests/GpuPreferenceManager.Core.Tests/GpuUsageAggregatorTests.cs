using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Metrics;

namespace GpuPreferenceManager.Core.Tests;

public sealed class GpuUsageAggregatorTests
{
    [Fact]
    public void TakesDuplicateMaximumSumsPhysicalPartitionsAndKeepsEngineMaximum()
    {
        GpuAdapterDescriptor adapter = new(new(0xABCD, 0, 0x1002, 1, 1), "Fixture", 1000, 1000, false, false);
        PdhGpuInstance memory0 = Instance(42, 0, 0xABCD, 0);
        PdhGpuInstance memory1 = Instance(42, 0, 0xABCD, 1);
        PdhGpuInstance engine = Instance(42, 0, 0xABCD, 0, "3D");
        GpuMetricsSnapshot snapshot = new(
            DateTimeOffset.UnixEpoch,
            [
                new(GpuCounterKind.DedicatedMemory, memory0, 100, 0),
                new(GpuCounterKind.DedicatedMemory, memory0 with { DuplicateSuffix = 1 }, 120, 0),
                new(GpuCounterKind.DedicatedMemory, memory1, 30, 0),
                new(GpuCounterKind.SharedMemory, memory0, 20, 0),
                new(GpuCounterKind.EngineUtilization, engine, 30, 0),
                new(GpuCounterKind.EngineUtilization, engine with { EngineIndex = 2 }, 70, 0),
            ],
            []);

        IReadOnlyList<ProcessAdapterGpuUsage> result = GpuUsageAggregator.Aggregate(
            snapshot,
            new Dictionary<int, ProcessInstanceKey> { [42] = new(42, 1000) },
            [adapter],
            new GpuLuidMapper());

        ProcessAdapterGpuUsage usage = Assert.Single(result);
        Assert.Equal(150, usage.DedicatedBytes);
        Assert.Equal(20, usage.SharedBytes);
        Assert.Equal(70, usage.EngineUtilization["3D"]);
    }

    [Fact]
    public void MapsSwappedLuidOrderWhenOnlySwappedMatches()
    {
        GpuAdapterDescriptor adapter = new(new(0, 5, 1, 1, 1), "Fixture", 1, 1, false, false);
        PdhGpuInstance instance = Instance(1, 0, 5, 0);
        GpuLuidMapper mapper = new();

        Assert.True(mapper.TryMap(instance, [adapter], out GpuAdapterId id));
        Assert.Equal(adapter.Id, id);
    }

    private static PdhGpuInstance Instance(
        int pid,
        uint first,
        uint second,
        int physical,
        string? engineType = null) =>
        new(pid, first, second, physical, engineType is null ? null : 1, engineType, null, "fixture");
}
