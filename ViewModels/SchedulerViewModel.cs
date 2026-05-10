using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

// ── Calendar cell ─────────────────────────────────────────────────────────────

public class CalendarDayViewModel : ViewModelBase
{
    public DateTime? Date           { get; init; }
    public bool      IsCurrentMonth { get; init; }
    public string    DayText        => Date?.Day.ToString() ?? "";
    public bool      IsToday        => Date?.Date == DateTime.Today;

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    bool _hasEvents;
    public bool HasEvents
    {
        get => _hasEvents;
        set => this.RaiseAndSetIfChanged(ref _hasEvents, value);
    }
}

// ── Scheduled event display wrapper ──────────────────────────────────────────

public class ScheduledEventDisplay : ViewModelBase
{
    public ScheduledEvent Event       { get; }
    public string         TimeLabel   => Event.ScheduledAt.ToString("HH:mm");
    public string         RundownName { get; }
    public string         PackageName { get; }
    public string         Label       => Event.Label;

    public string Subtitle
    {
        get
        {
            var parts = new List<string> { RundownName, PackageName };
            var repeat = RepeatLabel;
            if (repeat.Length > 0) parts.Add(repeat);
            return string.Join("  ·  ", parts);
        }
    }

    string RepeatLabel => Event.Repeat switch
    {
        RepeatType.Daily   => Event.RepeatInterval == 1 ? "Daily"   : $"Every {Event.RepeatInterval} days",
        RepeatType.Weekly  => Event.RepeatInterval == 1 ? "Weekly"  : $"Every {Event.RepeatInterval} weeks",
        RepeatType.Monthly => Event.RepeatInterval == 1 ? "Monthly" : $"Every {Event.RepeatInterval} months",
        _                  => ""
    };

    public ScheduledEventDisplay(ScheduledEvent evt, string rundownName, string packageName)
    {
        Event       = evt;
        RundownName = rundownName;
        PackageName = packageName;
    }

    public bool IsEnabled
    {
        get => Event.IsEnabled;
        set { Event.IsEnabled = value; this.RaisePropertyChanged(); }
    }
}

// ── Main scheduler view-model ─────────────────────────────────────────────────

public class SchedulerViewModel : ViewModelBase
{
    readonly ShowFile _showFile;

    public SchedulerViewModel(ShowFile showFile)
    {
        _showFile = showFile;
        ViewMonth = new DateTime(DateTime.Today.Year, DateTime.Today.Month, 1);

        // Auto-select today
        var today = CalendarDays.FirstOrDefault(c => c.IsToday && c.IsCurrentMonth);
        if (today is not null) SelectDay(today);
    }

    // ── Month navigation ──────────────────────────────────────────────────────

    DateTime _viewMonth;
    public DateTime ViewMonth
    {
        get => _viewMonth;
        private set
        {
            this.RaiseAndSetIfChanged(ref _viewMonth, value);
            this.RaisePropertyChanged(nameof(MonthLabel));
            RefreshCalendar();
        }
    }

    public string MonthLabel => _viewMonth.ToString("MMMM yyyy");

    public void PreviousMonth() => ViewMonth = ViewMonth.AddMonths(-1);
    public void NextMonth()     => ViewMonth = ViewMonth.AddMonths(1);

    // ── Calendar grid (42 cells = 6 weeks × 7 days) ───────────────────────────

    public ObservableCollection<CalendarDayViewModel> CalendarDays { get; } = new();

    public void RefreshCalendar()
    {
        var prevSelected = _selectedDay?.Date;
        CalendarDays.Clear();

        var first       = new DateTime(_viewMonth.Year, _viewMonth.Month, 1);
        int startOffset = (int)first.DayOfWeek;
        int daysInMonth = DateTime.DaysInMonth(_viewMonth.Year, _viewMonth.Month);

        for (int i = 0; i < startOffset; i++)
        {
            var d = first.AddDays(i - startOffset);
            CalendarDays.Add(new CalendarDayViewModel { Date = d, IsCurrentMonth = false,
                HasEvents = _showFile.ScheduledEvents.Any(e => HasEventOn(e, d)) });
        }

        for (int day = 1; day <= daysInMonth; day++)
        {
            var d = new DateTime(_viewMonth.Year, _viewMonth.Month, day);
            CalendarDays.Add(new CalendarDayViewModel { Date = d, IsCurrentMonth = true,
                HasEvents = _showFile.ScheduledEvents.Any(e => HasEventOn(e, d)) });
        }

        while (CalendarDays.Count < 42)
        {
            var d = first.AddMonths(1).AddDays(CalendarDays.Count - startOffset - daysInMonth);
            CalendarDays.Add(new CalendarDayViewModel { Date = d, IsCurrentMonth = false,
                HasEvents = _showFile.ScheduledEvents.Any(e => HasEventOn(e, d)) });
        }

        if (prevSelected.HasValue)
        {
            var match = CalendarDays.FirstOrDefault(c => c.Date == prevSelected);
            if (match is not null) { match.IsSelected = true; _selectedDay = match; }
        }

        RaiseSelectedDayChanged();
    }

    // ── Recurring event helpers ───────────────────────────────────────────────

    public static bool HasEventOn(ScheduledEvent evt, DateTime date)
    {
        if (!evt.IsEnabled) return false;
        var startDate = evt.ScheduledAt.Date;
        if (date.Date < startDate) return false;
        if (evt.RepeatUntil.HasValue && date.Date > evt.RepeatUntil.Value.Date) return false;

        return evt.Repeat switch
        {
            RepeatType.None => date.Date == startDate,
            RepeatType.Daily => (int)(date.Date - startDate).TotalDays % Math.Max(1, evt.RepeatInterval) == 0,
            RepeatType.Weekly =>
                ((evt.RepeatWeekDays == 0
                    ? date.DayOfWeek == startDate.DayOfWeek
                    : (evt.RepeatWeekDays & (1 << (int)date.DayOfWeek)) != 0)
                && (int)(date.Date - startDate).TotalDays / 7 % Math.Max(1, evt.RepeatInterval) == 0),
            RepeatType.Monthly =>
                date.Day == startDate.Day &&
                (date.Year * 12 + date.Month - (startDate.Year * 12 + startDate.Month)) % Math.Max(1, evt.RepeatInterval) == 0,
            _ => false
        };
    }

    // ── Day selection ─────────────────────────────────────────────────────────

    CalendarDayViewModel? _selectedDay;
    public CalendarDayViewModel? SelectedDay => _selectedDay;

    public void SelectDay(CalendarDayViewModel? cell)
    {
        if (cell is null || !cell.Date.HasValue) return;

        if (!cell.IsCurrentMonth)
        {
            ViewMonth = new DateTime(cell.Date.Value.Year, cell.Date.Value.Month, 1);
            SelectDay(CalendarDays.FirstOrDefault(c => c.Date == cell.Date));
            return;
        }

        if (_selectedDay is not null) _selectedDay.IsSelected = false;
        _selectedDay = cell;
        if (_selectedDay is not null) _selectedDay.IsSelected = true;
        RaiseSelectedDayChanged();
    }

    void RaiseSelectedDayChanged()
    {
        this.RaisePropertyChanged(nameof(SelectedDay));
        this.RaisePropertyChanged(nameof(SelectedDayLabel));
        RefreshDayEvents();
    }

    public string SelectedDayLabel =>
        _selectedDay?.Date.HasValue == true
            ? _selectedDay.Date!.Value.ToString("dddd, MMMM d")
            : "Select a day";

    // ── Events for selected day ───────────────────────────────────────────────

    public ObservableCollection<ScheduledEventDisplay> SelectedDayEvents { get; } = new();

    void RefreshDayEvents()
    {
        SelectedDayEvents.Clear();
        if (_selectedDay?.Date.HasValue != true) return;
        var date = _selectedDay.Date!.Value.Date;
        foreach (var evt in _showFile.ScheduledEvents
            .Where(e => HasEventOn(e, date))
            .OrderBy(e => e.ScheduledAt.TimeOfDay))
        {
            var rd  = _showFile.Rundowns.FirstOrDefault(r => r.Id == evt.RundownId)?.Name ?? "(deleted)";
            var pkg = _showFile.FindPackage(evt.PackageId)?.Name ?? "(deleted)";
            SelectedDayEvents.Add(new ScheduledEventDisplay(evt, rd, pkg));
        }
    }

    public void RemoveEvent(ScheduledEvent evt)
    {
        _showFile.ScheduledEvents.Remove(evt);
        RefreshCalendar();
    }

    public ShowFile ShowFile => _showFile;
    public DateTime? SelectedDate => _selectedDay?.Date;
}
