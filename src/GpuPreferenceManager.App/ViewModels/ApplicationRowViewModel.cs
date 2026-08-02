using CommunityToolkit.Mvvm.ComponentModel;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.Registry;

namespace GpuPreferenceManager.App.ViewModels;

public sealed partial class ApplicationRowViewModel : ObservableObject
{
    private const long DisplayUsageThresholdBytes = 100 * 1024;
    private IReadOnlyList<GpuAdapterInfo> _currentAdapters = [];

    [ObservableProperty]
    private bool _isSelected;

    [ObservableProperty]
    private bool _isDetailSelected;

    [ObservableProperty]
    private bool _areProcessesExpanded;

    [ObservableProperty]
    private string _displayName = string.Empty;

    [ObservableProperty]
    private int _processCount;

    [ObservableProperty]
    private long _dedicatedBytes;

    [ObservableProperty]
    private long _otherDedicatedBytes;

    [ObservableProperty]
    private long _sharedBytes;

    [ObservableProperty]
    private long _peakBytes;

    [ObservableProperty]
    private string _engine = string.Empty;

    [ObservableProperty]
    private string _preference = string.Empty;

    [ObservableProperty]
    private string _preferenceSummary = string.Empty;

    [ObservableProperty]
    private string _actualGpu = "尚未检测到 GPU 活动";

    [ObservableProperty]
    private string _actualGpuTable = "无 GPU 活动";

    [ObservableProperty]
    private string _adapterUsageDetails = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<AdapterUsageDetailViewModel> _adapterUsages = [];

    [ObservableProperty]
    private string _processDetails = string.Empty;

    [ObservableProperty]
    private string _rowStatus = string.Empty;

    [ObservableProperty]
    private string _rawRule = string.Empty;

    [ObservableProperty]
    private IReadOnlyList<ProcessRowViewModel> _processes = [];

    [ObservableProperty]
    private IReadOnlyList<PreferenceTargetViewModel> _preferenceTargets = [];

    [ObservableProperty]
    private PreferenceTargetViewModel? _selectedPreferenceTarget;

    [ObservableProperty]
    private ApplicationCategory _category;

    [ObservableProperty]
    private DateTimeOffset _lastSeenUtc;

    [ObservableProperty]
    private bool _isForegroundApplication;

    public required string ExecutablePath { get; init; }

    public int MissingSamples { get; set; }

    public string EffectivePreferencePath => SelectedPreferenceTarget?.ExecutablePath ?? ExecutablePath;

    public bool HasReadablePath => !EffectivePreferencePath.StartsWith("<pid:", StringComparison.Ordinal);

    public bool HasMultiplePreferenceTargets => PreferenceTargets.Count > 1;

    public bool HasMultipleProcesses => ProcessCount > 1;

    public string GroupDisplayName => HasMultipleProcesses ? $"{DisplayName} ({ProcessCount})" : DisplayName;

    public string ProcessSection => IsForegroundApplication ? "前台应用" : "后台进程";

    public string DedicatedDisplay => FormatBytes(DedicatedBytes);

    public string OtherDedicatedDisplay => FormatBytes(OtherDedicatedBytes);

    public string SharedDisplay => FormatBytes(SharedBytes);

    public string PeakDisplay => FormatBytes(PeakBytes);

    public long TotalDedicatedBytes => DedicatedBytes + OtherDedicatedBytes;

    public string MemorySummary => BuildMemorySummary(DedicatedBytes, OtherDedicatedBytes);

    public string RelatedProcessesHeader => $"相关进程（{ProcessCount}）";

    public void Update(ExecutableGpuUsage source, IReadOnlyList<GpuAdapterInfo> adapters)
    {
        _currentAdapters = adapters;
        string? selectedTargetPath = SelectedPreferenceTarget?.ExecutablePath;
        Dictionary<GpuAdapterId, GpuAdapterInfo> adapterById = adapters.ToDictionary(static adapter => adapter.Id);
        GpuAdapterId? highPerformanceAdapter = adapters.FirstOrDefault(
            static adapter => adapter.Role == GpuAdapterRole.DiscreteOrHighPerformance)?.Id;
        DisplayName = source.DisplayName;
        ProcessCount = source.Processes.Count;
        Processes = BuildProcessRows(source, adapters, adapterById);
        List<PreferenceTargetViewModel> nextPreferenceTargets = BuildPreferenceTargets(source);
        if (!PreferenceTargetsEquivalent(PreferenceTargets, nextPreferenceTargets))
        {
            PreferenceTargets = nextPreferenceTargets;
        }
        SelectedPreferenceTarget = PreferenceTargets.FirstOrDefault(target =>
                string.Equals(target.ExecutablePath, selectedTargetPath, StringComparison.OrdinalIgnoreCase))
            ?? PreferenceTargets.FirstOrDefault(target =>
                string.Equals(target.ExecutablePath, source.ExecutablePath, StringComparison.OrdinalIgnoreCase))
            ?? (PreferenceTargets.Count > 0 ? PreferenceTargets[0] : null);
        string[] preferenceDescriptions = PreferenceTargets
            .Select(target => FormatPreference(target.Rule, adapters))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        PreferenceSummary = preferenceDescriptions.Length switch
        {
            0 => "Windows 决定",
            1 => preferenceDescriptions[0],
            _ => $"多个 EXE：{preferenceDescriptions.Length} 种规则",
        };
        DedicatedBytes = highPerformanceAdapter is not null
            && source.AdapterUsages.TryGetValue(highPerformanceAdapter.Value, out AggregatedAdapterUsage? highUsage)
            && IsMeaningfulUsage(highUsage)
                ? highUsage.DedicatedBytes
                : 0;
        OtherDedicatedBytes = source.AdapterUsages
            .Where(pair => (highPerformanceAdapter is null || pair.Key != highPerformanceAdapter.Value)
                && IsDisplayableAdapter(pair.Key, adapterById)
                && IsMeaningfulUsage(pair.Value))
            .Sum(static pair => pair.Value.DedicatedBytes);
        SharedBytes = source.AdapterUsages.Values.Sum(static usage => usage.SharedBytes);
        PeakBytes = source.PeakDedicatedBytes30Seconds;
        Engine = source.AdapterUsages.Values.SelectMany(static usage => usage.EngineUtilization)
            .OrderByDescending(static engine => engine.Value)
            .Select(static engine => $"{engine.Key} {engine.Value:F1}%")
            .FirstOrDefault() ?? "无活动引擎";
        Category = source.Category;
        LastSeenUtc = source.LastSeenUtc;
        IsForegroundApplication = source.IsForegroundApplication;
        ProcessDetails = $"{source.Processes.Count} 个相关进程（按进程树归组）\nPID：{string.Join("、", source.Processes.Select(static process => process.ProcessId))}";
        RowStatus = FormatStatus(source);

        List<(string Name, AggregatedAdapterUsage Usage)> active = source.AdapterUsages
            .Where(pair => IsDisplayableAdapter(pair.Key, adapterById)
                && IsMeaningfulUsage(pair.Value))
            .OrderByDescending(static pair => pair.Value.DedicatedBytes + pair.Value.SharedBytes)
            .Select(pair => (
                adapterById.TryGetValue(pair.Key, out GpuAdapterInfo? adapter)
                    ? adapter.Name
                    : $"未知适配器 LUID {pair.Key.LuidHighPart:X8}:{pair.Key.LuidLowPart:X8}",
                pair.Value))
            .ToList();
        ActualGpu = active.Count switch
        {
            0 => "尚未检测到 GPU 活动",
            1 => active[0].Name,
            _ => $"同时使用 {active.Count} 张：{string.Join(" / ", active.Select(static item => item.Name))}",
        };
        ActualGpuTable = active.Count == 0
            ? "无 GPU 活动"
            : string.Join("\n", active.Select(static item => item.Name));
        AdapterUsageDetails = active.Count == 0
            ? "当前样本没有可显示的适配器占用。"
            : string.Join(
                "\n\n",
                active.Select(static item =>
                    $"{item.Name}\n专用 {FormatBytes(item.Usage.DedicatedBytes)}  ·  共享 {FormatBytes(item.Usage.SharedBytes)}  ·  {FormatEngine(item.Usage)}"));
        AdapterUsages = active.Select(static item => new AdapterUsageDetailViewModel(
            item.Name,
            $"专用 {FormatBytes(item.Usage.DedicatedBytes)}",
            $"共享 {FormatBytes(item.Usage.SharedBytes)}",
            FormatEngine(item.Usage))).ToList();
        MissingSamples = 0;
        OnPropertyChanged(nameof(HasReadablePath));
        OnPropertyChanged(nameof(HasMultipleProcesses));
        OnPropertyChanged(nameof(GroupDisplayName));
        OnPropertyChanged(nameof(DedicatedDisplay));
        OnPropertyChanged(nameof(OtherDedicatedDisplay));
        OnPropertyChanged(nameof(SharedDisplay));
        OnPropertyChanged(nameof(PeakDisplay));
        OnPropertyChanged(nameof(TotalDedicatedBytes));
        OnPropertyChanged(nameof(MemorySummary));
        OnPropertyChanged(nameof(HasMultiplePreferenceTargets));
        OnPropertyChanged(nameof(RelatedProcessesHeader));
    }

    partial void OnSelectedPreferenceTargetChanged(PreferenceTargetViewModel? value)
    {
        GpuPreferenceRule rule = value?.Rule ?? RegistryRuleParser.Parse(null);
        Preference = FormatPreference(rule, _currentAdapters);
        RawRule = string.IsNullOrEmpty(rule.RawValue) ? "（无注册表规则）" : rule.RawValue;
        OnPropertyChanged(nameof(EffectivePreferencePath));
        OnPropertyChanged(nameof(HasReadablePath));
    }

    partial void OnIsForegroundApplicationChanged(bool value) => OnPropertyChanged(nameof(ProcessSection));

    public bool MatchesSearch(string searchText) => DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || ExecutablePath.Contains(searchText, StringComparison.OrdinalIgnoreCase)
        || Processes.Any(process => process.DisplayName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || process.ProcessName.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || process.ExecutablePath.Contains(searchText, StringComparison.OrdinalIgnoreCase)
            || process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(searchText, StringComparison.OrdinalIgnoreCase));

    private static List<ProcessRowViewModel> BuildProcessRows(
        ExecutableGpuUsage source,
        IReadOnlyList<GpuAdapterInfo> adapters,
        Dictionary<GpuAdapterId, GpuAdapterInfo> adapterById)
    {
        if (source.ProcessUsages is null)
        {
            return source.Processes
                .OrderBy(static process => process.ProcessId)
                .Select(process => new ProcessRowViewModel(
                    source.DisplayName,
                    System.IO.Path.GetFileName(source.ExecutablePath),
                    process.ProcessId,
                    FormatCreationTime(process.CreationTimeFileTime),
                    "无可用的逐进程 GPU 数据",
                    "—",
                    source.ExecutablePath))
                .ToList();
        }

        GpuAdapterId? highPerformanceAdapter = adapters.FirstOrDefault(
            static adapter => adapter.Role == GpuAdapterRole.DiscreteOrHighPerformance)?.Id;
        return source.ProcessUsages.Select(process =>
        {
            List<(string Name, AggregatedAdapterUsage Usage)> active = process.AdapterUsages
                .Where(pair => IsDisplayableAdapter(pair.Key, adapterById) && IsMeaningfulUsage(pair.Value))
                .Select(pair => (
                    adapterById.TryGetValue(pair.Key, out GpuAdapterInfo? adapter) ? adapter.Name : "未知适配器",
                    pair.Value))
                .ToList();
            long discrete = highPerformanceAdapter is not null
                && process.AdapterUsages.TryGetValue(highPerformanceAdapter.Value, out AggregatedAdapterUsage? usage)
                && IsMeaningfulUsage(usage)
                    ? usage.DedicatedBytes
                    : 0;
            long other = process.AdapterUsages
                .Where(pair => (highPerformanceAdapter is null || pair.Key != highPerformanceAdapter.Value)
                    && IsDisplayableAdapter(pair.Key, adapterById)
                    && IsMeaningfulUsage(pair.Value))
                .Sum(static pair => pair.Value.DedicatedBytes);
            return new ProcessRowViewModel(
                process.DisplayName,
                process.ProcessName,
                process.Process.ProcessId,
                FormatCreationTime(process.Process.CreationTimeFileTime),
                active.Count == 0 ? "无 GPU 活动" : string.Join(" / ", active.Select(static item => item.Name)),
                BuildMemorySummary(discrete, other),
                process.ExecutablePath ?? source.ExecutablePath);
        }).ToList();
    }

    private static List<PreferenceTargetViewModel> BuildPreferenceTargets(ExecutableGpuUsage source)
    {
        List<PreferenceTargetViewModel> targets =
        [
            new(
                source.ExecutablePath,
                System.IO.Path.GetFileName(source.ExecutablePath),
                source.DisplayName,
                source.Rule),
        ];
        if (source.ProcessUsages is not null)
        {
            targets.AddRange(source.ProcessUsages
                .Where(static process => !string.IsNullOrWhiteSpace(process.ExecutablePath))
                .Select(process => new PreferenceTargetViewModel(
                    process.ExecutablePath!,
                    process.ProcessName,
                    process.DisplayName,
                    process.Rule ?? RegistryRuleParser.Parse(null))));
        }

        return targets
            .Where(static target => !target.ExecutablePath.StartsWith("<pid:", StringComparison.Ordinal))
            .DistinctBy(static target => target.ExecutablePath, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    private static bool PreferenceTargetsEquivalent(
        IReadOnlyList<PreferenceTargetViewModel> current,
        List<PreferenceTargetViewModel> next) =>
        current.Count == next.Count
        && current.Zip(next).All(static pair =>
            string.Equals(pair.First.ExecutablePath, pair.Second.ExecutablePath, StringComparison.OrdinalIgnoreCase)
            && string.Equals(pair.First.DisplayName, pair.Second.DisplayName, StringComparison.Ordinal)
            && string.Equals(pair.First.ProcessName, pair.Second.ProcessName, StringComparison.Ordinal)
            && string.Equals(pair.First.Rule.RawValue, pair.Second.Rule.RawValue, StringComparison.Ordinal));

    private static string FormatPreference(GpuPreferenceRule rule, IReadOnlyList<GpuAdapterInfo> adapters) => rule.Kind switch
    {
        GpuPreferenceKind.NoRule or GpuPreferenceKind.WindowsDecides => "Windows 决定",
        GpuPreferenceKind.GenericPowerSaving => "通用节能",
        GpuPreferenceKind.GenericHighPerformance => "通用高性能",
        GpuPreferenceKind.SpecificAdapter => adapters
            .Where(adapter => SpecificAdapterKey.Matches(adapter.SpecificAdapterKey, rule.SpecificAdapterKey))
            .Select(static adapter => $"指定：{adapter.Name}")
            .FirstOrDefault() ?? $"指定适配器：{rule.SpecificAdapterKey}",
        _ => "未知或部分可识别规则",
    };

    private static string FormatStatus(ExecutableGpuUsage source)
    {
        if (!source.IsPathAccessible)
        {
            return "系统或受保护进程：无法读取完整 EXE 路径，因此不能写入应用偏好。";
        }

        return source.Category switch
        {
            ApplicationCategory.Ignored => "已在本工具中忽略；Windows 注册表未因此改变。",
            ApplicationCategory.Assigned => "已存在明确 GPU 偏好；修改后需重启目标程序。",
            ApplicationCategory.Exceptional => "规则包含未知字段或无法安全解释，请先查看详情。",
            _ => "尚未设置明确 GPU 偏好。",
        };
    }

    private static string FormatEngine(AggregatedAdapterUsage usage) => usage.EngineUtilization
        .OrderByDescending(static engine => engine.Value)
        .Select(static engine => $"{engine.Key} {engine.Value:F1}%")
        .FirstOrDefault() ?? "无引擎活动";

    private static string FormatCreationTime(long fileTime)
    {
        try
        {
            return DateTime.FromFileTimeUtc(fileTime).ToLocalTime().ToString("HH:mm:ss", System.Globalization.CultureInfo.CurrentCulture);
        }
        catch (ArgumentOutOfRangeException)
        {
            return "未知时间";
        }
    }

    private static bool IsDisplayableAdapter(
        GpuAdapterId id,
        Dictionary<GpuAdapterId, GpuAdapterInfo> adapterById) =>
        !adapterById.TryGetValue(id, out GpuAdapterInfo? adapter)
        || adapter.Role != GpuAdapterRole.Excluded;

    private static bool IsMeaningfulUsage(AggregatedAdapterUsage usage) =>
        usage.DedicatedBytes >= DisplayUsageThresholdBytes
        || usage.EngineUtilization.Values.Any(static value => value > 0.01);

    private static string BuildMemorySummary(long discrete, long other)
    {
        List<string> lines = [];
        if (discrete >= DisplayUsageThresholdBytes)
        {
            lines.Add($"独显 {FormatBytes(discrete)}");
        }

        if (other >= DisplayUsageThresholdBytes)
        {
            lines.Add($"其他 {FormatBytes(other)}");
        }

        return lines.Count == 0 ? "—" : string.Join("\n", lines);
    }

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024 * 1024
        ? $"{bytes / (1024d * 1024 * 1024):F2} GiB"
        : $"{bytes / (1024d * 1024):F1} MiB";
}

public sealed record ProcessRowViewModel(
    string DisplayName,
    string ProcessName,
    int ProcessId,
    string StartedAt,
    string ActualGpu,
    string Memory,
    string ExecutablePath)
{
    public string Identity => $"{ProcessName}  ·  PID {ProcessId}  ·  启动于 {StartedAt}";
}

public sealed record AdapterUsageDetailViewModel(
    string Name,
    string DedicatedMemory,
    string SharedMemory,
    string Engine);

public sealed record PreferenceTargetViewModel(
    string ExecutablePath,
    string ProcessName,
    string DisplayName,
    GpuPreferenceRule Rule)
{
    public string Label => string.Equals(ProcessName, DisplayName, StringComparison.OrdinalIgnoreCase)
        ? ProcessName
        : $"{DisplayName}（{ProcessName}）";
}
