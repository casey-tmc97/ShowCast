using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public enum PlaybackState { Stopped, Playing, Paused }

public class AudioPlayerViewModel : ReactiveObject, IDisposable
{
    // ── LibVLC fields (null when unavailable) ─────────────────────────────────
    // LibVLC objects are added in Task 5. Declared here so the class compiles.
    bool _libVlcReady;

    // ── Observable state ──────────────────────────────────────────────────────

    public ObservableCollection<AudioPlaylist>  Playlists { get; } = new();
    public ObservableCollection<AudioTrack>     TrackList { get; } = new();

    AudioPlaylist? _selectedPlaylist;
    public AudioPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
            RefreshTrackList();
            RaisePlaylistPassthroughs();
        }
    }

    AudioTrack? _currentTrack;
    public AudioTrack? CurrentTrack
    {
        get => _currentTrack;
        private set => this.RaiseAndSetIfChanged(ref _currentTrack, value);
    }

    int _currentTrackIndex = -1;
    public int CurrentTrackIndex
    {
        get => _currentTrackIndex;
        private set => this.RaiseAndSetIfChanged(ref _currentTrackIndex, value);
    }

    PlaybackState _state = PlaybackState.Stopped;
    public PlaybackState State
    {
        get => _state;
        set
        {
            this.RaiseAndSetIfChanged(ref _state, value);
            this.RaisePropertyChanged(nameof(PlayPauseIcon));
        }
    }

    TimeSpan _position;
    public TimeSpan Position
    {
        get => _position;
        set
        {
            this.RaiseAndSetIfChanged(ref _position, value);
            this.RaisePropertyChanged(nameof(PositionSeconds));
            this.RaisePropertyChanged(nameof(PositionDisplay));
        }
    }

    TimeSpan _duration;
    public TimeSpan Duration
    {
        get => _duration;
        set
        {
            this.RaiseAndSetIfChanged(ref _duration, value);
            this.RaisePropertyChanged(nameof(PositionDisplay));
        }
    }

    double _volume = 0.8;
    public double Volume
    {
        get => _volume;
        set
        {
            this.RaiseAndSetIfChanged(ref _volume, value);
            this.RaisePropertyChanged(nameof(VolumeDisplay));
            ApplyVolume();
        }
    }

    bool _isUnavailable;
    public bool IsUnavailable
    {
        get => _isUnavailable;
        private set => this.RaiseAndSetIfChanged(ref _isUnavailable, value);
    }

    string _statusMessage = "";
    public string StatusMessage
    {
        get => _statusMessage;
        set => this.RaiseAndSetIfChanged(ref _statusMessage, value);
    }

    // ── Computed / passthrough ────────────────────────────────────────────────

    public double PositionSeconds => Position.TotalSeconds;

    public string PlayPauseIcon => State == PlaybackState.Playing ? "⏸" : "▶";

    public string PositionDisplay
    {
        get
        {
            static string Fmt(TimeSpan t) => t.Hours > 0
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
            return $"{Fmt(Position)} / {Fmt(Duration)}";
        }
    }

    public string VolumeDisplay => $"{(int)Math.Round(_volume * 100)}%";

    // Passthrough to SelectedPlaylist — safe when null
    public bool AutoAdvance
    {
        get => _selectedPlaylist?.AutoAdvance ?? true;
        set { if (_selectedPlaylist is not null) { _selectedPlaylist.AutoAdvance = value; this.RaisePropertyChanged(nameof(AutoAdvance)); } }
    }

    public bool Shuffle
    {
        get => _selectedPlaylist?.Shuffle ?? false;
        set { if (_selectedPlaylist is not null) { _selectedPlaylist.Shuffle = value; this.RaisePropertyChanged(nameof(Shuffle)); } }
    }

    public RepeatMode Repeat     => _selectedPlaylist?.Repeat ?? RepeatMode.None;
    public bool IsResumeFromTop  => _selectedPlaylist?.ResumeMode == ResumeMode.FromTop;
    public bool IsResumeFromLast => _selectedPlaylist?.ResumeMode == ResumeMode.FromLastPosition;
    public float Speed           => _selectedPlaylist?.Speed ?? 1.0f;

    public string RepeatLabel => (_selectedPlaylist?.Repeat ?? RepeatMode.None) switch
    {
        RepeatMode.None => "🔁 Off",
        RepeatMode.One  => "🔂 One",
        RepeatMode.All  => "🔁 All",
        _               => "🔁 Off"
    };

    public string SpeedLabel => (_selectedPlaylist?.Speed ?? 1.0f) switch
    {
        0.5f  => ".5×",
        0.75f => ".75×",
        1.25f => "1.25×",
        1.5f  => "1.5×",
        2.0f  => "2×",
        _     => "1×"
    };

    // ── Static converter (used in AXAML) ──────────────────────────────────────
    public static readonly MsToTimeStringConverter MsToTimeConverter = new();

    // ── Constructor ───────────────────────────────────────────────────────────

    public AudioPlayerViewModel()
    {
        // LibVLC initialization added in Task 5
    }

    // ── Playlist management ───────────────────────────────────────────────────

    public void LoadPlaylists(List<AudioPlaylist> playlists, Guid selectedId)
    {
        Playlists.Clear();
        foreach (var pl in playlists) Playlists.Add(pl);
        SelectedPlaylist = Playlists.FirstOrDefault(p => p.Id == selectedId)
                           ?? Playlists.FirstOrDefault();
    }

    public void CreatePlaylist(string name)
    {
        var pl = new AudioPlaylist { Name = name };
        Playlists.Add(pl);
        SelectedPlaylist = pl;
    }

    public void DeletePlaylist(AudioPlaylist playlist)
    {
        if (CurrentTrack is not null && _selectedPlaylist == playlist)
            Stop();
        Playlists.Remove(playlist);
        if (SelectedPlaylist == playlist)
            SelectedPlaylist = Playlists.Count > 0 ? Playlists[0] : null;
    }

    public void RenamePlaylist(AudioPlaylist playlist, string name)
    {
        playlist.Name = name;
        // Force ComboBox to refresh
        int idx = Playlists.IndexOf(playlist);
        if (idx >= 0) { Playlists.RemoveAt(idx); Playlists.Insert(idx, playlist); }
        SelectedPlaylist = playlist;
    }

    public void DeleteTrack(AudioTrack track)
    {
        if (CurrentTrack == track) Stop();
        _selectedPlaylist?.Tracks.Remove(track);
        TrackList.Remove(track);
        if (CurrentTrack == track) { CurrentTrack = null; CurrentTrackIndex = -1; }
    }

    // ── Static next-index logic (testable without LibVLC) ─────────────────────

    static readonly Random _rng = new();

    public static int PickNextIndex(AudioPlaylist playlist, int currentIndex)
    {
        if (playlist.Tracks.Count == 0) return -1;

        if (playlist.Shuffle)
        {
            if (playlist.Tracks.Count == 1) return 0;
            int next;
            do { next = _rng.Next(playlist.Tracks.Count); } while (next == currentIndex);
            return next;
        }

        int nextIdx = currentIndex + 1;
        if (nextIdx < playlist.Tracks.Count) return nextIdx;

        return playlist.Repeat switch
        {
            RepeatMode.All  => 0,
            RepeatMode.One  => currentIndex < 0 ? 0 : currentIndex,
            _               => -1
        };
    }

    // ── Playback stubs (implemented in Task 5) ────────────────────────────────

    public void Play(AudioTrack? track = null) { }
    public void Pause()                        { }
    public void Stop()                         { State = PlaybackState.Stopped; Position = TimeSpan.Zero; }
    public void Next()                         { }
    public void Previous()                     { }
    public void Seek(double seconds)           { }
    public void SeekRelative(double deltaSeconds) { }
    public void CycleRepeat()
    {
        if (_selectedPlaylist is null) return;
        _selectedPlaylist.Repeat = _selectedPlaylist.Repeat switch
        {
            RepeatMode.None => RepeatMode.One,
            RepeatMode.One  => RepeatMode.All,
            _               => RepeatMode.None
        };
        this.RaisePropertyChanged(nameof(Repeat));
        this.RaisePropertyChanged(nameof(RepeatLabel));
    }

    public void SetSpeed(float speed)
    {
        if (_selectedPlaylist is null) return;
        _selectedPlaylist.Speed = speed;
        ApplySpeed();
        this.RaisePropertyChanged(nameof(Speed));
        this.RaisePropertyChanged(nameof(SpeedLabel));
    }

    public void SetResumeMode(ResumeMode mode)
    {
        if (_selectedPlaylist is null) return;
        _selectedPlaylist.ResumeMode = mode;
        this.RaisePropertyChanged(nameof(IsResumeFromTop));
        this.RaisePropertyChanged(nameof(IsResumeFromLast));
    }

    // ── Persistence ───────────────────────────────────────────────────────────

    /// <summary>Called by MainViewModel before saving to persist the current playback position.</summary>
    public void PersistPlaybackState()
    {
        if (_selectedPlaylist is null || _currentTrack is null) return;
        _selectedPlaylist.LastTrackId    = _currentTrack.Id;
        _selectedPlaylist.LastPositionMs = 0; // updated in Task 5 when _player is wired
    }

    // ── Import stub (implemented in Task 5) ───────────────────────────────────

    public System.Threading.Tasks.Task ImportFilesAsync(IEnumerable<string> paths) =>
        System.Threading.Tasks.Task.CompletedTask;

    // ── Helpers ───────────────────────────────────────────────────────────────

    void RefreshTrackList()
    {
        TrackList.Clear();
        if (_selectedPlaylist is null) return;
        foreach (var t in _selectedPlaylist.Tracks) TrackList.Add(t);
    }

    void RaisePlaylistPassthroughs()
    {
        this.RaisePropertyChanged(nameof(AutoAdvance));
        this.RaisePropertyChanged(nameof(Shuffle));
        this.RaisePropertyChanged(nameof(Repeat));
        this.RaisePropertyChanged(nameof(RepeatLabel));
        this.RaisePropertyChanged(nameof(IsResumeFromTop));
        this.RaisePropertyChanged(nameof(IsResumeFromLast));
        this.RaisePropertyChanged(nameof(Speed));
        this.RaisePropertyChanged(nameof(SpeedLabel));
    }

    void ApplyVolume() { /* wired to _player in Task 5 */ }
    void ApplySpeed()  { /* wired to _player in Task 5 */ }

    public void Dispose() { }
}

public class MsToTimeStringConverter : Avalonia.Data.Converters.IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
    {
        if (value is long ms && ms > 0)
        {
            var t = TimeSpan.FromMilliseconds(ms);
            return t.Hours > 0
                ? $"{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}"
                : $"{t.Minutes}:{t.Seconds:00}";
        }
        return "—";
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, System.Globalization.CultureInfo culture)
        => Avalonia.Data.BindingOperations.DoNothing;
}
