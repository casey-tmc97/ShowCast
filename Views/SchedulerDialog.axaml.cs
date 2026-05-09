using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class SchedulerDialog : Window
{
    SchedulerViewModel VM => (SchedulerViewModel)DataContext!;

    public SchedulerDialog(ShowFile showFile)
    {
        InitializeComponent();
        DataContext = new SchedulerViewModel(showFile);
    }

    void OnPrevMonth(object? sender, RoutedEventArgs e) => VM.PreviousMonth();
    void OnNextMonth(object? sender, RoutedEventArgs e) => VM.NextMonth();

    void OnDayTapped(object? sender, TappedEventArgs e)
    {
        var v = e.Source as Avalonia.Visual;
        while (v is not null)
        {
            if (v is Control c && c.DataContext is CalendarDayViewModel day)
            {
                VM.SelectDay(day);
                return;
            }
            v = v.GetVisualParent();
        }
    }

    async void OnAddEvent(object? sender, RoutedEventArgs e)
    {
        var date   = VM.SelectedDate;
        var dialog = new AddEditEventDialog(VM.ShowFile, date);
        var result = await dialog.ShowDialog<ScheduledEvent?>(this);
        if (result is not null)
        {
            VM.ShowFile.ScheduledEvents.Add(result);
            VM.RefreshCalendar();
        }
    }

    async void OnEditEvent(object? sender, RoutedEventArgs e)
    {
        if (sender is not Button btn || btn.DataContext is not ScheduledEventDisplay display) return;
        var dialog = new AddEditEventDialog(VM.ShowFile, null, display.Event);
        var result = await dialog.ShowDialog<ScheduledEvent?>(this);
        if (result is not null)
            VM.RefreshCalendar(); // event was mutated in-place by BuildEvent()
    }

    void OnDeleteEvent(object? sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.DataContext is ScheduledEventDisplay display)
            VM.RemoveEvent(display.Event);
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
}
