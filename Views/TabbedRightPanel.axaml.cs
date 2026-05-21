using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Media;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class TabbedRightPanel : UserControl
{
    MainViewModel? VM => DataContext as MainViewModel;

    public TabbedRightPanel() => InitializeComponent();

    // ── Tab switching ─────────────────────────────────────────────────────────

    void OnTimersTab(object? sender, RoutedEventArgs e) => ActivateTab(timers: true);
    void OnAudioTab(object? sender, RoutedEventArgs e)  => ActivateTab(timers: false);

    void ActivateTab(bool timers)
    {
        TimersContent.IsVisible = timers;
        AudioContent.IsVisible  = !timers;
        AddTimerBtn.IsVisible   = timers;

        // Active tab: dark bg, white text, accent underline
        // Inactive tab: mid bg, grey text, no underline
        TimersTabBtn.Background  = timers
            ? new SolidColorBrush(Color.Parse("#1e1e1e"))
            : new SolidColorBrush(Color.Parse("#2d2d2d"));
        TimersTabBtn.Foreground  = timers
            ? Brushes.White
            : new SolidColorBrush(Color.Parse("#888888"));
        TimersTabBtn.BorderBrush = timers
            ? new SolidColorBrush(Color.Parse("#e07050"))
            : Brushes.Transparent;

        AudioTabBtn.Background   = !timers
            ? new SolidColorBrush(Color.Parse("#1e1e1e"))
            : new SolidColorBrush(Color.Parse("#2d2d2d"));
        AudioTabBtn.Foreground   = !timers
            ? Brushes.White
            : new SolidColorBrush(Color.Parse("#888888"));
        AudioTabBtn.BorderBrush  = !timers
            ? new SolidColorBrush(Color.Parse("#e07050"))
            : Brushes.Transparent;
    }

    // ── Add timer (delegated to MainViewModel) ────────────────────────────────

    async void OnAddTimer(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TimerEditDialog();
        await dlg.ShowDialog(owner);
        if (dlg.Result is not null)
        {
            VM?.AddTimer(dlg.Result);
            TimersContent.RefreshEmptyHint();
        }
    }
}
