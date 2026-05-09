using System;
using System.Collections.Generic;
using System.ComponentModel;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class ScreenConfigDialog : Window
{
    readonly MainViewModel       _vm;
    readonly OutputEditViewModel _editVm = new();
    OutputState?                 _current;
    readonly List<Border>        _monitorBorders = new();
    int                          _appScreenIndex = -1;

    public ScreenConfigDialog(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        // Settings panel gets the edit VM; the ListBox inherits the window DataContext.
        SettingsPanel.DataContext = _editVm;
        OutputList.ItemsSource    = _vm.OutputStates;
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        // Detect which screen the main application window is on so we can block it.
        _appScreenIndex = -1;
        if (Screens is not null && Owner is Window owner)
        {
            var appScreen = Screens.ScreenFromPoint(owner.Position);
            var all = Screens.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Bounds == appScreen?.Bounds)
                {
                    _appScreenIndex = i;
                    break;
                }
            }
        }

        // Build the monitor list, flagging the app's screen.
        var monitors = new List<string>();
        if (Screens is not null)
        {
            int i = 1;
            foreach (var s in Screens.All)
            {
                var b = s.Bounds;
                string suffix = (i - 1) == _appScreenIndex ? "  (Main App)" : string.Empty;
                monitors.Add($"Monitor {i++}  ({b.Width}×{b.Height}){suffix}");
            }
        }
        if (monitors.Count == 0) monitors.Add("No monitors detected");
        _editVm.AvailableMonitors = monitors;

        // Build the visual layout after the first layout pass so bounds are available.
        Dispatcher.UIThread.Post(BuildMonitorLayout);
        _editVm.PropertyChanged += OnEditVmPropertyChanged;

        // Select first output.
        if (_vm.OutputStates.Count > 0)
            OutputList.SelectedIndex = 0;
    }

    // ── Monitor layout diagram ────────────────────────────────────────────────

    void OnEditVmPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(OutputEditViewModel.DisplayIndex))
        {
            // Don't allow selecting the main app's screen — snap to next available.
            if (_editVm.DisplayIndex == _appScreenIndex)
            {
                int count = _editVm.AvailableMonitors.Count;
                for (int i = 0; i < count; i++)
                {
                    if (i != _appScreenIndex)
                    {
                        _editVm.DisplayIndex = i;
                        return; // re-fires this handler with the corrected index
                    }
                }
                // Only one screen total — can't block it, fall through.
            }
            RefreshMonitorHighlight();
        }
        else if (e.PropertyName == nameof(OutputEditViewModel.IsDisplay) && _editVm.IsDisplay)
            Dispatcher.UIThread.Post(BuildMonitorLayout);
    }

    void BuildMonitorLayout()
    {
        _monitorBorders.Clear();

        var screens = Screens?.All;
        if (screens is null || screens.Count == 0)
        {
            MonitorLayoutHost.Child = null;
            return;
        }

        double hostW = MonitorLayoutHost.Bounds.Width > 1 ? MonitorLayoutHost.Bounds.Width : 460;
        double hostH = MonitorLayoutHost.Bounds.Height > 1 ? MonitorLayoutHost.Bounds.Height : 110;
        const double pad = 8;
        double availW = hostW - pad * 2;
        double availH = hostH - pad * 2;

        // Compute bounding box across all physical screens.
        double minX = double.MaxValue, minY = double.MaxValue;
        double maxX = double.MinValue, maxY = double.MinValue;
        foreach (var s in screens)
        {
            minX = Math.Min(minX, s.Bounds.X);
            minY = Math.Min(minY, s.Bounds.Y);
            maxX = Math.Max(maxX, s.Bounds.Right);
            maxY = Math.Max(maxY, s.Bounds.Bottom);
        }

        double totalW = maxX - minX;
        double totalH = maxY - minY;
        if (totalW <= 0 || totalH <= 0) return;

        double scale   = Math.Min(availW / totalW, availH / totalH);
        double offsetX = pad + (availW - totalW * scale) / 2;
        double offsetY = pad + (availH - totalH * scale) / 2;

        var canvas = new Canvas { Background = Brushes.Transparent };

        for (int i = 0; i < screens.Count; i++)
        {
            var b = screens[i].Bounds;
            double x = (b.X - minX) * scale + offsetX;
            double y = (b.Y - minY) * scale + offsetY;
            double w = Math.Max(b.Width  * scale, 32);
            double h = Math.Max(b.Height * scale, 22);

            bool isApp = i == _appScreenIndex;
            int  capturedIdx = i;

            string labelText = isApp
                ? (w >= 60 && h >= 36 ? $"Main App\n{b.Width}×{b.Height}" : "App")
                : (w >= 52 && h >= 36 ? $"{i + 1}\n{b.Width}×{b.Height}" : $"{i + 1}");

            var label = new TextBlock
            {
                Text                = labelText,
                FontSize            = 9,
                Foreground          = isApp
                    ? new SolidColorBrush(Color.Parse("#666666"))
                    : Brushes.White,
                TextAlignment       = Avalonia.Media.TextAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
            };

            var border = new Border
            {
                Width           = w,
                Height          = h,
                CornerRadius    = new Avalonia.CornerRadius(3),
                BorderThickness = new Avalonia.Thickness(2),
                Child           = label,
                Cursor          = new Cursor(isApp ? StandardCursorType.No : StandardCursorType.Hand),
                ClipToBounds    = true,
            };

            if (!isApp)
                border.PointerPressed += (_, _) => _editVm.DisplayIndex = capturedIdx;

            Canvas.SetLeft(border, x);
            Canvas.SetTop(border, y);
            canvas.Children.Add(border);
            _monitorBorders.Add(border);
        }

        MonitorLayoutHost.Child = canvas;
        RefreshMonitorHighlight();
    }

    void RefreshMonitorHighlight()
    {
        for (int i = 0; i < _monitorBorders.Count; i++)
        {
            if (i == _appScreenIndex)
            {
                _monitorBorders[i].BorderBrush = new SolidColorBrush(Color.Parse("#333333"));
                _monitorBorders[i].Background  = new SolidColorBrush(Color.Parse("#181818"));
            }
            else
            {
                bool active = i == _editVm.DisplayIndex;
                _monitorBorders[i].BorderBrush = new SolidColorBrush(
                    active ? Color.Parse("#4da6ff") : Color.Parse("#555555"));
                _monitorBorders[i].Background = new SolidColorBrush(
                    active ? Color.Parse("#1e3a5f") : Color.Parse("#2a2a2a"));
            }
        }
    }

    // ── Output list selection ─────────────────────────────────────────────────

    void OnOutputSelected(object? sender, SelectionChangedEventArgs e)
    {
        // Commit edits to the previously selected output.
        CommitCurrent();

        _current = OutputList.SelectedItem as OutputState;
        if (_current is null) return;

        _editVm.LoadFrom(_current.Config, _editVm.AvailableMonitors.Count);
    }

    void CommitCurrent()
    {
        if (_current is not null)
            _editVm.WriteTo(_current.Config);
    }

    // ── Add / Remove ──────────────────────────────────────────────────────────

    void OnAddOutput(object? sender, RoutedEventArgs e)
    {
        _vm.AddOutput();
        OutputList.SelectedIndex = _vm.OutputStates.Count - 1;
    }

    void OnRemoveOutput(object? sender, RoutedEventArgs e)
    {
        var state = OutputList.SelectedItem as OutputState;
        if (state is null || _vm.OutputStates.Count <= 1) return;

        // Close the live output window if it's open.
        if (Owner is MainWindow mainWin)
            mainWin.CloseOutputWindow(state);

        var idx = _vm.OutputStates.IndexOf(state);
        _current = null;
        _vm.RemoveOutput(state);

        int newIdx = Math.Clamp(idx, 0, _vm.OutputStates.Count - 1);
        if (_vm.OutputStates.Count > 0)
            OutputList.SelectedIndex = newIdx;
    }

    // ── Close ─────────────────────────────────────────────────────────────────

    void OnClose(object? sender, RoutedEventArgs e)
    {
        CommitCurrent();
        Close();
    }

    protected override void OnClosing(WindowClosingEventArgs e)
    {
        _editVm.PropertyChanged -= OnEditVmPropertyChanged;
        CommitCurrent();
        _vm.NotifyOutputConfigsChanged();
        base.OnClosing(e);
    }
}
