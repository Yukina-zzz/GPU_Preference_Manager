using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Windows;
using System.Windows.Data;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Applications;
using GpuPreferenceManager.Core.History;
using GpuPreferenceManager.Windows.Diagnostics;
using GpuPreferenceManager.Windows.Storage;

namespace GpuPreferenceManager.App.ViewModels;

public sealed partial class MainViewModel : ObservableObject, IAsyncDisposable
{
    private readonly IApplicationInventoryService _inventory;
    private readonly IGpuPreferenceChangeService _changes;
    private readonly IHistoryStore _history;
    private readonly IRollbackService _rollback;
    private readonly IIgnoredApplicationStore _ignored;
    private readonly SettingsService _settingsService;
    private readonly DiagnosticsExportService _diagnostics;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, ApplicationRowViewModel> _rowMap = new(StringComparer.OrdinalIgnoreCase);
    private Task? _monitorTask;
    private AppSettings _settings;

    [ObservableProperty]
    private bool _isPaused;

    [ObservableProperty]
    private string _status = "正在初始化…";

    [ObservableProperty]
    private string _searchText = string.Empty;

    [ObservableProperty]
    private ApplicationRowViewModel? _selectedItem;

    [ObservableProperty]
    private HistoryEntry? _selectedHistory;

    [ObservableProperty]
    private int _selectedCount;

    [ObservableProperty]
    private bool _hasWritableSelection;

    [ObservableProperty]
    private int _excludedAdapterCount;

    [ObservableProperty]
    private int _usageFilterIndex;

    [ObservableProperty]
    private bool _isCustomUsageFilter;

    [ObservableProperty]
    private int _customFilterTargetIndex;

    [ObservableProperty]
    private double _customFilterMinimumMiB = 10;

    public MainViewModel(
        IApplicationInventoryService inventory,
        IGpuPreferenceChangeService changes,
        IHistoryStore history,
        IRollbackService rollback,
        IIgnoredApplicationStore ignored,
        SettingsService settingsService,
        DiagnosticsExportService diagnostics,
        AppSettings settings)
    {
        _inventory = inventory;
        _changes = changes;
        _history = history;
        _rollback = rollback;
        _ignored = ignored;
        _settingsService = settingsService;
        _diagnostics = diagnostics;
        _settings = settings;

        PendingRows = CreateView(ApplicationCategory.Pending);
        AssignedRows = CreateView(ApplicationCategory.Assigned);
        AllRows = CreateView(null);
        IgnoredRows = CreateView(ApplicationCategory.Ignored);
        ExceptionalRows = CreateView(ApplicationCategory.Exceptional);
        ApplyIntegratedCommand = new AsyncRelayCommand(
            () => ApplySpecificAsync(GpuAdapterRole.IntegratedOrPowerSaving),
            () => CanApplySpecific(GpuAdapterRole.IntegratedOrPowerSaving));
        ApplyDiscreteCommand = new AsyncRelayCommand(
            () => ApplySpecificAsync(GpuAdapterRole.DiscreteOrHighPerformance),
            () => CanApplySpecific(GpuAdapterRole.DiscreteOrHighPerformance));
        ApplyPowerSavingCommand = new AsyncRelayCommand(() => ApplyAsync(GpuPreferenceTarget.GenericPowerSaving, null), CanApplyGeneric);
        ApplyHighPerformanceCommand = new AsyncRelayCommand(() => ApplyAsync(GpuPreferenceTarget.GenericHighPerformance, null), CanApplyGeneric);
        ClearPreferenceCommand = new AsyncRelayCommand(() => ApplyAsync(GpuPreferenceTarget.WindowsDecides, null), CanApplyGeneric);
        IgnoreCommand = new AsyncRelayCommand(() => SetIgnoredAsync(true), HasSelection);
        UnignoreCommand = new AsyncRelayCommand(() => SetIgnoredAsync(false), HasSelection);
        RefreshHistoryCommand = new AsyncRelayCommand(RefreshHistoryAsync);
        UndoLatestCommand = new AsyncRelayCommand(UndoLatestAsync);
        RollbackToSelectedCommand = new AsyncRelayCommand(RollbackToSelectedAsync);
        RestoreBaselineCommand = new AsyncRelayCommand(RestoreBaselineAsync);
        ExportDiagnosticsCommand = new AsyncRelayCommand(ExportDiagnosticsAsync);
    }

    public ObservableCollection<ApplicationRowViewModel> Rows { get; } = [];

    public ObservableCollection<GpuAdapterInfo> Adapters { get; } = [];

    public ObservableCollection<AdapterCardViewModel> AdapterCards { get; } = [];

    public ObservableCollection<HistoryEntry> History { get; } = [];

    public ICollectionView PendingRows { get; }

    public ICollectionView AssignedRows { get; }

    public ICollectionView AllRows { get; }

    public ICollectionView IgnoredRows { get; }

    public ICollectionView ExceptionalRows { get; }

    public IAsyncRelayCommand ApplyIntegratedCommand { get; }

    public IAsyncRelayCommand ApplyDiscreteCommand { get; }

    public IAsyncRelayCommand ApplyPowerSavingCommand { get; }

    public IAsyncRelayCommand ApplyHighPerformanceCommand { get; }

    public IAsyncRelayCommand ClearPreferenceCommand { get; }

    public IAsyncRelayCommand IgnoreCommand { get; }

    public IAsyncRelayCommand UnignoreCommand { get; }

    public IAsyncRelayCommand RefreshHistoryCommand { get; }

    public IAsyncRelayCommand UndoLatestCommand { get; }

    public IAsyncRelayCommand RollbackToSelectedCommand { get; }

    public IAsyncRelayCommand RestoreBaselineCommand { get; }

    public IAsyncRelayCommand ExportDiagnosticsCommand { get; }

    public AppSettings Settings => _settings;

    public void Start()
    {
        _monitorTask ??= MonitorAsync(_shutdown.Token);
        _ = RefreshHistoryAsync();
    }

    public async ValueTask DisposeAsync()
    {
        _shutdown.Cancel();
        if (_monitorTask is not null)
        {
            try
            {
                await _monitorTask;
            }
            catch (OperationCanceledException)
            {
            }
        }

        await _inventory.DisposeAsync();
        _shutdown.Dispose();
    }

    public async Task SaveWindowSettingsAsync(double width, double height, double left, double top)
    {
        _settings = _settings with
        {
            WindowWidth = width,
            WindowHeight = height,
            WindowLeft = left,
            WindowTop = top,
        };
        await _settingsService.SaveAsync(_settings, CancellationToken.None);
    }

    partial void OnSearchTextChanged(string value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            foreach (ApplicationRowViewModel row in Rows.Where(row => row.Processes.Any(process =>
                         process.DisplayName.Contains(value, StringComparison.OrdinalIgnoreCase)
                         || process.ProcessName.Contains(value, StringComparison.OrdinalIgnoreCase)
                         || process.ExecutablePath.Contains(value, StringComparison.OrdinalIgnoreCase)
                         || process.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(value, StringComparison.OrdinalIgnoreCase))))
            {
                row.AreProcessesExpanded = true;
            }
        }

        RefreshViews();
    }

    partial void OnUsageFilterIndexChanged(int value)
    {
        IsCustomUsageFilter = value == 4;
        RefreshViews();
    }

    partial void OnCustomFilterTargetIndexChanged(int value) => RefreshViews();

    partial void OnCustomFilterMinimumMiBChanged(double value) => RefreshViews();

    partial void OnSelectedItemChanged(ApplicationRowViewModel? oldValue, ApplicationRowViewModel? newValue)
    {
        if (oldValue is not null)
        {
            oldValue.IsDetailSelected = false;
        }

        if (newValue is not null)
        {
            newValue.IsDetailSelected = true;
        }
    }

    private ListCollectionView CreateView(ApplicationCategory? category)
    {
        ListCollectionView view = new(Rows);
        view.Filter = item => item is ApplicationRowViewModel row
            && (category is null || row.Category == category)
            && MatchesUsageFilter(row)
            && (string.IsNullOrWhiteSpace(SearchText) || row.MatchesSearch(SearchText));
        PropertyGroupDescription sectionGroup = new(nameof(ApplicationRowViewModel.ProcessSection));
        sectionGroup.GroupNames.Add("前台应用");
        sectionGroup.GroupNames.Add("后台进程");
        view.GroupDescriptions.Add(sectionGroup);
        view.SortDescriptions.Add(new SortDescription(nameof(ApplicationRowViewModel.TotalDedicatedBytes), ListSortDirection.Descending));
        if (view.CanChangeLiveFiltering)
        {
            view.LiveFilteringProperties.Add(nameof(ApplicationRowViewModel.Category));
            view.LiveFilteringProperties.Add(nameof(ApplicationRowViewModel.TotalDedicatedBytes));
            view.LiveFilteringProperties.Add(nameof(ApplicationRowViewModel.DedicatedBytes));
            view.IsLiveFiltering = true;
        }

        if (view.CanChangeLiveGrouping)
        {
            view.LiveGroupingProperties.Add(nameof(ApplicationRowViewModel.ProcessSection));
            view.IsLiveGrouping = true;
        }

        if (view.CanChangeLiveSorting)
        {
            view.LiveSortingProperties.Add(nameof(ApplicationRowViewModel.TotalDedicatedBytes));
            view.LiveSortingProperties.Add(nameof(ApplicationRowViewModel.DisplayName));
            view.LiveSortingProperties.Add(nameof(ApplicationRowViewModel.ActualGpuTable));
            view.LiveSortingProperties.Add(nameof(ApplicationRowViewModel.Preference));
            view.IsLiveSorting = true;
        }

        return view;
    }

    private async Task MonitorAsync(CancellationToken cancellationToken)
    {
        try
        {
            await foreach (IReadOnlyList<ExecutableGpuUsage> snapshot in _inventory.MonitorAsync(
                               TimeSpan.FromSeconds(_settings.SamplingIntervalSeconds),
                               cancellationToken))
            {
                if (IsPaused)
                {
                    continue;
                }

                await Application.Current.Dispatcher.InvokeAsync(() => ApplySnapshot(snapshot));
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            Status = $"GPU 采样不可用：{exception.Message}；注册表管理仍可使用。";
        }
    }

    private void ApplySnapshot(IReadOnlyList<ExecutableGpuUsage> snapshot)
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        foreach (ExecutableGpuUsage item in snapshot)
        {
            seen.Add(item.ExecutablePath);
            if (!_rowMap.TryGetValue(item.ExecutablePath, out ApplicationRowViewModel? row))
            {
                row = new() { ExecutablePath = item.ExecutablePath };
                row.PropertyChanged += (_, args) =>
                {
                    if (args.PropertyName == nameof(ApplicationRowViewModel.IsSelected))
                    {
                        if (row.IsSelected)
                        {
                            SelectedItem = row;
                        }

                        UpdateSelectionState();
                        NotifyCommandStates();
                    }
                    else if (args.PropertyName == nameof(ApplicationRowViewModel.SelectedPreferenceTarget)
                        || args.PropertyName == nameof(ApplicationRowViewModel.HasReadablePath))
                    {
                        NotifyCommandStates();
                    }
                };
                _rowMap[item.ExecutablePath] = row;
                Rows.Add(row);
            }

            row.Update(item, _inventory.Adapters);
        }

        foreach (ApplicationRowViewModel row in Rows.ToList())
        {
            if (!seen.Contains(row.ExecutablePath) && ++row.MissingSamples >= 2)
            {
                Rows.Remove(row);
                _rowMap.Remove(row.ExecutablePath);
            }
        }

        Adapters.Clear();
        AdapterCards.Clear();
        ExcludedAdapterCount = 0;
        foreach (GpuAdapterInfo adapter in _inventory.Adapters)
        {
            Adapters.Add(adapter);
            if (adapter.Role == GpuAdapterRole.Excluded)
            {
                ExcludedAdapterCount++;
            }
            else
            {
                AdapterCards.Add(new AdapterCardViewModel(adapter));
            }
        }

        UpdateSelectionState();
        Status = $"运行中 · {DateTimeOffset.Now:T} · {Rows.Count} 个程序";
        NotifyCommandStates();
    }

    private bool MatchesUsageFilter(ApplicationRowViewModel row) => UsageFilterIndex switch
    {
        1 => row.DedicatedBytes >= 100 * 1024,
        2 => row.DedicatedBytes >= 10L * 1024 * 1024,
        3 => true,
        4 => (CustomFilterTargetIndex == 1 ? row.DedicatedBytes : row.TotalDedicatedBytes) >= CustomFilterThresholdBytes,
        _ => row.TotalDedicatedBytes >= 100 * 1024,
    };

    private long CustomFilterThresholdBytes => double.IsFinite(CustomFilterMinimumMiB)
        ? checked((long)(Math.Clamp(CustomFilterMinimumMiB, 0, 1024 * 1024) * 1024 * 1024))
        : 0;

    private async Task ApplySpecificAsync(GpuAdapterRole role)
    {
        GpuAdapterInfo? adapter = Adapters.SingleOrDefault(item => item.Role == role && item.IsAssignable);
        if (adapter is null)
        {
            Status = "没有身份唯一且可分配的目标适配器。";
            return;
        }

        await ApplyAsync(GpuPreferenceTarget.SpecificAdapter, adapter.SpecificAdapterKey);
    }

    private async Task ApplyAsync(GpuPreferenceTarget target, string? key)
    {
        string[] paths = Rows
            .Where(static row => row.IsSelected && row.HasReadablePath)
            .Select(static row => row.EffectivePreferencePath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        ChangeResult result = await _changes.ApplyPreferenceAsync(paths, target, key, CancellationToken.None);
        Status = result.Message;
        await RefreshHistoryAsync();
    }

    private async Task SetIgnoredAsync(bool ignored)
    {
        foreach (ApplicationRowViewModel row in Rows.Where(static row => row.IsSelected).ToList())
        {
            await _ignored.SetIgnoredAsync(row.ExecutablePath, ignored, CancellationToken.None);
            row.IsSelected = false;
        }

        Status = ignored ? "已加入忽略列表。" : "已取消忽略。";
    }

    private async Task RefreshHistoryAsync()
    {
        IReadOnlyList<HistoryEntry> entries = await _history.QueryAsync(CancellationToken.None);
        await Application.Current.Dispatcher.InvokeAsync(() =>
        {
            History.Clear();
            foreach (HistoryEntry entry in entries)
            {
                History.Add(entry);
            }
        });
    }

    private async Task UndoLatestAsync()
    {
        HistoryEntry? latest = History.FirstOrDefault(static entry => entry.Status == TransactionStatus.Applied);
        if (latest is null)
        {
            Status = "没有可撤销的已应用事务。";
            return;
        }

        ChangeResult result = await _rollback.UndoAsync(latest.Id, ConflictPolicy.Stop, CancellationToken.None);
        Status = result.Message;
        await RefreshHistoryAsync();
    }

    private async Task RollbackToSelectedAsync()
    {
        if (SelectedHistory is null)
        {
            Status = "请先选择一个历史节点。";
            return;
        }

        ChangeResult result = await _rollback.RollbackToAsync(SelectedHistory.Id, ConflictPolicy.Stop, CancellationToken.None);
        Status = result.Message;
        await RefreshHistoryAsync();
    }

    private async Task RestoreBaselineAsync()
    {
        ChangeResult result = await _rollback.RestoreBaselineAsync(ConflictPolicy.Stop, CancellationToken.None);
        Status = result.Message;
        await RefreshHistoryAsync();
    }

    private async Task ExportDiagnosticsAsync()
    {
        string path = await _diagnostics.ExportAsync(CancellationToken.None);
        Status = $"诊断包已导出：{path}";
    }

    private void RefreshViews()
    {
        PendingRows.Refresh();
        AssignedRows.Refresh();
        AllRows.Refresh();
        IgnoredRows.Refresh();
        ExceptionalRows.Refresh();
    }

    private bool HasSelection() => Rows.Any(static row => row.IsSelected);

    private bool CanApplyGeneric() => Rows.Any(static row =>
        row.IsSelected && row.HasReadablePath);

    private bool CanApplySpecific(GpuAdapterRole role) => CanApplyGeneric()
        && Adapters.Count(item => item.Role == role && item.IsAssignable) == 1;

    private void UpdateSelectionState()
    {
        SelectedCount = Rows.Count(static row => row.IsSelected);
        HasWritableSelection = Rows.Any(static row => row.IsSelected && row.HasReadablePath);
    }

    private void NotifyCommandStates()
    {
        ApplyIntegratedCommand.NotifyCanExecuteChanged();
        ApplyDiscreteCommand.NotifyCanExecuteChanged();
        ApplyPowerSavingCommand.NotifyCanExecuteChanged();
        ApplyHighPerformanceCommand.NotifyCanExecuteChanged();
        ClearPreferenceCommand.NotifyCanExecuteChanged();
        IgnoreCommand.NotifyCanExecuteChanged();
        UnignoreCommand.NotifyCanExecuteChanged();
    }

}
