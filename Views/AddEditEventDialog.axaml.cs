using Avalonia.Controls;
using Avalonia.Interactivity;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class AddEditEventDialog : Window
{
    AddEditEventViewModel VM => (AddEditEventViewModel)DataContext!;

    public AddEditEventDialog(ShowFile showFile, System.DateTime? defaultDate = null, ScheduledEvent? existing = null)
    {
        InitializeComponent();
        DataContext = new AddEditEventViewModel(showFile, defaultDate, existing);
    }

    void OnSpin(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn) return;
        switch (btn.Tag as string)
        {
            case "hour-up":      VM.Hour++;            break;
            case "hour-down":    VM.Hour--;            break;
            case "min-up":       VM.Minute++;          break;
            case "min-down":     VM.Minute--;          break;
            case "interval-up":  VM.RepeatInterval++;  break;
            case "interval-down": VM.RepeatInterval--; break;
        }
    }

    void OnAddTimerAction(object? sender, RoutedEventArgs e) => VM.AddTimerAction();

    void OnRemoveTimerAction(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is ShowCast.ViewModels.TimerActionRow row)
            VM.RemoveTimerAction(row);
    }

    void OnSave(object? sender, RoutedEventArgs e) => Close(VM.BuildEvent());
    void OnCancel(object? sender, RoutedEventArgs e) => Close(null);
}
