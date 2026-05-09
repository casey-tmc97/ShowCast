using System;
using Avalonia.Media;
using Avalonia.Threading;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class TimerViewModel : ReactiveObject, IDisposable
{
    public TimerDef Def { get; }

    DispatcherTimer? _ticker;
    int      _currentSeconds;
    bool     _running;
    DateTime _startedAt;
    int      _secondsAtStart;

    public TimerViewModel(TimerDef def)
    {
        Def = def;
        _currentSeconds = def.Type == TimerType.Counter ? def.StartSeconds : 0;
        TimerTextCache.Update(def.Id, DisplayText);

        // Clock timers always display a live countdown — start the refresh loop immediately
        if (def.Type == TimerType.Clock)
        {
            _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTick);
            _ticker.Start();
            _running = true;
        }
    }

    public string Name => Def.Name;

    // ── Reactive state ────────────────────────────────────────────────────────

    public int CurrentSeconds
    {
        get => _currentSeconds;
        private set
        {
            this.RaiseAndSetIfChanged(ref _currentSeconds, value);
            this.RaisePropertyChanged(nameof(DisplayText));
            this.RaisePropertyChanged(nameof(DisplayBrush));
            TimerTextCache.Update(Def.Id, DisplayText);
        }
    }

    public bool IsRunning
    {
        get => _running;
        private set
        {
            this.RaiseAndSetIfChanged(ref _running, value);
            this.RaisePropertyChanged(nameof(PlayPauseIcon));
        }
    }

    // ── Computed display ──────────────────────────────────────────────────────

    public bool CanPlayPause => true;
    public bool IsClockType  => Def.Type == TimerType.Clock;

    public string PlayPauseIcon    => IsRunning ? "⏸" : "▶";
    public string TargetTimeDisplay => IsClockType ? $"Until {Def.ClockTime}" : string.Empty;

    public string DisplayText
    {
        get
        {
            int secs = Def.Type == TimerType.Clock ? ClockSecondsRemaining() : _currentSeconds;
            bool neg = secs < 0;
            int abs = Math.Abs(secs);
            int h = abs / 3600, m = (abs % 3600) / 60, s = abs % 60;
            string sign = neg ? "−" : string.Empty;
            return h > 0 ? $"{sign}{h}:{m:00}:{s:00}" : $"{sign}{m}:{s:00}";
        }
    }

    bool InWarning
    {
        get
        {
            if (!Def.WarnEnabled || Def.Type != TimerType.Counter) return false;
            int remaining = Def.StartSeconds > Def.EndSeconds
                ? _currentSeconds - Def.EndSeconds
                : Def.EndSeconds - _currentSeconds;
            return remaining >= 0 && remaining <= Def.WarnOffset;
        }
    }

    bool InOverflow
    {
        get
        {
            if (Def.Type != TimerType.Counter) return false;
            return Def.StartSeconds > Def.EndSeconds
                ? _currentSeconds < Def.EndSeconds
                : _currentSeconds > Def.EndSeconds;
        }
    }

    public IBrush DisplayBrush =>
        InOverflow ? new SolidColorBrush(Color.Parse("#FF4136")) :
        InWarning  ? new SolidColorBrush(Color.Parse("#FF8000")) :
                     Brushes.White;

    // ── Controls ──────────────────────────────────────────────────────────────

    public void PlayPause()
    {
        if (!CanPlayPause) return;
        if (IsRunning) Pause(); else Play();
    }

    public void Play()
    {
        if (IsRunning) return;
        IsRunning      = true;
        _startedAt     = DateTime.Now;
        _secondsAtStart = _currentSeconds;
        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTick);
        _ticker.Start();
    }

    public void Pause()
    {
        // Snap currentSeconds to the wall-clock value before stopping so Resume picks up correctly.
        if (IsRunning && Def.Type == TimerType.Counter)
            CurrentSeconds = WallClockSeconds();
        _ticker?.Stop();
        _ticker    = null;
        IsRunning  = false;
    }

    int WallClockSeconds()
    {
        double elapsed = (DateTime.Now - _startedAt).TotalSeconds;
        return Def.StartSeconds >= Def.EndSeconds
            ? _secondsAtStart - (int)elapsed
            : _secondsAtStart + (int)elapsed;
    }

    public void Reset()
    {
        if (Def.Type == TimerType.Clock)
        {
            // Clock: resume live tracking from current wall-clock time
            Pause();
            Play();
        }
        else
        {
            Pause();
            CurrentSeconds = Def.StartSeconds;
        }
    }

    // ── Tick ─────────────────────────────────────────────────────────────────

    void OnTick(object? sender, EventArgs e)
    {
        if (Def.Type == TimerType.Clock)
        {
            TimerTextCache.Update(Def.Id, DisplayText);
            this.RaisePropertyChanged(nameof(DisplayText));
            this.RaisePropertyChanged(nameof(DisplayBrush));
            return;
        }

        bool countDown = Def.StartSeconds >= Def.EndSeconds;
        int next = WallClockSeconds();

        bool hitEnd = countDown ? next <= Def.EndSeconds : next >= Def.EndSeconds;
        if (hitEnd && !Def.OverflowEnabled)
        {
            CurrentSeconds = Def.EndSeconds;
            Pause();
            return;
        }

        CurrentSeconds = next;
    }

    int ClockSecondsRemaining()
    {
        var parts = Def.ClockTime.Split(':');
        if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m))
            return 0;
        var now    = DateTime.Now;
        var target = new DateTime(now.Year, now.Month, now.Day, h, m, 0);
        if (target <= now) target = target.AddDays(1);
        return (int)(target - now).TotalSeconds;
    }

    public void RefreshAfterEdit()
    {
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(DisplayText));
        this.RaisePropertyChanged(nameof(DisplayBrush));
        this.RaisePropertyChanged(nameof(CanPlayPause));
        this.RaisePropertyChanged(nameof(PlayPauseIcon));
        this.RaisePropertyChanged(nameof(IsClockType));
        this.RaisePropertyChanged(nameof(TargetTimeDisplay));
    }

    public void Dispose()
    {
        _ticker?.Stop();
        _ticker = null;
        TimerTextCache.Remove(Def.Id);
    }
}
