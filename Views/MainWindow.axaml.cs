using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class MainWindow : Window
{
    public MainWindow() => InitializeComponent();

    MainViewModel? VM => DataContext as MainViewModel;

    // ── Lifecycle: auto-load / auto-save ──────────────────────────────────────

    protected override async void OnOpened(EventArgs e)
    {
        base.OnOpened(e);

        if (VM is null) return;
        if (File.Exists(AppFolders.SessionFile))
        {
            await VM.LoadSessionAsync(
                AppFolders.SessionFile,
                confirmMigration: () => AlertDialog.ShowConfirm(
                    this,
                    "Upgrade File Format",
                    "This file was saved with an older version of ShowCast. Upgrade it to the current format?"),
                showError: msg => AlertDialog.ShowError(
                    this,
                    "Cannot Open File",
                    msg));
            RestoreWindowState(VM.ShowFile.Settings);
        }
        VM.OutputStates.CollectionChanged += (_, _) => UpdateRightGridLayout();
        RightGrid.SizeChanged += (_, _) => UpdateRightGridLayout();
        UpdateRightGridLayout();

        VM.WhenAnyValue(x => x.IsEditorOpen)
          .Subscribe(open => WorkspaceGrid.IsVisible = !open);
    }

    // Snap the multiview row to the exact height of N output cards so the timer panel
    // sits flush below the last card. Cell height is derived from the actual panel width
    // (each card is a 16:9 image with a 2px border on every side).
    void UpdateRightGridLayout()
    {
        double panelWidth = RightGrid.Bounds.Width;
        if (panelWidth <= 0) return; // not laid out yet; SizeChanged will fire once it is

        int count = VM?.OutputStates.Count ?? 0;
        if (count < 1) count = 1;

        double imageWidth = panelWidth - 4; // subtract 2px border left + right
        double cellHeight = imageWidth * (1080.0 / 1920.0) + 4; // +4 for top/bottom border

        RightGrid.RowDefinitions[0].Height = new GridLength(count * cellHeight, GridUnitType.Pixel);
        RightGrid.RowDefinitions[2].Height = new GridLength(1, GridUnitType.Star);
    }

    void RestoreWindowState(ShowCast.Core.AppSettings s)
    {
        if (s.WindowMaximized)
        {
            WindowState = WindowState.Maximized;
        }
        else
        {
            Width  = s.WindowWidth;
            Height = s.WindowHeight;
            if (s.WindowX.HasValue && s.WindowY.HasValue)
                Position = new Avalonia.PixelPoint(s.WindowX.Value, s.WindowY.Value);
        }

        WorkspaceGrid.ColumnDefinitions[0].Width = new Avalonia.Controls.GridLength(s.LeftPanelWidth,  Avalonia.Controls.GridUnitType.Pixel);
        WorkspaceGrid.ColumnDefinitions[4].Width = new Avalonia.Controls.GridLength(s.RightPanelWidth, Avalonia.Controls.GridUnitType.Pixel);
        LeftGrid.RowDefinitions[0].Height = new Avalonia.Controls.GridLength(s.LeftTopStarWeight,  Avalonia.Controls.GridUnitType.Star);
        LeftGrid.RowDefinitions[2].Height = new Avalonia.Controls.GridLength(s.LeftMidStarWeight,  Avalonia.Controls.GridUnitType.Star);
        LeftGrid.RowDefinitions[4].Height = new Avalonia.Controls.GridLength(s.LeftBotStarWeight,  Avalonia.Controls.GridUnitType.Star);
        RightGrid.RowDefinitions[0].Height = new Avalonia.Controls.GridLength(s.RightTopStarWeight, Avalonia.Controls.GridUnitType.Star);
        RightGrid.RowDefinitions[2].Height = new Avalonia.Controls.GridLength(s.RightBotStarWeight, Avalonia.Controls.GridUnitType.Star);
    }

    void SavePanelSizes(ShowCast.Core.AppSettings s)
    {
        s.LeftPanelWidth     = WorkspaceGrid.ColumnDefinitions[0].ActualWidth;
        s.RightPanelWidth    = WorkspaceGrid.ColumnDefinitions[4].ActualWidth;
        s.LeftTopStarWeight  = LeftGrid.RowDefinitions[0].Height.Value;
        s.LeftMidStarWeight  = LeftGrid.RowDefinitions[2].Height.Value;
        s.LeftBotStarWeight  = LeftGrid.RowDefinitions[4].Height.Value;
        s.RightTopStarWeight = RightGrid.RowDefinitions[0].Height.Value;
        s.RightBotStarWeight = RightGrid.RowDefinitions[2].Height.Value;
    }

    bool _saving;

    protected override async void OnClosing(WindowClosingEventArgs e)
    {
        if (!_saving)
        {
            // Cancel the first close request, save, then re-close.
            e.Cancel = true;
            _saving  = true;
            if (VM is not null)
            {
                var s = VM.ShowFile.Settings;
                s.WindowMaximized = WindowState == WindowState.Maximized;
                if (!s.WindowMaximized)
                {
                    s.WindowWidth  = Width;
                    s.WindowHeight = Height;
                    s.WindowX      = Position.X;
                    s.WindowY      = Position.Y;
                }
                SavePanelSizes(s);
                await VM.SaveSessionAsync(AppFolders.SessionFile);
            }
            foreach (var win in _outputWindows.Values.ToList())
                win.Close();
            Close();
        }
        base.OnClosing(e);
    }

    // ── Output windows ────────────────────────────────────────────────────────

    readonly Dictionary<OutputState, OutputWindow> _outputWindows = new();

    public void CloseOutputWindow(OutputState output)
    {
        if (_outputWindows.TryGetValue(output, out var win))
            win.Close();
    }

    public void ToggleOutputWindowFor(OutputState? output)
    {
        if (output is null) return;
        if (_outputWindows.TryGetValue(output, out var existing))
        {
            existing.Close();
        }
        else
        {
            var win = new OutputWindow(output);
            _outputWindows[output] = win;
            output.IsOutputWindowOpen = true;
            win.Closed += (_, _) => { _outputWindows.Remove(output); output.IsOutputWindowOpen = false; };
            win.Opened += (_, _) =>
            {
                if (win.Screens is not null) win.PositionOnScreen(win.Screens);
                win.Activate();
            };
            win.Show();
        }
    }

    async void OnScreenConfig(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new ScreenConfigDialog(VM);
        await dialog.ShowDialog(this);
    }

    void OnToggleLiveOutput(object? sender, RoutedEventArgs e)
    {
        var output = VM?.SelectedOutput;
        if (output is null) return;

        if (_outputWindows.TryGetValue(output, out var existing))
        {
            existing.Close();
        }
        else
        {
            var win = new OutputWindow(output);
            _outputWindows[output] = win;
            output.IsOutputWindowOpen = true;
            win.Closed += (_, _) => { _outputWindows.Remove(output); output.IsOutputWindowOpen = false; };
            win.Opened += (_, _) =>
            {
                if (win.Screens is not null)
                    win.PositionOnScreen(win.Screens);
                win.Activate();
            };
            win.Show();
        }
    }

    // ── Live control ──────────────────────────────────────────────────────────

    void OnClear(object? sender, RoutedEventArgs e) => VM?.ClearLive();

    // ── Settings menu ─────────────────────────────────────────────────────────

    async void OnScheduler(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new SchedulerDialog(VM.ShowFile);
        await dialog.ShowDialog(this);
        // Re-arm the scheduler in case new events were added
        VM.StartSchedulerTimer();
    }

    // ── Edit menu ─────────────────────────────────────────────────────────────

    void OnUndo(object? sender, RoutedEventArgs e) => VM?.Undo();
    void OnRedo(object? sender, RoutedEventArgs e) => VM?.Redo();

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled) return;

        bool ctrl       = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool textFocused = FocusManager.GetFocusedElement() is TextBox or NumericUpDown;

        if (ctrl && e.Key == Key.Z) { VM?.Undo(); e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y) { VM?.Redo(); e.Handled = true; return; }
        if (e.Key == Key.F1)        { OpenManual(); e.Handled = true; return; }

        if (ctrl && !textFocused && VM?.IsEditorOpen == false)
        {
            if (e.Key == Key.C) { VM.CopyPage(VM.SelectedPage); e.Handled = true; return; }
            if (e.Key == Key.X) { VM.CutPage(VM.SelectedPage);  e.Handled = true; return; }
            if (e.Key == Key.V) { VM.PastePage();               e.Handled = true; return; }
        }

        // Page grid hotkeys — blocked when editor is open or a text input has focus
        if (!textFocused && VM?.IsEditorOpen == false)
        {
            switch (e.Key)
            {
                case Key.Space:
                    VM.GoLiveAndAdvance();
                    e.Handled = true;
                    break;
                case Key.Return:
                    VM.GoLive();
                    e.Handled = true;
                    break;
                case Key.Left:
                case Key.Up:
                    VM.SelectPreviousPage();
                    e.Handled = true;
                    break;
                case Key.Right:
                case Key.Down:
                    VM.SelectNextPage();
                    e.Handled = true;
                    break;
            }
        }
    }

    // ── Help menu ─────────────────────────────────────────────────────────────

    void OnManual(object? sender, RoutedEventArgs e) => OpenManual();

    static void OpenManual()
    {
        var path = System.IO.Path.Combine(AppContext.BaseDirectory, "Docs", "manual.html");
        if (!System.IO.File.Exists(path)) return;
        System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(path)
        {
            UseShellExecute = true
        });
    }

    // ── File menu ─────────────────────────────────────────────────────────────

    void OnNew(object? sender, RoutedEventArgs e) => VM?.NewFile();

    // ── Window chrome ─────────────────────────────────────────────────────────

    void OnTitleBarPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
            BeginMoveDrag(e);
    }

    void OnTitleBarDoubleTapped(object? sender, TappedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    void OnMinimize(object? sender, RoutedEventArgs e) =>
        WindowState = WindowState.Minimized;

    void OnMaximize(object? sender, RoutedEventArgs e)
    {
        WindowState = WindowState == WindowState.Maximized
            ? WindowState.Normal
            : WindowState.Maximized;
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();

    void OnQuit(object? sender, RoutedEventArgs e) => Close();
}
