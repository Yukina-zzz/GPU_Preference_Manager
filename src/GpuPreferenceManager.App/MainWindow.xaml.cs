using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using GpuPreferenceManager.App.ViewModels;
using GpuPreferenceManager.Core.Applications;

namespace GpuPreferenceManager.App;

public partial class MainWindow : Window
{
    private readonly MainViewModel _viewModel;
    private bool _shutdownCompleted;

    public MainWindow(MainViewModel viewModel)
    {
        InitializeComponent();
        _viewModel = viewModel;
        DataContext = viewModel;
        Width = viewModel.Settings.WindowWidth;
        Height = viewModel.Settings.WindowHeight;
        if (viewModel.Settings.WindowLeft is double left && viewModel.Settings.WindowTop is double top)
        {
            Left = left;
            Top = top;
        }

        Loaded += (_, _) => _viewModel.Start();
        Closing += OnClosing;
    }

    private void OnPreferenceMenuClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is Button { ContextMenu: { } menu } button)
        {
            menu.PlacementTarget = button;
            menu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
            menu.IsOpen = true;
        }
    }

    private void OnApplicationGridLoadingRow(object sender, DataGridRowEventArgs eventArgs)
    {
        if (eventArgs.Row.DataContext is ApplicationRowViewModel row)
        {
            eventArgs.Row.DetailsVisibility = row.AreProcessesExpanded ? Visibility.Visible : Visibility.Collapsed;
        }

        eventArgs.Row.ContextMenu ??= new ContextMenu();
        eventArgs.Row.ContextMenu.Opened -= OnApplicationRowContextMenuOpened;
        eventArgs.Row.ContextMenu.Opened += OnApplicationRowContextMenuOpened;
        eventArgs.Row.ContextMenu.Closed -= OnApplicationRowContextMenuClosed;
        eventArgs.Row.ContextMenu.Closed += OnApplicationRowContextMenuClosed;
        eventArgs.Row.PreviewMouseRightButtonDown -= OnApplicationRowRightClick;
        eventArgs.Row.PreviewMouseRightButtonDown += OnApplicationRowRightClick;
    }

    private void OnApplicationRowRightClick(object sender, MouseButtonEventArgs eventArgs)
    {
        if (sender is DataGridRow { DataContext: ApplicationRowViewModel row } gridRow)
        {
            gridRow.IsSelected = true;
            _viewModel.SelectedItem = row;
        }
    }

    private void OnApplicationRowContextMenuOpened(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not ContextMenu { PlacementTarget: DataGridRow { DataContext: ApplicationRowViewModel row } } menu)
        {
            return;
        }

        _viewModel.SetRowContextMenuOpen(true);
        menu.Items.Clear();
        MenuItem preference = new() { Header = "设置偏好" };
        if (row.PreferenceTargets.Count <= 1)
        {
            string target = row.PreferenceTargets.Count == 1
                ? row.PreferenceTargets[0].ExecutablePath
                : row.ExecutablePath;
            AddPreferenceActions(preference, row, target);
        }
        else
        {
            foreach (PreferenceTargetViewModel target in row.PreferenceTargets)
            {
                MenuItem targetMenu = new()
                {
                    Header = target.Label,
                    ToolTip = target.ExecutablePath,
                };
                AddPreferenceActions(targetMenu, row, target.ExecutablePath);
                preference.Items.Add(targetMenu);
            }
        }

        menu.Items.Add(preference);
        menu.Items.Add(new Separator());
        bool ignored = row.Category == ApplicationCategory.Ignored;
        menu.Items.Add(new MenuItem
        {
            Header = ignored ? "取消忽略" : "忽略",
            ToolTip = "忽略仅影响本工具中的列表分类，不修改 Windows GPU 偏好。",
            Command = _viewModel.SetSingleIgnoredCommand,
            CommandParameter = new SingleIgnoreRequest(row, !ignored),
        });
    }

    private void OnApplicationRowContextMenuClosed(object sender, RoutedEventArgs eventArgs) =>
        _viewModel.SetRowContextMenuOpen(false);

    private void AddPreferenceActions(ItemsControl parent, ApplicationRowViewModel row, string executablePath)
    {
        AddPreferenceAction(parent, row, executablePath, "指定节能 GPU", SinglePreferenceAction.SpecificPowerSaving,
            "需要一张身份唯一且可分配的节能 GPU。");
        AddPreferenceAction(parent, row, executablePath, "指定高性能 GPU", SinglePreferenceAction.SpecificHighPerformance,
            "需要一张身份唯一且可分配的高性能 GPU。");
        parent.Items.Add(new Separator());
        AddPreferenceAction(parent, row, executablePath, "通用节能", SinglePreferenceAction.GenericPowerSaving);
        AddPreferenceAction(parent, row, executablePath, "通用高性能", SinglePreferenceAction.GenericHighPerformance);
        parent.Items.Add(new Separator());
        AddPreferenceAction(parent, row, executablePath, "清除偏好（Windows 决定）", SinglePreferenceAction.WindowsDecides);
    }

    private void AddPreferenceAction(
        ItemsControl parent,
        ApplicationRowViewModel row,
        string executablePath,
        string header,
        SinglePreferenceAction action,
        string? toolTip = null)
    {
        MenuItem item = new()
        {
            Header = header,
            ToolTip = executablePath.StartsWith("<pid:", StringComparison.Ordinal)
                ? "Windows 未提供完整 EXE 路径，不能安全写入偏好。"
                : toolTip ?? executablePath,
            Command = _viewModel.ApplySinglePreferenceCommand,
            CommandParameter = new SinglePreferenceRequest(row, executablePath, action),
        };
        ToolTipService.SetShowOnDisabled(item, true);
        parent.Items.Add(item);
    }

    private void OnApplicationGridSizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (!eventArgs.WidthChanged || sender is not DataGrid { Columns.Count: 5 } grid || grid.ActualWidth < 1)
        {
            return;
        }

        const double selectionWidth = 54;
        double usableWidth = Math.Max(500, grid.ActualWidth - SystemParameters.VerticalScrollBarWidth - selectionWidth - 4);
        double[] minimumWidths = [160, 200, 140, 150];
        double minimumTotal = minimumWidths.Sum();
        double[] weights = [0.30, 0.35, 0.15, 0.20];

        grid.Columns[0].Width = new DataGridLength(selectionWidth, DataGridLengthUnitType.Pixel);
        if (usableWidth <= minimumTotal)
        {
            double scale = usableWidth / minimumTotal;
            for (int index = 0; index < minimumWidths.Length; index++)
            {
                grid.Columns[index + 1].Width = new DataGridLength(
                    minimumWidths[index] * scale,
                    DataGridLengthUnitType.Pixel);
            }

            return;
        }

        double extraWidth = usableWidth - minimumTotal;
        for (int index = 0; index < minimumWidths.Length; index++)
        {
            grid.Columns[index + 1].Width = new DataGridLength(
                minimumWidths[index] + extraWidth * weights[index],
                DataGridLengthUnitType.Pixel);
        }
    }

    private void OnProcessToggleClick(object sender, RoutedEventArgs eventArgs)
    {
        if (sender is not ToggleButton button)
        {
            return;
        }

        DependencyObject? current = button;
        while (current is not null and not DataGridRow)
        {
            current = VisualTreeHelper.GetParent(current);
        }

        if (current is DataGridRow row)
        {
            row.DetailsVisibility = button.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        }
    }

    private async void OnClosing(object? sender, CancelEventArgs eventArgs)
    {
        if (_shutdownCompleted)
        {
            return;
        }

        eventArgs.Cancel = true;
        await _viewModel.SaveWindowSettingsAsync(Width, Height, Left, Top);
        await _viewModel.DisposeAsync();
        _shutdownCompleted = true;
        Close();
    }
}
