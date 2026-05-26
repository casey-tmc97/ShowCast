using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Avalonia.Threading;
using LibVLCSharp.Shared;
using ReactiveUI;
using ShowCast.Core;
using AudioTrack = ShowCast.Core.AudioTrack;

namespace ShowCast.ViewModels;

public enum PlaybackState { Stopped, Playing, Paused }

public enum FileConflictChoice { Reuse, Replace, Skip }

public class AudioPlayerViewModel : ReactiveObject, IDisposable
{
    // ── LibVLC fields (null when unavailable) ─────────────────────────────────
    LibVLC?      _libVlc;
    MediaPlayer? _player;
    System.Timers.Timer? _poller;
    string? _pendingOutputModule;
    string? _pendingDeviceId;

    /// <summary>Exposes the LibVLC instance for device enumeration. Null if unavailable.</summary>
    public LibVLC? LibVlc => _libVlc;

    // ── Observable state ──────────────────────────────────────────────────────

    public ObservableCollection<AudioPlaylist>  Playlists { get; } = new();

    /// <summary>Row-level wrappers that carry IsPlaying / IsSelected state for the UI.</summary>
    public ObservableCollection<AudioTrackRow>  TrackList { get; } = new();

    AudioPlaylist? _selectedPlaylist;
    public AudioPlaylist? SelectedPlaylist
    {
        get => _selectedPlaylist;
        set
        {
            Stop();
            this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
            RefreshTrackList();
            RaisePlaylistPassthroughs();
            ApplyResume(value);
        }
    }

    AudioTrack? _currentTrack;
    public AudioTrack? CurrentTrack
    {
        get => _currentTrack;
        private set
        {
            // Persist last position on the outgoing track
            if (_currentTrack is not null && _selectedPlaylist is not null)
            {
                _selectedPlaylist.LastTrackId    = _currentTrack.Id;
                _selectedPlaylist.LastPositionMs = _player is not null ? Math.Max(0, _player.Time) : 0;
            }

            // Clear IsPlaying on the row that was playing
            var oldRow = TrackList.FirstOrDefault(r => r.Track == _currentTrack);
            if (oldRow is not null) oldRow.IsPlaying = false;

            this.RaiseAndSetIfChanged(ref _currentTrack, value);

            // Set IsPlaying on the new row
            var newRow = TrackList.FirstOrDefault(r => r.Track == _currentTrack);
            if (newRow is not null) newRow.IsPlaying = true;
        }
    }

    int _currentTrackIndex = -1;
    public int CurrentTrackIndex
    {
        get => _currentTrackIndex;
        private set => this.RaiseAndSetIfChanged(ref _currentTrackIndex, value);
    }

    // ── Selection (separate from currently-playing) ───────────────────────────

    AudioTrackRow? _selectedTrackRow;
    public AudioTrackRow? SelectedTrackRow
    {
        get => _selectedTrackRow;
        private set
        {
            if (_selectedTrackRow is not null) _selectedTrackRow.IsSelected = false;
            this.RaiseAndSetIfChanged(ref _selectedTrackRow, value);
            if (_selectedTrackRow is not null) _selectedTrackRow.IsSelected = true;
        }
    }

    /// <summary>Select a row (single click). Does not start playback.</summary>
    public void SelectTrack(AudioTrackRow row) => SelectedTrackRow = row;

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
        try
        {
            LibVLCSharp.Shared.Core.Initialize();
            _libVlc = new LibVLC();
            _player  = new MediaPlayer(_libVlc);

            _player.EndReached += (_, _) =>
                Dispatcher.UIThread.Post(Next);

            _poller = new System.Timers.Timer(250) { AutoReset = true };
            _poller.Elapsed += (_, _) =>
            {
                if (_player is null) return;
                long timeMs   = _player.Time;
                long lengthMs = _player.Length;
                Dispatcher.UIThread.Post(() =>
                {
                    if (timeMs >= 0)
                        Position = TimeSpan.FromMilliseconds(timeMs);
                    if (lengthMs > 0 && (long)_duration.TotalMilliseconds != lengthMs)
                        Duration = TimeSpan.FromMilliseconds(lengthMs);
                });
            };
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"[AudioPlayer] LibVLC init failed: {ex.Message}");
            IsUnavailable = true;
        }
    }

    // ── Playlist management ───────────────────────────────────────────────────

    public void LoadPlaylists(List<AudioPlaylist> playlists, Guid selectedId)
    {
        // Migrate: strip legacy (n) suffixes produced by the old auto-rename import logic.
        // RelativePath (the actual filename) is intentionally left unchanged.
        var legacySuffix = new System.Text.RegularExpressions.Regex(@"\(\d+\)$");
        foreach (var pl in playlists)
            foreach (var t in pl.Tracks)
                t.Title = legacySuffix.Replace(t.Title, "").TrimEnd();

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
        int idx = Playlists.IndexOf(playlist);
        if (idx >= 0) { Playlists.RemoveAt(idx); Playlists.Insert(idx, playlist); }
        SelectedPlaylist = playlist;
    }

    public void DeleteTrack(AudioTrack track)
    {
        if (CurrentTrack == track) Stop();
        _selectedPlaylist?.Tracks.Remove(track);

        var row = TrackList.FirstOrDefault(r => r.Track == track);
        if (row is not null)
        {
            if (_selectedTrackRow == row) SelectedTrackRow = null;
            TrackList.Remove(row);
        }

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

    // ── Playback ──────────────────────────────────────────────────────────────

    public void Play(AudioTrack? track = null)
    {
        if (_player is null || _selectedPlaylist is null) return;

        if (track is not null)
        {
            CurrentTrack      = track;
            CurrentTrackIndex = _selectedPlaylist.Tracks.IndexOf(track);
        }

        // If no current track, prefer the selected row, then fall back to first track
        if (CurrentTrack is null)
        {
            var start = _selectedTrackRow?.Track
                        ?? (_selectedPlaylist.Tracks.Count > 0 ? _selectedPlaylist.Tracks[0] : null);
            if (start is null) return;
            CurrentTrack      = start;
            CurrentTrackIndex = _selectedPlaylist.Tracks.IndexOf(start);
        }

        var absPath = Path.Combine(AppFolders.Media, CurrentTrack.RelativePath);
        if (!File.Exists(absPath))
        {
            StatusMessage = $"File not found: {CurrentTrack.RelativePath}";
            if (_selectedPlaylist.AutoAdvance)
            {
                int ni = PickNextIndex(_selectedPlaylist, CurrentTrackIndex);
                if (ni >= 0) Play(_selectedPlaylist.Tracks[ni]); else Stop();
            }
            return;
        }

        var oldMedia = _player.Media;
        var newMedia = new Media(_libVlc!, new Uri(absPath));
        _player.Media = newMedia;
        if (_pendingDeviceId is not null)
        {
            _player.SetOutputDevice(_pendingOutputModule!, _pendingDeviceId);
            _pendingDeviceId     = null;
            _pendingOutputModule = null;
        }
        _player.Play();
        _player.SetRate(_selectedPlaylist.Speed);
        _player.Volume = (int)Math.Round(_volume * 100);
        oldMedia?.Dispose();

        _poller?.Start();
        State         = PlaybackState.Playing;
        StatusMessage = "";
    }

    /// <summary>Sets the audio output device for the next Play() call.</summary>
    public void SetAudioDevice(string outputModule, string deviceId)
    {
        _pendingOutputModule = outputModule;
        _pendingDeviceId     = deviceId;
    }

    public void Pause()
    {
        if (_player is null) return;
        _player.Pause();
        _poller?.Stop();
        State = PlaybackState.Paused;
    }

    public void Stop()
    {
        _player?.Stop();
        _poller?.Stop();
        State             = PlaybackState.Stopped;
        Position          = TimeSpan.Zero;
        Duration          = TimeSpan.Zero;
        CurrentTrack      = null;
        CurrentTrackIndex = -1;
    }

    public void Next()
    {
        var pl = _selectedPlaylist;
        if (pl is null || pl.Tracks.Count == 0) { Stop(); return; }

        int nextIdx = PickNextIndex(pl, CurrentTrackIndex);
        if (nextIdx < 0 || !pl.AutoAdvance) { Stop(); return; }
        Play(pl.Tracks[nextIdx]);
    }

    public void Previous()
    {
        var pl = _selectedPlaylist;
        if (pl is null || pl.Tracks.Count == 0) return;

        if (_player is not null && _player.Time > 3000 && CurrentTrackIndex >= 0)
        {
            Seek(0);
            return;
        }

        int prevIdx = Math.Max(0, CurrentTrackIndex - 1);
        Play(pl.Tracks[prevIdx]);
    }

    public void Seek(double seconds)
    {
        if (_player is null) return;
        _player.Time = (long)(seconds * 1000);
    }

    public void SeekRelative(double deltaSeconds)
    {
        if (_player is null) return;
        double newPos = Math.Clamp(Position.TotalSeconds + deltaSeconds, 0, Duration.TotalSeconds);
        Seek(newPos);
    }

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
        _selectedPlaylist.LastPositionMs = _player is not null ? Math.Max(0, _player.Time) : 0;
    }

    // ── Import ────────────────────────────────────────────────────────────────

    /// <param name="sourcePaths">Full paths of files selected by the user.</param>
    /// <param name="onConflict">
    ///   Called when the destination filename already exists in the media folder.
    ///   Should return Reuse (keep existing), Replace (overwrite), or Skip.
    ///   When null, defaults to Reuse.
    /// </param>
    public async System.Threading.Tasks.Task ImportFilesAsync(
        IEnumerable<string> sourcePaths,
        Func<string, System.Threading.Tasks.Task<FileConflictChoice>>? onConflict = null)
    {
        var pl = _selectedPlaylist;
        if (pl is null) return;

        var mediaDir = AppFolders.Media;
        Directory.CreateDirectory(mediaDir);

        foreach (var src in sourcePaths)
        {
            var destName = Path.GetFileName(src);
            var dest     = Path.Combine(mediaDir, destName);

            if (File.Exists(dest))
            {
                var choice = onConflict is not null
                    ? await onConflict(destName)
                    : FileConflictChoice.Reuse;

                if (choice == FileConflictChoice.Skip)    continue;
                if (choice == FileConflictChoice.Replace) File.Copy(src, dest, overwrite: true);
                // Reuse: existing file is already in place — fall through to parse + add track
            }
            else
            {
                File.Copy(src, dest);
            }

            long durationMs = 0;
            if (_libVlc is not null)
            {
                try
                {
                    using var media = new Media(_libVlc, new Uri(dest));
                    await media.Parse(MediaParseOptions.ParseLocal);
                    durationMs = media.Duration;
                }
                catch { /* leave duration as 0 */ }
            }

            var track = new AudioTrack
            {
                Title        = Path.GetFileNameWithoutExtension(destName),
                RelativePath = destName,
                DurationMs   = durationMs
            };

            pl.Tracks.Add(track);
            TrackList.Add(new AudioTrackRow(track));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void ApplyResume(AudioPlaylist? playlist)
    {
        if (playlist is null || playlist.ResumeMode == ResumeMode.FromTop) return;

        var track = playlist.Tracks.FirstOrDefault(t => t.Id == playlist.LastTrackId);
        if (track is null) return;

        CurrentTrack      = track;
        CurrentTrackIndex = playlist.Tracks.IndexOf(track);
    }

    void RefreshTrackList()
    {
        // Clear selection when switching playlists
        _selectedTrackRow = null;
        this.RaisePropertyChanged(nameof(SelectedTrackRow));

        TrackList.Clear();
        if (_selectedPlaylist is null) return;

        foreach (var t in _selectedPlaylist.Tracks)
        {
            var row = new AudioTrackRow(t) { IsPlaying = t == _currentTrack };
            TrackList.Add(row);
        }
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

    void ApplyVolume()
    {
        if (_player is null) return;
        _player.Volume = (int)Math.Round(_volume * 100);
    }

    void ApplySpeed()
    {
        if (_player is null) return;
        _player.SetRate(_selectedPlaylist?.Speed ?? 1.0f);
    }

    public void Dispose()
    {
        _poller?.Stop();
        _poller?.Dispose();
        if (_player is not null)
        {
            _player.Stop();
            _player.Dispose();
        }
        _libVlc?.Dispose();
    }
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
