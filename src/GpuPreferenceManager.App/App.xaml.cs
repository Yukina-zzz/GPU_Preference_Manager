using System.IO;
using System.Windows;
using GpuPreferenceManager.App.ViewModels;
using GpuPreferenceManager.Core.Adapters;
using GpuPreferenceManager.Core.Registry;
using GpuPreferenceManager.Windows.Adapters;
using GpuPreferenceManager.Windows.Diagnostics;
using GpuPreferenceManager.Windows.History;
using GpuPreferenceManager.Windows.Monitoring;
using GpuPreferenceManager.Windows.Processes;
using GpuPreferenceManager.Windows.Registry;
using GpuPreferenceManager.Windows.Storage;
using Serilog.Core;
using Wpf.Ui.Appearance;

namespace GpuPreferenceManager.App;

/// <summary>
/// 应用入口和服务组合根。
/// </summary>
public partial class App : Application, IDisposable
{
    private Logger? _logger;
    private GpuPreferenceChangeService? _changes;
    private RollbackService? _rollback;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            ApplicationDataPaths paths = ApplicationDataPaths.CreateDefault();
            paths.EnsureDirectories();
            _logger = DiagnosticLogging.CreateLogger("app");
            SettingsService settingsService = new(paths);
            AppSettings settings = await settingsService.LoadAsync(CancellationToken.None);
            ApplicationTheme theme = settings.Theme.ToUpperInvariant() switch
            {
                "DARK" => ApplicationTheme.Dark,
                "LIGHT" => ApplicationTheme.Light,
                _ => ApplicationThemeManager.GetSystemTheme() switch
                {
                    SystemTheme.Dark => ApplicationTheme.Dark,
                    SystemTheme.HCWhite or SystemTheme.HCBlack or SystemTheme.HC1 or SystemTheme.HC2 => ApplicationTheme.HighContrast,
                    _ => ApplicationTheme.Light,
                },
            };
            ApplicationThemeManager.Apply(theme);
            WindowsGpuPreferenceRegistry registry = new();
            SqliteHistoryStore history = new(paths);
            await history.InitializeAsync(CancellationToken.None);
            DxgiGpuAdapterEnumerator adapterEnumerator = new();
            RegistrySnapshot registrySnapshot = await registry.ReadSnapshotAsync(CancellationToken.None);
            IReadOnlyList<GpuAdapterInfo> startupAdapters = GpuAdapterMappingService.Map(
                adapterEnumerator.EnumerateAdapters(),
                registrySnapshot);
            await history.EnsureBaselineAsync(registrySnapshot, startupAdapters, CancellationToken.None);
            RegistryBackupService backup = new(paths);
            if (!Directory.EnumerateFiles(paths.BackupDirectory, "Initial_*.reg").Any())
            {
                await backup.ExportAsync(registrySnapshot, "Initial", CancellationToken.None);
            }

            IgnoredApplicationStore ignored = new(paths);
            ApplicationInventoryService inventory = new(
                adapterEnumerator,
                new WindowsProcessInfoProvider(),
                registry,
                ignored);
            inventory.ApplyAdapterOverrides(settings.EffectiveAdapterOverrides);
            _changes = new(registry, history, backup, () => inventory.Adapters.Count > 0 ? inventory.Adapters : startupAdapters);
            _rollback = new(registry, history, backup);
            await _rollback.RecoverPendingTransactionsAsync(CancellationToken.None);
            MainViewModel viewModel = new(
                inventory,
                _changes,
                history,
                _rollback,
                ignored,
                settingsService,
                new DiagnosticsExportService(paths, registry, adapterEnumerator),
                settings);
            MainWindow window = new(viewModel);
            MainWindow = window;
            window.Show();
        }
        catch (Exception exception)
        {
            _logger?.Error(exception, "应用启动失败");
            MessageBox.Show(
                $"GPU Preference Manager 启动失败：\n{exception.Message}",
                "启动失败",
                MessageBoxButton.OK,
                MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        Dispose();
        base.OnExit(e);
    }

    public void Dispose()
    {
        _rollback?.Dispose();
        _changes?.Dispose();
        _logger?.Dispose();
        GC.SuppressFinalize(this);
    }
}
