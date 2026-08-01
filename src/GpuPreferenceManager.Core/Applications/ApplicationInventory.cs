using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Metrics;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.Core.Applications;

public sealed record AggregatedAdapterUsage(
    long DedicatedBytes,
    long SharedBytes,
    IReadOnlyDictionary<string, double> EngineUtilization);

public enum ApplicationCategory
{
    Pending,
    Assigned,
    Ignored,
    Exceptional,
    All,
}

public sealed record ExecutableGpuUsage(
    string ExecutablePath,
    string DisplayName,
    IReadOnlyList<ProcessInstanceKey> Processes,
    IReadOnlyDictionary<GpuAdapterId, AggregatedAdapterUsage> AdapterUsages,
    GpuPreferenceRule Rule,
    bool IsIgnored,
    bool IsPathAccessible,
    long PeakDedicatedBytes30Seconds,
    ApplicationCategory Category,
    DateTimeOffset LastSeenUtc,
    IReadOnlyList<ProcessGpuUsage>? ProcessUsages = null,
    bool IsForegroundApplication = false);

public sealed record ProcessGpuUsage(
    ProcessInstanceKey Process,
    string DisplayName,
    string ProcessName,
    IReadOnlyDictionary<GpuAdapterId, AggregatedAdapterUsage> AdapterUsages,
    string? ExecutablePath = null,
    GpuPreferenceRule? Rule = null);

public static class ApplicationInventoryBuilder
{
    public static IReadOnlyList<ExecutableGpuUsage> Build(
        IReadOnlyList<ProcessInfoSnapshot> processSnapshots,
        IReadOnlyList<ProcessAdapterGpuUsage> usages,
        RegistrySnapshot registry,
        IReadOnlySet<string> ignoredNormalizedPaths,
        IReadOnlyDictionary<string, long> peaks)
    {
        ArgumentNullException.ThrowIfNull(processSnapshots);
        ArgumentNullException.ThrowIfNull(usages);
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentNullException.ThrowIfNull(ignoredNormalizedPaths);
        ArgumentNullException.ThrowIfNull(peaks);

        Dictionary<ProcessInstanceKey, ProcessInfoSnapshot> processByKey = processSnapshots
            .ToDictionary(static process => process.Key);
        Dictionary<string, GpuPreferenceRule> rules = registry.ApplicationValues
            .Where(static value => value.Rule is not null)
            .Select(value => new
            {
                Path = ExecutablePathNormalizer.Normalize(value.Name),
                Rule = value.Rule!,
            })
            .Where(static entry => entry.Path is not null)
            .GroupBy(static entry => entry.Path!, StringComparer.OrdinalIgnoreCase)
            .ToDictionary(static group => group.Key, static group => group.Last().Rule, StringComparer.OrdinalIgnoreCase);

        Dictionary<int, ProcessInfoSnapshot> processById = processSnapshots
            .GroupBy(static process => process.Key.ProcessId)
            .ToDictionary(static group => group.Key, static group => group.OrderByDescending(item => item.Key.CreationTimeFileTime).First());
        Dictionary<int, string> applicationPathByProcessId = processSnapshots.ToDictionary(
            static process => process.Key.ProcessId,
            process => ResolveApplicationPath(process, processById));

        var grouped = usages
            .Where(usage => processByKey.ContainsKey(usage.Process))
            .GroupBy(usage =>
            {
                ProcessInfoSnapshot process = processByKey[usage.Process];
                return applicationPathByProcessId.GetValueOrDefault(process.Key.ProcessId)
                    ?? ExecutablePathNormalizer.Normalize(process.ExecutablePath)
                    ?? $"<pid:{process.Key.ProcessId}>";
            }, StringComparer.OrdinalIgnoreCase);

        List<ExecutableGpuUsage> result = [];
        foreach (var group in grouped)
        {
            List<ProcessInfoSnapshot> processes = processSnapshots
                .Where(process => string.Equals(
                    applicationPathByProcessId.GetValueOrDefault(process.Key.ProcessId),
                    group.Key,
                    StringComparison.OrdinalIgnoreCase))
                .DistinctBy(static process => process.Key)
                .OrderBy(static process => process.Key.ProcessId)
                .ToList();
            if (processes.Count == 0)
            {
                processes = group
                    .Select(usage => processByKey[usage.Process])
                    .DistinctBy(static process => process.Key)
                    .OrderBy(static process => process.Key.ProcessId)
                    .ToList();
            }

            bool pathAccessible = !group.Key.StartsWith("<pid:", StringComparison.Ordinal);
            GpuPreferenceRule rule = rules.GetValueOrDefault(group.Key) ?? RegistryRuleParser.Parse(null);
            bool ignored = ignoredNormalizedPaths.Contains(group.Key);
            IReadOnlyDictionary<GpuAdapterId, AggregatedAdapterUsage> adapterUsages = AggregateAdapterUsages(group);
            IReadOnlyList<ProcessGpuUsage> processUsages = processes
                .OrderBy(static process => process.Key.ProcessId)
                .Select(process => new ProcessGpuUsage(
                    process.Key,
                    process.FileDescription ?? process.ProcessName,
                    System.IO.Path.GetFileName(process.ExecutablePath) ?? process.ProcessName,
                    AggregateAdapterUsages(group.Where(usage => usage.Process == process.Key)),
                    process.ExecutablePath,
                    ExecutablePathNormalizer.Normalize(process.ExecutablePath) is string processPath
                        ? rules.GetValueOrDefault(processPath) ?? RegistryRuleParser.Parse(null)
                        : RegistryRuleParser.Parse(null)))
                .ToList();
            ApplicationCategory category = Classify(
                processUsages.Select(static process => process.Rule ?? RegistryRuleParser.Parse(null)).Append(rule),
                ignored,
                pathAccessible);
            ProcessInfoSnapshot primaryProcess = processes.FirstOrDefault(process =>
                    string.Equals(ExecutablePathNormalizer.Normalize(process.ExecutablePath), group.Key, StringComparison.OrdinalIgnoreCase))
                ?? processes[0];
            string displayName = new[] { primaryProcess.FileDescription }
                .FirstOrDefault(static description => !string.IsNullOrWhiteSpace(description))
                ?? primaryProcess.ProcessName;
            result.Add(new(
                group.Key,
                displayName,
                processes.Select(static process => process.Key).ToList(),
                adapterUsages,
                rule,
                ignored,
                pathAccessible,
                peaks.GetValueOrDefault(group.Key),
                category,
                group.Max(static usage => usage.SampleTimeUtc),
                processUsages,
                processes.Any(static process => process.HasVisibleTopLevelWindow)));
        }

        return result.OrderByDescending(static item => item.AdapterUsages.Values.Sum(static usage => usage.DedicatedBytes)).ToList();
    }

    private static string ResolveApplicationPath(
        ProcessInfoSnapshot process,
        Dictionary<int, ProcessInfoSnapshot> processById)
    {
        ProcessInfoSnapshot current = process;
        HashSet<int> visited = [current.Key.ProcessId];
        while (current.ParentProcessId is int parentId
            && visited.Add(parentId)
            && processById.TryGetValue(parentId, out ProcessInfoSnapshot? parent)
            && CanJoinParent(current, parent))
        {
            current = parent;
        }

        return ExecutablePathNormalizer.Normalize(current.ExecutablePath)
            ?? $"<pid:{current.Key.ProcessId}>";
    }

    private static bool CanJoinParent(ProcessInfoSnapshot child, ProcessInfoSnapshot parent)
    {
        string childName = System.IO.Path.GetFileName(child.ExecutablePath) ?? child.ProcessName;
        string parentName = System.IO.Path.GetFileName(parent.ExecutablePath) ?? parent.ProcessName;
        if (IsProcessTreeBoundary(parentName))
        {
            return false;
        }

        if (child.HasVisibleTopLevelWindow)
        {
            return false;
        }

        if (parent.HasVisibleTopLevelWindow)
        {
            return true;
        }

        string? childPath = ExecutablePathNormalizer.Normalize(child.ExecutablePath);
        string? parentPath = ExecutablePathNormalizer.Normalize(parent.ExecutablePath);
        return string.Equals(childPath, parentPath, StringComparison.OrdinalIgnoreCase)
            || IsHostedHelper(childName);
    }

    private static bool IsHostedHelper(string executableName) => executableName.Equals("msedgewebview2.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("webviewhost.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("runtimebroker.exe", StringComparison.OrdinalIgnoreCase);

    private static bool IsProcessTreeBoundary(string executableName) => executableName.Equals("explorer.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("svchost.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("services.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("wininit.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("winlogon.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("csrss.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("smss.exe", StringComparison.OrdinalIgnoreCase)
        || executableName.Equals("system", StringComparison.OrdinalIgnoreCase);

    private static Dictionary<GpuAdapterId, AggregatedAdapterUsage> AggregateAdapterUsages(
        IEnumerable<ProcessAdapterGpuUsage> usages) => usages
        .GroupBy(static usage => usage.Adapter)
        .ToDictionary(
            static adapterGroup => adapterGroup.Key,
            static adapterGroup => new AggregatedAdapterUsage(
                adapterGroup.Sum(static usage => usage.DedicatedBytes),
                adapterGroup.Sum(static usage => usage.SharedBytes),
                adapterGroup.SelectMany(static usage => usage.EngineUtilization)
                    .GroupBy(static engine => engine.Key, StringComparer.OrdinalIgnoreCase)
                    .ToDictionary(
                        static engineGroup => engineGroup.Key,
                        static engineGroup => engineGroup.Max(static value => value.Value),
                        StringComparer.OrdinalIgnoreCase)));

    private static ApplicationCategory Classify(
        IEnumerable<GpuPreferenceRule> rules,
        bool ignored,
        bool pathAccessible)
    {
        if (ignored)
        {
            return ApplicationCategory.Ignored;
        }

        GpuPreferenceKind[] kinds = rules.Select(static rule => rule.Kind).Distinct().ToArray();
        if (!pathAccessible || kinds.Contains(GpuPreferenceKind.Unknown))
        {
            return ApplicationCategory.Exceptional;
        }

        return kinds.Any(static kind => kind is GpuPreferenceKind.GenericPowerSaving
            or GpuPreferenceKind.GenericHighPerformance
            or GpuPreferenceKind.SpecificAdapter)
            ? ApplicationCategory.Assigned
            : ApplicationCategory.Pending;
    }
}

public sealed class GpuUsagePeakTracker
{
    private readonly TimeSpan _window;
    private readonly Dictionary<string, Queue<(DateTimeOffset Time, long Value)>> _samples =
        new(StringComparer.OrdinalIgnoreCase);

    public GpuUsagePeakTracker(TimeSpan? window = null)
    {
        _window = window ?? TimeSpan.FromSeconds(30);
    }

    public IReadOnlyDictionary<string, long> Update(
        DateTimeOffset sampleTime,
        IReadOnlyDictionary<string, long> dedicatedBytesByPath)
    {
        foreach ((string path, long value) in dedicatedBytesByPath)
        {
            Queue<(DateTimeOffset Time, long Value)> queue = _samples.GetValueOrDefault(path) ?? new();
            _samples[path] = queue;
            queue.Enqueue((sampleTime, Math.Max(0, value)));
        }

        DateTimeOffset cutoff = sampleTime - _window;
        foreach ((string path, Queue<(DateTimeOffset Time, long Value)> queue) in _samples.ToList())
        {
            while (queue.TryPeek(out var sample) && sample.Time < cutoff)
            {
                queue.Dequeue();
            }

            if (queue.Count == 0)
            {
                _samples.Remove(path);
            }
        }

        return _samples
            .Where(static pair => pair.Value.Count > 0)
            .ToDictionary(
                static pair => pair.Key,
                static pair => pair.Value.Max(static sample => sample.Value),
                StringComparer.OrdinalIgnoreCase);
    }
}

public interface IApplicationInventoryService : IAsyncDisposable
{
    IReadOnlyList<GpuAdapterInfo> Adapters { get; }

    IAsyncEnumerable<IReadOnlyList<ExecutableGpuUsage>> MonitorAsync(
        TimeSpan interval,
        CancellationToken cancellationToken);
}
