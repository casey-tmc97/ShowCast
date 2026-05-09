using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class TimerActionRow : ViewModelBase
{
    public IReadOnlyList<TimerDef>        AvailableTimers { get; }
    public IReadOnlyList<TimerActionType> AvailableActions { get; } =
        new[] { TimerActionType.Play, TimerActionType.Pause, TimerActionType.Reset, TimerActionType.PlayPause };

    TimerDef?       _selectedTimer;
    TimerActionType _action = TimerActionType.Play;

    public TimerActionRow(IReadOnlyList<TimerDef> availableTimers, Guid? timerId = null, TimerActionType action = TimerActionType.Play)
    {
        AvailableTimers = availableTimers;
        _selectedTimer  = timerId.HasValue ? availableTimers.FirstOrDefault(t => t.Id == timerId.Value) : availableTimers.FirstOrDefault();
        _action         = action;
    }

    public TimerDef? SelectedTimer
    {
        get => _selectedTimer;
        set => this.RaiseAndSetIfChanged(ref _selectedTimer, value);
    }

    public TimerActionType Action
    {
        get => _action;
        set => this.RaiseAndSetIfChanged(ref _action, value);
    }
}

public class AddEditEventViewModel : ViewModelBase
{
    readonly ShowFile            _showFile;
    readonly IReadOnlyList<TimerDef> _availableTimers;

    public AddEditEventViewModel(ShowFile showFile, DateTime? defaultDate = null, ScheduledEvent? existing = null)
    {
        _showFile        = showFile;
        _availableTimers = showFile.Timers;
        SourceEvent      = existing;
        IsEditing        = existing is not null;
        Title            = IsEditing ? "Edit Event" : "Add Event";
        Rundowns         = showFile.Rundowns;

        if (existing is not null)
        {
            _dateText        = existing.ScheduledAt.ToString("MM/dd/yyyy");
            _hour            = existing.ScheduledAt.Hour;
            _minute          = existing.ScheduledAt.Minute;
            _repeat          = existing.Repeat;
            _repeatInterval  = Math.Max(1, existing.RepeatInterval);
            _repeatWeekDays  = existing.RepeatWeekDays;
            _hasRepeatUntil  = existing.RepeatUntil.HasValue;
            _repeatUntilText = existing.RepeatUntil?.ToString("MM/dd/yyyy") ?? "";
            _label           = existing.Label;
            _selectedRundown = showFile.Rundowns.FirstOrDefault(r => r.Id == existing.RundownId);
        }
        else
        {
            _dateText = (defaultDate ?? DateTime.Today).ToString("MM/dd/yyyy");
            _hour     = 9;
            _minute   = 0;
            if (Rundowns.Count > 0) _selectedRundown = Rundowns[0];
        }

        RebuildPackages();

        if (existing is not null)
        {
            _selectedPackage = RundownPackages.FirstOrDefault(p => p.Id == existing.PackageId)
                               ?? RundownPackages.FirstOrDefault();
            RebuildPages();
            if (existing.PageId.HasValue)
                _selectedPage = PackagePages.FirstOrDefault(p => p.Id == existing.PageId.Value)
                                ?? PackagePages.FirstOrDefault();

            foreach (var ta in existing.TimerActions)
                TimerActionRows.Add(new TimerActionRow(_availableTimers, ta.TimerId, ta.Action));
        }
    }

    public bool           IsEditing   { get; }
    public string         Title       { get; }
    public ScheduledEvent? SourceEvent { get; }
    public List<Rundown>  Rundowns    { get; }

    public ObservableCollection<Package> RundownPackages { get; } = new();
    public ObservableCollection<Page>    PackagePages    { get; } = new();

    // ── Date ──────────────────────────────────────────────────────────────────

    string _dateText = "";
    public string DateText
    {
        get => _dateText;
        set { this.RaiseAndSetIfChanged(ref _dateText, value); this.RaisePropertyChanged(nameof(CanSave)); }
    }

    // ── Time ──────────────────────────────────────────────────────────────────

    int _hour;
    public int Hour
    {
        get => _hour;
        set { value = Math.Clamp(value, 0, 23); this.RaiseAndSetIfChanged(ref _hour, value); this.RaisePropertyChanged(nameof(HourText)); }
    }
    public string HourText
    {
        get => _hour.ToString("D2");
        set { if (int.TryParse(value, out int v)) Hour = v; }
    }

    int _minute;
    public int Minute
    {
        get => _minute;
        set { value = Math.Clamp(value, 0, 59); this.RaiseAndSetIfChanged(ref _minute, value); this.RaisePropertyChanged(nameof(MinuteText)); }
    }
    public string MinuteText
    {
        get => _minute.ToString("D2");
        set { if (int.TryParse(value, out int v)) Minute = v; }
    }

    // ── Repeat ────────────────────────────────────────────────────────────────

    public IReadOnlyList<RepeatType> RepeatTypes { get; } =
        new[] { RepeatType.None, RepeatType.Daily, RepeatType.Weekly, RepeatType.Monthly };

    RepeatType _repeat;
    public RepeatType Repeat
    {
        get => _repeat;
        set
        {
            this.RaiseAndSetIfChanged(ref _repeat, value);
            this.RaisePropertyChanged(nameof(ShowRepeatOptions));
            this.RaisePropertyChanged(nameof(ShowWeeklyOptions));
            this.RaisePropertyChanged(nameof(RepeatUnitLabel));
        }
    }

    public bool ShowRepeatOptions => _repeat != RepeatType.None;
    public bool ShowWeeklyOptions => _repeat == RepeatType.Weekly;

    public string RepeatUnitLabel => _repeat switch
    {
        RepeatType.Daily   => "day(s)",
        RepeatType.Weekly  => "week(s)",
        RepeatType.Monthly => "month(s)",
        _                  => ""
    };

    int _repeatInterval = 1;
    public int RepeatInterval
    {
        get => _repeatInterval;
        set { value = Math.Max(1, value); this.RaiseAndSetIfChanged(ref _repeatInterval, value); this.RaisePropertyChanged(nameof(RepeatIntervalText)); }
    }
    public string RepeatIntervalText
    {
        get => _repeatInterval.ToString();
        set { if (int.TryParse(value, out int v)) RepeatInterval = v; }
    }

    // ── Day-of-week toggles ───────────────────────────────────────────────────

    int _repeatWeekDays;
    public int RepeatWeekDays
    {
        get => _repeatWeekDays;
        set => this.RaiseAndSetIfChanged(ref _repeatWeekDays, value);
    }

    void SetWeekDay(int bit, bool val)
    {
        RepeatWeekDays = val ? _repeatWeekDays | (1 << bit) : _repeatWeekDays & ~(1 << bit);
    }

    public bool RepeatSunday    { get => (_repeatWeekDays & (1 << 0)) != 0; set { SetWeekDay(0, value); this.RaisePropertyChanged(); } }
    public bool RepeatMonday    { get => (_repeatWeekDays & (1 << 1)) != 0; set { SetWeekDay(1, value); this.RaisePropertyChanged(); } }
    public bool RepeatTuesday   { get => (_repeatWeekDays & (1 << 2)) != 0; set { SetWeekDay(2, value); this.RaisePropertyChanged(); } }
    public bool RepeatWednesday { get => (_repeatWeekDays & (1 << 3)) != 0; set { SetWeekDay(3, value); this.RaisePropertyChanged(); } }
    public bool RepeatThursday  { get => (_repeatWeekDays & (1 << 4)) != 0; set { SetWeekDay(4, value); this.RaisePropertyChanged(); } }
    public bool RepeatFriday    { get => (_repeatWeekDays & (1 << 5)) != 0; set { SetWeekDay(5, value); this.RaisePropertyChanged(); } }
    public bool RepeatSaturday  { get => (_repeatWeekDays & (1 << 6)) != 0; set { SetWeekDay(6, value); this.RaisePropertyChanged(); } }

    // ── Until date ────────────────────────────────────────────────────────────

    bool _hasRepeatUntil;
    public bool HasRepeatUntil
    {
        get => _hasRepeatUntil;
        set => this.RaiseAndSetIfChanged(ref _hasRepeatUntil, value);
    }

    string _repeatUntilText = "";
    public string RepeatUntilText
    {
        get => _repeatUntilText;
        set => this.RaiseAndSetIfChanged(ref _repeatUntilText, value);
    }

    // ── Rundown / Package ─────────────────────────────────────────────────────

    Rundown? _selectedRundown;
    public Rundown? SelectedRundown
    {
        get => _selectedRundown;
        set { this.RaiseAndSetIfChanged(ref _selectedRundown, value); RebuildPackages(); this.RaisePropertyChanged(nameof(CanSave)); }
    }

    void RebuildPackages()
    {
        RundownPackages.Clear();
        _selectedPackage = null;
        if (_selectedRundown is null) return;
        foreach (var entry in _selectedRundown.Entries)
        {
            var pkg = FindPackage(entry.PackageId);
            if (pkg is not null) RundownPackages.Add(pkg);
        }
        SelectedPackage = RundownPackages.FirstOrDefault();
    }

    Package? FindPackage(Guid id)
    {
        foreach (var show in _showFile.Shows)
            foreach (var pkg in show.Packages)
                if (pkg.Id == id) return pkg;
        return null;
    }

    Package? _selectedPackage;
    public Package? SelectedPackage
    {
        get => _selectedPackage;
        set { this.RaiseAndSetIfChanged(ref _selectedPackage, value); RebuildPages(); this.RaisePropertyChanged(nameof(CanSave)); }
    }

    Page? _selectedPage;
    public Page? SelectedPage
    {
        get => _selectedPage;
        set => this.RaiseAndSetIfChanged(ref _selectedPage, value);
    }

    void RebuildPages()
    {
        PackagePages.Clear();
        _selectedPage = null;
        if (_selectedPackage is null) return;
        foreach (var page in _selectedPackage.Pages)
            PackagePages.Add(page);
        SelectedPage = PackagePages.FirstOrDefault();
    }

    // ── Timer Actions ─────────────────────────────────────────────────────────

    public ObservableCollection<TimerActionRow> TimerActionRows { get; } = new();
    public bool HasAvailableTimers => _availableTimers.Count > 0;

    public void AddTimerAction()
    {
        TimerActionRows.Add(new TimerActionRow(_availableTimers));
    }

    public void RemoveTimerAction(TimerActionRow row)
    {
        TimerActionRows.Remove(row);
    }

    string _label = "";
    public string Label
    {
        get => _label;
        set => this.RaiseAndSetIfChanged(ref _label, value);
    }

    // ── Validation ────────────────────────────────────────────────────────────

    public bool CanSave =>
        _selectedRundown is not null &&
        _selectedPackage is not null &&
        DateTime.TryParse(_dateText, out _);

    // ── Build result ──────────────────────────────────────────────────────────

    public ScheduledEvent BuildEvent()
    {
        DateTime.TryParse(_dateText, out var date);
        var at    = date.Date.AddHours(_hour).AddMinutes(_minute);
        var label = _label.Trim();

        DateTime? until = null;
        if (_hasRepeatUntil && DateTime.TryParse(_repeatUntilText, out var u)) until = u.Date;

        var evt = SourceEvent ?? new ScheduledEvent();
        evt.RundownId      = _selectedRundown!.Id;
        evt.PackageId      = _selectedPackage!.Id;
        evt.PageId         = _selectedPage?.Id;
        evt.ScheduledAt    = at;
        evt.Label          = label.Length > 0 ? label : _selectedPackage!.Name;
        evt.IsEnabled      = true;
        evt.HasRun         = false;
        evt.Repeat         = _repeat;
        evt.RepeatInterval = _repeatInterval;
        evt.RepeatWeekDays = _repeat == RepeatType.Weekly ? _repeatWeekDays : 0;
        evt.RepeatUntil    = until;

        evt.TimerActions.Clear();
        foreach (var row in TimerActionRows)
            if (row.SelectedTimer is not null)
                evt.TimerActions.Add(new TimerAction { TimerId = row.SelectedTimer.Id, Action = row.Action });

        return evt;
    }
}
