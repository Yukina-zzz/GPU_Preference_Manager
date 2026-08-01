using GpuPreferenceManager.Core.Adapters;

namespace GpuPreferenceManager.Core.Metrics;

public readonly record struct ProcessInstanceKey(int ProcessId, long CreationTimeFileTime);

public sealed record ProcessInfoSnapshot(
    ProcessInstanceKey Key,
    string ProcessName,
    string? ExecutablePath,
    string? FileDescription,
    bool IsProtectedOrInaccessible,
    int? ParentProcessId = null,
    bool HasVisibleTopLevelWindow = false);

public enum GpuCounterKind
{
    DedicatedMemory,
    SharedMemory,
    EngineUtilization,
}

public sealed record PdhGpuInstance(
    int ProcessId,
    uint FirstLuidPart,
    uint SecondLuidPart,
    int PhysicalAdapterIndex,
    int? EngineIndex,
    string? EngineType,
    int? DuplicateSuffix,
    string RawName);

public sealed record RawGpuCounterValue(
    GpuCounterKind Kind,
    PdhGpuInstance Instance,
    double Value,
    uint CounterStatus);

public sealed record GpuMetricsSnapshot(
    DateTimeOffset SampleTimeUtc,
    IReadOnlyList<RawGpuCounterValue> Values,
    IReadOnlyList<string> UnparsedInstances);

public sealed record ProcessAdapterGpuUsage(
    ProcessInstanceKey Process,
    GpuAdapterId Adapter,
    long DedicatedBytes,
    long SharedBytes,
    IReadOnlyDictionary<string, double> EngineUtilization,
    DateTimeOffset SampleTimeUtc);

public interface IGpuMetricsSampler : IAsyncDisposable
{
    IAsyncEnumerable<GpuMetricsSnapshot> SampleAsync(
        TimeSpan interval,
        CancellationToken cancellationToken);
}

public interface IProcessInfoProvider
{
    ValueTask<ProcessInfoSnapshot> GetAsync(int processId, CancellationToken cancellationToken);

    ValueTask<IReadOnlyList<ProcessInfoSnapshot>> GetAllAsync(CancellationToken cancellationToken);
}
