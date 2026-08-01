using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using GpuPreferenceManager.App.ViewModels;

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
