using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.Core.Metrics;

public static class GpuUsageAggregator
{
    public static IReadOnlyList<ProcessAdapterGpuUsage> Aggregate(
        GpuMetricsSnapshot snapshot,
        IReadOnlyDictionary<int, ProcessInstanceKey> processes,
        IReadOnlyList<GpuAdapterDescriptor> adapters,
        GpuLuidMapper luidMapper)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(processes);
        ArgumentNullException.ThrowIfNull(adapters);
        ArgumentNullException.ThrowIfNull(luidMapper);

        var mapped = snapshot.Values
            .Where(value => processes.ContainsKey(value.Instance.ProcessId))
            .Select(value => luidMapper.TryMap(value.Instance, adapters, out GpuAdapterId adapter)
                ? new MappedValue(value, adapter)
                : null)
            .Where(static value => value is not null)
            .Select(static value => value!);

        List<ProcessAdapterGpuUsage> result = [];
        foreach (IGrouping<(int ProcessId, GpuAdapterId Adapter), MappedValue> group in mapped.GroupBy(
                     value => (value.Value.Instance.ProcessId, value.Adapter)))
        {
            long dedicated = AggregateMemory(group, GpuCounterKind.DedicatedMemory);
            long shared = AggregateMemory(group, GpuCounterKind.SharedMemory);
            IReadOnlyDictionary<string, double> engines = group
                .Where(item => item.Value.Kind == GpuCounterKind.EngineUtilization
                    && item.Value.Instance.EngineType is not null)
                .GroupBy(item => item.Value.Instance.EngineType!, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static engineGroup => engineGroup.Key,
                    static engineGroup => Math.Clamp(engineGroup.Max(item => item.Value.Value), 0, 100),
                    StringComparer.OrdinalIgnoreCase);
            result.Add(new(
                processes[group.Key.ProcessId],
                group.Key.Adapter,
                dedicated,
                shared,
                engines,
                snapshot.SampleTimeUtc));
        }

        return result;
    }

    private static long AggregateMemory(IEnumerable<MappedValue> values, GpuCounterKind kind) =>
        checked((long)values
            .Where(item => item.Value.Kind == kind)
            .GroupBy(item => item.Value.Instance.PhysicalAdapterIndex)
            .Sum(group => Math.Max(0, group.Max(item => item.Value.Value))));

    private sealed record MappedValue(RawGpuCounterValue Value, GpuAdapterId Adapter);
}
