using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class TimerPanel : UserControl
{
    MainViewModel? VM => DataContext as MainViewModel;

    public TimerPanel() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VM is null) return;
        TimerList.ItemsSource = VM.Timers;
        VM.Timers.CollectionChanged += (_, _) => UpdateEmptyHint();
        UpdateEmptyHint();
    }

    void UpdateEmptyHint() =>
        EmptyHint.IsVisible = VM is null || VM.Timers.Count == 0;

    async void OnAddTimer(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TimerEditDialog();
        await dlg.ShowDialog(owner);
        if (dlg.Result is not null)
        {
            VM?.AddTimer(dlg.Result);
            UpdateEmptyHint();
        }
    }

    void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is TimerViewModel tvm)
            tvm.PlayPause();
    }

    void OnReset(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is TimerViewModel tvm)
            tvm.Reset();
    }

    void OnDeleteTimer(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is TimerViewModel tvm)
        {
            VM?.RemoveTimer(tvm);
            UpdateEmptyHint();
        }
    }

    async void OnEditTimer(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not TimerViewModel tvm) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var dlg = new TimerEditDialog(tvm.Def);
        await dlg.ShowDialog(owner);
        if (dlg.Result is not null)
        {
            bool wasRunning = tvm.IsRunning;
            tvm.Pause();
            tvm.Reset();
            tvm.RefreshAfterEdit();
            if (wasRunning && tvm.CanPlayPause) tvm.Play();
        }
    }
}
