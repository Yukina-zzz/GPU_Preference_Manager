using System.Runtime.CompilerServices;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.Metrics;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Metrics;

namespace GpuPreferenceManager.Windows.Monitoring;

public sealed class ApplicationInventoryService : IApplicationInventoryService
{
    private readonly IGpuAdapterEnumerator _adapterEnumerator;
    private readonly IProcessInfoProvider _processProvider;
    private readonly IUserGpuPreferencesReader _registry;
    private readonly IIgnoredApplicationStore _ignoredStore;
    private PdhGpuMetricsSampler? _sampler;
    private readonly GpuLuidMapper _luidMapper = new();
    private readonly GpuUsagePeakTracker _peaks = new();
    private IReadOnlyList<GpuAdapterDescriptor> _descriptors = [];
    private IReadOnlyList<GpuAdapterOverride> _adapterOverrides = [];
    private RegistrySnapshot? _lastRegistrySnapshot;

    public ApplicationInventoryService(
        IGpuAdapterEnumerator adapterEnumerator,
        IProcessInfoProvider processProvider,
        IUserGpuPreferencesReader registry,
        IIgnoredApplicationStore ignoredStore)
    {
        _adapterEnumerator = adapterEnumerator;
        _processProvider = processProvider;
        _registry = registry;
        _ignoredStore = ignoredStore;
    }

    public IReadOnlyList<GpuAdapterInfo> Adapters { get; private set; } = [];

    public void ApplyAdapterOverrides(IReadOnlyList<GpuAdapterOverride> adapterOverrides)
    {
        ArgumentNullException.ThrowIfNull(adapterOverrides);
        _adapterOverrides = adapterOverrides.ToArray();
        if (_descriptors.Count > 0 && _lastRegistrySnapshot is not null)
        {
            Adapters = GpuAdapterMappingService.Map(_descriptors, _lastRegistrySnapshot, _adapterOverrides);
        }
    }

    public async IAsyncEnumerable<IReadOnlyList<ExecutableGpuUsage>> MonitorAsync(
        TimeSpan interval,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await RefreshAdaptersAsync(cancellationToken);
        _sampler ??= new();
        await foreach (GpuMetricsSnapshot metrics in _sampler.SampleAsync(interval, cancellationToken))
        {
            int[] pids = metrics.Values.Select(static value => value.Instance.ProcessId).Distinct().ToArray();
            IReadOnlyList<ProcessInfoSnapshot> allProcesses = await _processProvider.GetAllAsync(cancellationToken);
            List<ProcessInfoSnapshot> processList = allProcesses.ToList();
            HashSet<int> knownProcessIds = processList.Select(static process => process.Key.ProcessId).ToHashSet();
            int[] missingActivePids = pids.Where(pid => !knownProcessIds.Contains(pid)).ToArray();
            if (missingActivePids.Length > 0)
            {
                processList.AddRange(await ReadProcessesAsync(missingActivePids, cancellationToken));
            }

            ProcessInfoSnapshot[] processes = processList.ToArray();
            Dictionary<int, ProcessInstanceKey> keys = processes.ToDictionary(
                static process => process.Key.ProcessId,
                static process => process.Key);
            IReadOnlyList<ProcessAdapterGpuUsage> usages = GpuUsageAggregator.Aggregate(
                metrics,
                keys,
                _descriptors,
                _luidMapper);
            Dictionary<ProcessInstanceKey, ProcessInfoSnapshot> processByKey = processes.ToDictionary(static process => process.Key);
            Dictionary<string, long> current = usages
                .Where(usage => processByKey.TryGetValue(usage.Process, out ProcessInfoSnapshot? process)
                    && ExecutablePathNormalizer.Normalize(process.ExecutablePath) is not null)
                .GroupBy(
                    usage => ExecutablePathNormalizer.Normalize(processByKey[usage.Process].ExecutablePath)!,
                    StringComparer.OrdinalIgnoreCase)
                .ToDictionary(
                    static group => group.Key,
                    static group => group.Sum(static usage => usage.DedicatedBytes),
                    StringComparer.OrdinalIgnoreCase);
            IReadOnlyDictionary<string, long> peaks = _peaks.Update(metrics.SampleTimeUtc, current);
            RegistrySnapshot registry = await _registry.ReadSnapshotAsync(cancellationToken);
            IReadOnlySet<string> ignored = await _ignoredStore.ReadAllAsync(cancellationToken);
            yield return ApplicationInventoryBuilder.Build(processes, usages, registry, ignored, peaks);
        }
    }

    public ValueTask DisposeAsync() => _sampler?.DisposeAsync() ?? ValueTask.CompletedTask;

    private async Task RefreshAdaptersAsync(CancellationToken cancellationToken)
    {
        _descriptors = _adapterEnumerator.EnumerateAdapters();
        RegistrySnapshot registry = await _registry.ReadSnapshotAsync(cancellationToken);
        _lastRegistrySnapshot = registry;
        Adapters = GpuAdapterMappingService.Map(_descriptors, registry, _adapterOverrides);
    }

    private async Task<ProcessInfoSnapshot[]> ReadProcessesAsync(int[] pids, CancellationToken cancellationToken)
    {
        using SemaphoreSlim limit = new(8);
        return await Task.WhenAll(pids.Select(async pid =>
        {
            await limit.WaitAsync(cancellationToken);
            try
            {
                return await _processProvider.GetAsync(pid, cancellationToken);
            }
            finally
            {
                limit.Release();
            }
        }));
    }
}
