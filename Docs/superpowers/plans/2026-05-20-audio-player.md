# Audio Player Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a multi-playlist audio player to ShowCast's right panel, accessed via a TIMERS / AUDIO tab strip that replaces the current TimerPanel header.

**Architecture:** `TabbedRightPanel` (new UserControl) replaces `<views:TimerPanel />` in `MainWindow.axaml`. It hosts a custom tab strip and both panels (always in visual tree; `IsVisible` toggles). `AudioPlayerViewModel` wraps LibVLCSharp and owns all playlist management. Playlists persist in `ShowFile.AudioPlaylists`; audio files are copied to `AppFolders.Media` on import and tracked by filename.

**Tech Stack:** .NET 9, Avalonia 11.2.2, ReactiveUI 20.x, LibVLCSharp 3.x, VideoLAN.LibVLC.Windows 3.x, xUnit

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `Core/AudioTrack.cs` | Track model |
| Create | `Core/AudioPlaylist.cs` | Playlist model + `RepeatMode` + `ResumeMode` enums |
| Modify | `Core/ShowFile.cs` | Add `AudioPlaylists` list |
| Modify | `Core/AppSettings.cs` | Add `SelectedAudioPlaylistId` |
| Create | `ViewModels/AudioPlayerViewModel.cs` | All playback state + playlist management |
| Modify | `ViewModels/MainViewModel.cs` | Add `AudioPlayer` property; wire load/save |
| Modify | `Views/TimerPanel.axaml` | Remove header section (tabs take over) |
| Modify | `Views/TimerPanel.axaml.cs` | Remove `OnAddTimer` handler |
| Create | `Views/TabbedRightPanel.axaml` + `.cs` | Tab strip + hosts both panels |
| Create | `Views/AudioPlayerPanel.axaml` + `.cs` | Full audio player UI |
| Modify | `Views/MainWindow.axaml` | Replace `<views:TimerPanel />` with `<views:TabbedRightPanel />` |
| Modify | `ShowCast.csproj` | Add LibVLCSharp NuGet refs |
| Create | `ShowCast.Tests/ViewModels/AudioPlayerViewModelTests.cs` | Playlist CRUD + next-index logic tests |
| Modify | `ShowCast.Tests/Core/ShowFileSerializerTests.cs` | Round-trip test for AudioPlaylists |

---

### Task 1: Add NuGet packages

**Files:**
- Modify: `ShowCast.csproj`

- [ ] **Step 1: Add packages**

Open `ShowCast.csproj` and add inside the existing `<ItemGroup>` that has other `PackageReference` entries:

```xml
<PackageReference Include="LibVLCSharp"             Version="3.9.0" />
<PackageReference Include="VideoLAN.LibVLC.Windows" Version="3.0.21" />
```

- [ ] **Step 2: Restore and verify**

```powershell
dotnet restore ShowCast.csproj
```

Expected: no errors, packages listed in output.

- [ ] **Step 3: Commit**

```powershell
git add ShowCast.csproj
git commit -m "build: add LibVLCSharp and VideoLAN.LibVLC.Windows packages"
```

---

### Task 2: Core audio models

**Files:**
- Create: `Core/AudioTrack.cs`
- Create: `Core/AudioPlaylist.cs`

- [ ] **Step 1: Create AudioTrack**

Create `Core/AudioTrack.cs`:

```csharp
namespace ShowCast.Core;

public class AudioTrack
{
    public Guid   Id           { get; set; } = Guid.NewGuid();
    public string Title        { get; set; } = "";
    /// <summary>Filename only — resolved against AppFolders.Media at runtime.</summary>
    public string RelativePath { get; set; } = "";
    public long   DurationMs   { get; set; }
}
```

- [ ] **Step 2: Create AudioPlaylist**

Create `Core/AudioPlaylist.cs`:

```csharp
namespace ShowCast.Core;

public enum RepeatMode { None, One, All }
public enum ResumeMode { FromTop, FromLastPosition }

public class AudioPlaylist
{
    public Guid             Id             { get; set; } = Guid.NewGuid();
    public string           Name           { get; set; } = "New Playlist";
    public List<AudioTrack> Tracks         { get; set; } = new();
    public bool             AutoAdvance    { get; set; } = true;
    public bool             Shuffle        { get; set; } = false;
    public RepeatMode       Repeat         { get; set; } = RepeatMode.None;
    public float            Speed          { get; set; } = 1.0f;
    public ResumeMode       ResumeMode     { get; set; } = ResumeMode.FromTop;
    public Guid             LastTrackId    { get; set; }
    public long             LastPositionMs { get; set; }
}
```

- [ ] **Step 3: Build to confirm no errors**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add Core/AudioTrack.cs Core/AudioPlaylist.cs
git commit -m "feat: add AudioTrack and AudioPlaylist core models"
```

---

### Task 3: Extend ShowFile + AppSettings; add serializer round-trip test

**Files:**
- Modify: `Core/ShowFile.cs`
- Modify: `Core/AppSettings.cs`
- Modify: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Step 1: Write failing test**

Add to `ShowCast.Tests/Core/ShowFileSerializerTests.cs`:

```csharp
[Fact]
public async Task SaveAsync_LoadAsync_RoundTripsAudioPlaylists()
{
    var file = new ShowFile();
    var playlist = new AudioPlaylist { Name = "Sunday Set" };
    playlist.Tracks.Add(new AudioTrack { Title = "Song 1", RelativePath = "song1.mp3", DurationMs = 180000 });
    file.AudioPlaylists.Add(playlist);

    var path = Path.GetTempFileName();
    try
    {
        await ShowFileSerializer.SaveAsync(file, path);
        var result = await ShowFileSerializer.LoadAsync(path);

        Assert.NotNull(result);
        Assert.Single(result.File.AudioPlaylists);
        Assert.Equal("Sunday Set", result.File.AudioPlaylists[0].Name);
        Assert.Single(result.File.AudioPlaylists[0].Tracks);
        Assert.Equal("Song 1", result.File.AudioPlaylists[0].Tracks[0].Title);
        Assert.Equal(180000L, result.File.AudioPlaylists[0].Tracks[0].DurationMs);
    }
    finally { File.Delete(path); File.Delete(path + ".tmp"); }
}

[Fact]
public async Task LoadAsync_OldFile_GetsEmptyAudioPlaylists()
{
    var path = Path.GetTempFileName();
    try
    {
        // Old file with no AudioPlaylists field
        var json = """{ "Version": 1 }""";
        await File.WriteAllTextAsync(path, json);

        var result = await ShowFileSerializer.LoadAsync(path);

        Assert.NotNull(result);
        Assert.Empty(result.File.AudioPlaylists);
    }
    finally { File.Delete(path); }
}
```

- [ ] **Step 2: Run tests — confirm they fail**

```powershell
dotnet test ShowCast.Tests --filter "SaveAsync_LoadAsync_RoundTripsAudioPlaylists|LoadAsync_OldFile_GetsEmptyAudioPlaylists" -v normal
```

Expected: compilation error — `AudioPlaylists` does not exist on `ShowFile`.

- [ ] **Step 3: Add AudioPlaylists to ShowFile**

In `Core/ShowFile.cs`, add after `ScheduledEvents`:

```csharp
public List<AudioPlaylist> AudioPlaylists { get; } = new();
```

- [ ] **Step 4: Add SelectedAudioPlaylistId to AppSettings**

In `Core/AppSettings.cs`, add after `SelectedPackageItemId`:

```csharp
public Guid SelectedAudioPlaylistId { get; set; } = Guid.Empty;
```

- [ ] **Step 5: Run tests — confirm they pass**

```powershell
dotnet test ShowCast.Tests --filter "SaveAsync_LoadAsync_RoundTripsAudioPlaylists|LoadAsync_OldFile_GetsEmptyAudioPlaylists" -v normal
```

Expected: PASS (STJ Populate populates the existing list; missing field leaves it empty).

- [ ] **Step 6: Run full test suite**

```powershell
dotnet test ShowCast.Tests -v normal
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```powershell
git add Core/ShowFile.cs Core/AppSettings.cs ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat: add AudioPlaylists to ShowFile and AppSettings; add serializer tests"
```

---

### Task 4: AudioPlayerViewModel — playlist CRUD, track management, and next-index logic

**Files:**
- Create: `ViewModels/AudioPlayerViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/AudioPlayerViewModelTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ShowCast.Tests/ViewModels/AudioPlayerViewModelTests.cs`:

```csharp
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class AudioPlayerViewModelTests
{
    // ── Playlist CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public void CreatePlaylist_AddsToPlaylists_AndSelectsIt()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("Sunday Service");

        Assert.Single(vm.Playlists);
        Assert.Equal("Sunday Service", vm.Playlists[0].Name);
        Assert.Same(vm.Playlists[0], vm.SelectedPlaylist);
        vm.Dispose();
    }

    [Fact]
    public void DeletePlaylist_RemovesFromPlaylists()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("A");
        vm.CreatePlaylist("B");
        var first = vm.Playlists[0];

        vm.DeletePlaylist(first);

        Assert.Single(vm.Playlists);
        Assert.Equal("B", vm.Playlists[0].Name);
        vm.Dispose();
    }

    [Fact]
    public void DeletePlaylist_WhenOnlyOne_SetsSelectedPlaylistNull()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("Only");
        vm.DeletePlaylist(vm.Playlists[0]);

        Assert.Empty(vm.Playlists);
        Assert.Null(vm.SelectedPlaylist);
        vm.Dispose();
    }

    [Fact]
    public void RenamePlaylist_UpdatesName()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("Old");
        vm.RenamePlaylist(vm.Playlists[0], "New");

        Assert.Equal("New", vm.Playlists[0].Name);
        vm.Dispose();
    }

    [Fact]
    public void DeleteTrack_RemovesFromTrackListAndPlaylist()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("P");
        var track = new AudioTrack { Title = "T1", RelativePath = "t1.mp3" };
        vm.SelectedPlaylist!.Tracks.Add(track);
        vm.TrackList.Add(track);

        vm.DeleteTrack(track);

        Assert.Empty(vm.TrackList);
        Assert.Empty(vm.SelectedPlaylist.Tracks);
        vm.Dispose();
    }

    [Fact]
    public void LoadPlaylists_PopulatesPlaylists()
    {
        var vm = new AudioPlayerViewModel();
        var list = new List<AudioPlaylist>
        {
            new() { Name = "Alpha" },
            new() { Name = "Beta"  }
        };

        vm.LoadPlaylists(list, selectedId: Guid.Empty);

        Assert.Equal(2, vm.Playlists.Count);
        Assert.Equal("Alpha", vm.Playlists[0].Name);
        vm.Dispose();
    }

    [Fact]
    public void LoadPlaylists_RestoresSelectedPlaylistById()
    {
        var vm   = new AudioPlayerViewModel();
        var beta = new AudioPlaylist { Name = "Beta" };
        var list = new List<AudioPlaylist> { new() { Name = "Alpha" }, beta };

        vm.LoadPlaylists(list, selectedId: beta.Id);

        Assert.Same(beta, vm.SelectedPlaylist);
        vm.Dispose();
    }

    // ── Next-index logic ──────────────────────────────────────────────────────

    [Fact]
    public void PickNextIndex_MiddleOfPlaylist_ReturnsNextIndex()
    {
        var pl = new AudioPlaylist();
        pl.Tracks.AddRange(new[] {
            new AudioTrack(), new AudioTrack(), new AudioTrack()
        });
        Assert.Equal(2, AudioPlayerViewModel.PickNextIndex(pl, 1));
    }

    [Fact]
    public void PickNextIndex_EndNoRepeat_ReturnsMinusOne()
    {
        var pl = new AudioPlaylist { Repeat = RepeatMode.None };
        pl.Tracks.Add(new AudioTrack());
        Assert.Equal(-1, AudioPlayerViewModel.PickNextIndex(pl, 0));
    }

    [Fact]
    public void PickNextIndex_EndRepeatAll_ReturnsZero()
    {
        var pl = new AudioPlaylist { Repeat = RepeatMode.All };
        pl.Tracks.AddRange(new[] { new AudioTrack(), new AudioTrack() });
        Assert.Equal(0, AudioPlayerViewModel.PickNextIndex(pl, 1));
    }

    [Fact]
    public void PickNextIndex_EndRepeatOne_ReturnsSameIndex()
    {
        var pl = new AudioPlaylist { Repeat = RepeatMode.One };
        pl.Tracks.AddRange(new[] { new AudioTrack(), new AudioTrack() });
        Assert.Equal(1, AudioPlayerViewModel.PickNextIndex(pl, 1));
    }

    [Fact]
    public void PickNextIndex_EmptyPlaylist_ReturnsMinusOne()
    {
        var pl = new AudioPlaylist();
        Assert.Equal(-1, AudioPlayerViewModel.PickNextIndex(pl, 0));
    }

    [Fact]
    public void PickNextIndex_Shuffle_ReturnsValidIndexDifferentFromCurrent()
    {
        var pl = new AudioPlaylist { Shuffle = true };
        for (int i = 0; i < 5; i++) pl.Tracks.Add(new AudioTrack());

        // Run many times to confirm it never returns the current index
        for (int i = 0; i < 50; i++)
        {
            int next = AudioPlayerViewModel.PickNextIndex(pl, 2);
            Assert.InRange(next, 0, pl.Tracks.Count - 1);
            Assert.NotEqual(2, next);
        }
    }
}
```

- [ ] **Step 2: Run tests — confirm they fail**

```powershell
dotnet test ShowCast.Tests --filter "AudioPlayerViewModelTests" -v normal
```

Expected: compilation error — `AudioPlayerViewModel` does not exist.

- [ ] **Step 3: Create AudioPlayerViewModel (playlist CRUD + static logic, no LibVLC yet)**

Create `ViewModels/AudioPlayerViewModel.cs`:

```csharp
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

    public System.Collections.Generic.Dictionary<Guid, Guid> GetSelectedPlaylistId()
        => new() { [Guid.Empty] = _selectedPlaylist?.Id ?? Guid.Empty };

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
```

- [ ] **Step 4: Run tests — confirm they pass**

```powershell
dotnet test ShowCast.Tests --filter "AudioPlayerViewModelTests" -v normal
```

Expected: all 11 tests PASS.

- [ ] **Step 5: Build main project**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Commit**

```powershell
git add ViewModels/AudioPlayerViewModel.cs ShowCast.Tests/ViewModels/AudioPlayerViewModelTests.cs
git commit -m "feat: add AudioPlayerViewModel with playlist CRUD and next-index logic"
```

---

### Task 5: Wire LibVLC playback engine + file import

**Files:**
- Modify: `ViewModels/AudioPlayerViewModel.cs`

- [ ] **Step 1: Add using directives and LibVLC fields**

At the top of `ViewModels/AudioPlayerViewModel.cs`, add:

```csharp
using LibVLCSharp.Shared;
using Avalonia.Threading;
```

Inside the class, replace the `bool _libVlcReady;` placeholder with:

```csharp
LibVLC?      _libVlc;
MediaPlayer? _player;
System.Timers.Timer? _poller;
```

- [ ] **Step 2: Initialize LibVLC in constructor**

Replace the constructor body (currently empty) with:

```csharp
public AudioPlayerViewModel()
{
    try
    {
        Core.Initialize();
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
                {
                    Position = TimeSpan.FromMilliseconds(timeMs);
                }
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
```

- [ ] **Step 3: Implement Play**

Replace the `Play` stub:

```csharp
public void Play(AudioTrack? track = null)
{
    if (_player is null || _selectedPlaylist is null) return;

    if (track is not null)
    {
        CurrentTrack      = track;
        CurrentTrackIndex = _selectedPlaylist.Tracks.IndexOf(track);
    }

    if (CurrentTrack is null)
    {
        if (_selectedPlaylist.Tracks.Count == 0) return;
        CurrentTrack      = _selectedPlaylist.Tracks[0];
        CurrentTrackIndex = 0;
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
    _player.Play();
    _player.Rate   = _selectedPlaylist.Speed;
    _player.Volume = (int)Math.Round(_volume * 100);
    oldMedia?.Dispose();

    _poller?.Start();
    State         = PlaybackState.Playing;
    StatusMessage = "";
}
```

- [ ] **Step 4: Implement Pause, Stop, Next, Previous, Seek**

Replace the stubs:

```csharp
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
    State         = PlaybackState.Stopped;
    Position      = TimeSpan.Zero;
    Duration      = TimeSpan.Zero;
    CurrentTrack  = null;
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

    // Restart current track if more than 3 seconds in; otherwise go back one
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
```

- [ ] **Step 5: Implement ApplyVolume and ApplySpeed**

Replace the `ApplyVolume` and `ApplySpeed` stubs:

```csharp
void ApplyVolume()
{
    if (_player is null) return;
    _player.Volume = (int)Math.Round(_volume * 100);
}

void ApplySpeed()
{
    if (_player is null) return;
    _player.Rate = _selectedPlaylist?.Speed ?? 1.0f;
}
```

- [ ] **Step 6: Implement ImportFilesAsync**

Replace the `ImportFilesAsync` stub:

```csharp
public async System.Threading.Tasks.Task ImportFilesAsync(IEnumerable<string> sourcePaths)
{
    var pl = _selectedPlaylist;
    if (pl is null) return;

    var mediaDir = AppFolders.Media;
    Directory.CreateDirectory(mediaDir);

    foreach (var src in sourcePaths)
    {
        var destName = Path.GetFileName(src);
        var dest     = Path.Combine(mediaDir, destName);

        // Deduplicate filename
        if (File.Exists(dest))
        {
            var baseName = Path.GetFileNameWithoutExtension(destName);
            var ext      = Path.GetExtension(destName);
            int n = 1;
            do
            {
                destName = $"{baseName}({n}){ext}";
                dest     = Path.Combine(mediaDir, destName);
                n++;
            } while (File.Exists(dest));
        }

        File.Copy(src, dest);

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
        TrackList.Add(track);
    }
}
```

- [ ] **Step 7: Implement Dispose**

Replace the `Dispose` stub:

```csharp
public void Dispose()
{
    _poller?.Stop();
    _poller?.Dispose();
    if (_player is not null)
    {
        _player.EndReached -= (_, _) => Dispatcher.UIThread.Post(Next);
        _player.Stop();
        _player.Dispose();
    }
    _libVlc?.Dispose();
}
```

- [ ] **Step 8: Build to confirm no errors**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 9: Run all tests**

```powershell
dotnet test ShowCast.Tests -v normal
```

Expected: all tests pass (LibVLC tests either pass with VLC DLLs present or skip gracefully).

- [ ] **Step 10: Commit**

```powershell
git add ViewModels/AudioPlayerViewModel.cs
git commit -m "feat: implement LibVLC playback engine and file import in AudioPlayerViewModel"
```

---

### Task 6: Integrate AudioPlayerViewModel into MainViewModel

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add AudioPlayer property**

In `MainViewModel.cs`, add near the top of the class (after the `ShowFile` field):

```csharp
public AudioPlayerViewModel AudioPlayer { get; } = new();
```

- [ ] **Step 2: Wire LoadPlaylists in RebuildFromShowFile**

Inside `RebuildFromShowFile()`, after the existing `foreach (var def in _showFile.Timers)` block, add:

```csharp
AudioPlayer.LoadPlaylists(
    _showFile.AudioPlaylists,
    _showFile.Settings.SelectedAudioPlaylistId);
```

- [ ] **Step 3: Wire save in SaveSessionAsync**

Inside `SaveSessionAsync()`, after `s.SelectedPackageItemId = ...`, add:

```csharp
s.SelectedAudioPlaylistId = AudioPlayer.SelectedPlaylist?.Id ?? Guid.Empty;
```

- [ ] **Step 4: Add AudioPlaylists sync before save**

Still inside `SaveSessionAsync()`, before the `await ShowFileSerializer.SaveAsync(...)` call, add:

```csharp
// Sync AudioPlayer playlists back to the ShowFile model before saving
_showFile.AudioPlaylists.Clear();
foreach (var pl in AudioPlayer.Playlists)
    _showFile.AudioPlaylists.Add(pl);
```

- [ ] **Step 5: Dispose AudioPlayer in RebuildFromShowFile**

At the top of `RebuildFromShowFile()`, this is called when loading a new file. The `AudioPlayer` is long-lived (not recreated), so just reload it — the `LoadPlaylists` call in Step 2 already handles that. No extra disposal needed.

- [ ] **Step 6: Build**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 7: Commit**

```powershell
git add ViewModels/MainViewModel.cs
git commit -m "feat: integrate AudioPlayerViewModel into MainViewModel save/load cycle"
```

---

### Task 7: Restructure TimerPanel (remove its header)

The tab strip in `TabbedRightPanel` (Task 8) takes over the panel header and the "+" add-timer button. The `TimerPanel` itself becomes header-free.

**Files:**
- Modify: `Views/TimerPanel.axaml`
- Modify: `Views/TimerPanel.axaml.cs`

- [ ] **Step 1: Remove the header block from TimerPanel.axaml**

In `Views/TimerPanel.axaml`, delete the entire `<!-- Header -->` block (lines 9–27):

```xml
<!-- DELETE THIS ENTIRE BLOCK:
        <Border DockPanel.Dock="Top" Classes="panel-header">
            <Grid ColumnDefinitions="*,Auto">
                <TextBlock Text="TIMERS" VerticalAlignment="Center"/>
                <Button Grid.Column="1"
                        Content="+" Width="24" Height="24" Padding="0"
                        Background="#555555" Foreground="White"
                        FontSize="16" FontWeight="Bold"
                        CornerRadius="4" BorderThickness="0"
                        HorizontalContentAlignment="Center"
                        VerticalContentAlignment="Center"
                        Click="OnAddTimer">
                    <Button.Styles>
                        <Style Selector="Button:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#666666"/>
                        </Style>
                    </Button.Styles>
                </Button>
            </Grid>
        </Border>
-->
```

After deletion, `TimerPanel.axaml` should start at `<DockPanel>` and immediately have `<!-- Empty hint -->` as the first child.

- [ ] **Step 2: Remove OnAddTimer from TimerPanel.axaml.cs**

In `Views/TimerPanel.axaml.cs`, delete the entire `OnAddTimer` method:

```csharp
// DELETE:
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
```

- [ ] **Step 3: Build**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```powershell
git add Views/TimerPanel.axaml Views/TimerPanel.axaml.cs
git commit -m "refactor: strip header from TimerPanel (tab strip takes over)"
```

---

### Task 8: TabbedRightPanel

**Files:**
- Create: `Views/TabbedRightPanel.axaml`
- Create: `Views/TabbedRightPanel.axaml.cs`

- [ ] **Step 1: Create TabbedRightPanel.axaml**

Create `Views/TabbedRightPanel.axaml`:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ShowCast.ViewModels"
             xmlns:views="using:ShowCast.Views"
             x:Class="ShowCast.Views.TabbedRightPanel">

    <DockPanel LastChildFill="True">

        <!-- ── Tab strip (replaces TimerPanel's old header) ── -->
        <Border DockPanel.Dock="Top" Classes="panel-header" Padding="0">
            <Grid ColumnDefinitions="Auto,Auto,*,Auto">

                <!-- TIMERS tab -->
                <Button x:Name="TimersTabBtn"
                        Grid.Column="0"
                        Content="TIMERS"
                        Click="OnTimersTab"
                        Padding="14,6"
                        Background="#1e1e1e"
                        Foreground="White"
                        FontWeight="Bold"
                        FontSize="12"
                        BorderThickness="0,0,0,3"
                        BorderBrush="#e07050"
                        CornerRadius="0">
                    <Button.Styles>
                        <Style Selector="Button:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#2a2a2a"/>
                        </Style>
                    </Button.Styles>
                </Button>

                <!-- AUDIO tab -->
                <Button x:Name="AudioTabBtn"
                        Grid.Column="1"
                        Content="AUDIO"
                        Click="OnAudioTab"
                        Padding="14,6"
                        Background="#2d2d2d"
                        Foreground="#888888"
                        FontWeight="Bold"
                        FontSize="12"
                        BorderThickness="0,0,0,3"
                        BorderBrush="Transparent"
                        CornerRadius="0">
                    <Button.Styles>
                        <Style Selector="Button:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#2a2a2a"/>
                        </Style>
                    </Button.Styles>
                </Button>

                <!-- Add Timer button — only visible on TIMERS tab -->
                <Button x:Name="AddTimerBtn"
                        Grid.Column="3"
                        Content="+"
                        Width="24" Height="24"
                        Padding="0"
                        Margin="0,0,8,0"
                        Background="#555555"
                        Foreground="White"
                        FontSize="16" FontWeight="Bold"
                        CornerRadius="4" BorderThickness="0"
                        HorizontalContentAlignment="Center"
                        VerticalContentAlignment="Center"
                        Click="OnAddTimer">
                    <Button.Styles>
                        <Style Selector="Button:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#666666"/>
                        </Style>
                    </Button.Styles>
                </Button>

            </Grid>
        </Border>

        <!-- Timer panel content (no header of its own) -->
        <views:TimerPanel x:Name="TimersContent" />

        <!-- Audio player panel (hidden by default) -->
        <views:AudioPlayerPanel x:Name="AudioContent"
                                DataContext="{Binding AudioPlayer}"
                                IsVisible="False" />

    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Create TabbedRightPanel.axaml.cs**

Create `Views/TabbedRightPanel.axaml.cs`:

```csharp
using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
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
        TimersTabBtn.Background   = timers  ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1e1e1e"))
                                            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2d2d2d"));
        TimersTabBtn.Foreground   = timers  ? Avalonia.Media.Brushes.White : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888"));
        TimersTabBtn.BorderBrush  = timers  ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e07050"))
                                            : Avalonia.Media.Brushes.Transparent;

        AudioTabBtn.Background    = !timers ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1e1e1e"))
                                            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#2d2d2d"));
        AudioTabBtn.Foreground    = !timers ? Avalonia.Media.Brushes.White : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888"));
        AudioTabBtn.BorderBrush   = !timers ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#e07050"))
                                            : Avalonia.Media.Brushes.Transparent;
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
            // Notify TimerPanel's empty hint
            (TimersContent as TimerPanel)?.RefreshEmptyHint();
        }
    }
}
```

- [ ] **Step 3: Expose RefreshEmptyHint on TimerPanel**

In `Views/TimerPanel.axaml.cs`, add a public method so `TabbedRightPanel` can trigger it:

```csharp
public void RefreshEmptyHint() => UpdateEmptyHint();
```

- [ ] **Step 4: Build**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors. (AudioPlayerPanel doesn't exist yet — add a temporary stub if needed.)

If the build fails because `AudioPlayerPanel` is unknown, create a temporary empty stub:

```csharp
// Views/AudioPlayerPanel.axaml.cs  (temp stub)
using Avalonia.Controls;
namespace ShowCast.Views;
public partial class AudioPlayerPanel : UserControl
{
    public AudioPlayerPanel() => InitializeComponent();
}
```

```xml
<!-- Views/AudioPlayerPanel.axaml (temp stub) -->
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             x:Class="ShowCast.Views.AudioPlayerPanel">
    <TextBlock Text="Audio Player (coming soon)" Foreground="#888888" Margin="8"/>
</UserControl>
```

- [ ] **Step 5: Commit**

```powershell
git add Views/TabbedRightPanel.axaml Views/TabbedRightPanel.axaml.cs Views/TimerPanel.axaml.cs
git commit -m "feat: add TabbedRightPanel with TIMERS/AUDIO tab strip"
```

---

### Task 9: Wire MainWindow

**Files:**
- Modify: `Views/MainWindow.axaml`

- [ ] **Step 1: Replace TimerPanel with TabbedRightPanel**

In `Views/MainWindow.axaml`, find:

```xml
<Border Grid.Row="2">
    <views:TimerPanel />
</Border>
```

Replace with:

```xml
<Border Grid.Row="2">
    <views:TabbedRightPanel />
</Border>
```

- [ ] **Step 2: Build and run**

```powershell
dotnet build ShowCast.csproj -c Debug
dotnet run --project ShowCast.csproj
```

Expected: App launches. Right panel shows TIMERS / AUDIO tab strip. TIMERS tab is active by default. AUDIO tab switches to the stub "coming soon" panel. The "+" add-timer button works. Timers still function.

- [ ] **Step 3: Commit**

```powershell
git add Views/MainWindow.axaml
git commit -m "feat: wire TabbedRightPanel into MainWindow"
```

---

### Task 10: AudioPlayerPanel — full UI

Replace the temporary stub with the real panel.

**Files:**
- Modify: `Views/AudioPlayerPanel.axaml`
- Modify: `Views/AudioPlayerPanel.axaml.cs`

- [ ] **Step 1: Write AudioPlayerPanel.axaml**

Replace stub `Views/AudioPlayerPanel.axaml` with:

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ShowCast.ViewModels"
             xmlns:core="using:ShowCast.Core"
             x:Class="ShowCast.Views.AudioPlayerPanel"
             x:DataType="vm:AudioPlayerViewModel">

    <DockPanel LastChildFill="True">

        <!-- ── Unavailable notice ── -->
        <Border DockPanel.Dock="Top"
                IsVisible="{Binding IsUnavailable}"
                Background="#2a2a2a" Padding="8,6">
            <TextBlock Text="Audio unavailable — VLC libraries not found"
                       Foreground="#888888" FontSize="10" TextWrapping="Wrap"/>
        </Border>

        <!-- ── Playlist bar ── -->
        <Border DockPanel.Dock="Top" Background="#1a1a1a" Padding="6,4"
                BorderBrush="#555555" BorderThickness="0,0,0,1">
            <Grid ColumnDefinitions="*,Auto,Auto">
                <ComboBox Grid.Column="0"
                          ItemsSource="{Binding Playlists}"
                          SelectedItem="{Binding SelectedPlaylist}"
                          Background="#2d2d2d"
                          Foreground="White"
                          BorderThickness="0"
                          HorizontalAlignment="Stretch"
                          MaxDropDownHeight="200">
                    <ComboBox.ItemTemplate>
                        <DataTemplate x:DataType="core:AudioPlaylist">
                            <TextBlock Text="{Binding Name}" Foreground="White"/>
                        </DataTemplate>
                    </ComboBox.ItemTemplate>
                </ComboBox>
                <Button Grid.Column="1" Content="+" Width="24" Height="24"
                        Padding="0" Margin="4,0,0,0"
                        Background="#555555" Foreground="White"
                        FontSize="14" FontWeight="Bold"
                        CornerRadius="4" BorderThickness="0"
                        HorizontalContentAlignment="Center"
                        VerticalContentAlignment="Center"
                        Click="OnNewPlaylist">
                    <Button.Styles>
                        <Style Selector="Button:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#666666"/>
                        </Style>
                    </Button.Styles>
                </Button>
                <Button Grid.Column="2" Content="✕" Width="24" Height="24"
                        Padding="0" Margin="2,0,0,0"
                        Background="#444444" Foreground="#bbbbbb"
                        FontSize="11" CornerRadius="4" BorderThickness="0"
                        HorizontalContentAlignment="Center"
                        VerticalContentAlignment="Center"
                        Click="OnDeletePlaylist">
                    <Button.Styles>
                        <Style Selector="Button:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#8b1a1a"/>
                        </Style>
                    </Button.Styles>
                </Button>
            </Grid>
        </Border>

        <!-- ── Now Playing + Seek ── -->
        <Border DockPanel.Dock="Bottom" Background="#222222"
                Padding="8,6" BorderBrush="#555555" BorderThickness="0,1,0,0">
            <StackPanel Spacing="4">
                <TextBlock Text="{Binding CurrentTrack.Title, FallbackValue='—'}"
                           FontWeight="Bold" Foreground="White" FontSize="11"
                           TextTrimming="CharacterEllipsis"/>
                <Grid ColumnDefinitions="*,Auto">
                    <Slider x:Name="SeekSlider"
                            Grid.Column="0"
                            Minimum="0"
                            Maximum="{Binding Duration.TotalSeconds}"
                            Value="{Binding PositionSeconds, Mode=OneWay}"
                            PointerReleased="OnSeekReleased"
                            Foreground="#e07050"/>
                    <TextBlock Grid.Column="1"
                               Text="{Binding PositionDisplay}"
                               Foreground="#888888" FontSize="9"
                               VerticalAlignment="Center"
                               Margin="6,0,0,0"/>
                </Grid>

                <!-- Transport row -->
                <StackPanel Orientation="Horizontal"
                            HorizontalAlignment="Center" Spacing="4">
                    <Button Content="⏮" Click="OnPrevious"   Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White" CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                    <Button Content="⏪" Click="OnSeekBack"   Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White" CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                    <Button Content="{Binding PlayPauseIcon}" Click="OnPlayPause"
                            Width="34" Height="34" Padding="0"
                            Background="#e07050" Foreground="White" CornerRadius="17" BorderThickness="0"
                            FontSize="14" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#e08060"/></Style></Button.Styles>
                    </Button>
                    <Button Content="⏩" Click="OnSeekForward" Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White" CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                    <Button Content="⏭" Click="OnNext"        Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White" CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center" VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- ── Options (Shuffle / Repeat / Speed / Volume / Resume) ── -->
        <Border DockPanel.Dock="Bottom" Background="#1e1e1e"
                Padding="8,6" BorderBrush="#555555" BorderThickness="0,1,0,0">
            <StackPanel Spacing="6">

                <!-- Shuffle + Repeat -->
                <Grid ColumnDefinitions="*,*">
                    <ToggleButton Grid.Column="0"
                                  Content="🔀 Shuffle"
                                  IsChecked="{Binding Shuffle}"
                                  Classes="tool-btn"
                                  FontSize="10" Padding="6,3"/>
                    <Button Grid.Column="1"
                            Content="{Binding RepeatLabel}"
                            Click="OnCycleRepeat"
                            Classes="tool-btn"
                            Margin="4,0,0,0"
                            FontSize="10" Padding="6,3"/>
                </Grid>

                <!-- Speed -->
                <StackPanel Orientation="Horizontal" Spacing="2">
                    <TextBlock Text="Speed:" Foreground="#888888" FontSize="9"
                               VerticalAlignment="Center" Margin="0,0,4,0"/>
                    <Button x:Name="Spd05"   Content=".5×"   Click="OnSpd05"   Classes="tool-btn" FontSize="9" Padding="4,2"/>
                    <Button x:Name="Spd075"  Content=".75×"  Click="OnSpd075"  Classes="tool-btn" FontSize="9" Padding="4,2"/>
                    <Button x:Name="Spd1"    Content="1×"    Click="OnSpd1"    Classes="tool-btn" FontSize="9" Padding="4,2"/>
                    <Button x:Name="Spd125"  Content="1.25×" Click="OnSpd125"  Classes="tool-btn" FontSize="9" Padding="4,2"/>
                    <Button x:Name="Spd15"   Content="1.5×"  Click="OnSpd15"   Classes="tool-btn" FontSize="9" Padding="4,2"/>
                    <Button x:Name="Spd2"    Content="2×"    Click="OnSpd2"    Classes="tool-btn" FontSize="9" Padding="4,2"/>
                </StackPanel>

                <!-- Auto-advance + Resume -->
                <Grid ColumnDefinitions="Auto,*,Auto,*">
                    <TextBlock Grid.Column="0" Text="Auto-adv:" Foreground="#888888"
                               FontSize="9" VerticalAlignment="Center"/>
                    <ToggleButton Grid.Column="1"
                                  IsChecked="{Binding AutoAdvance}"
                                  Classes="tool-btn"
                                  Margin="4,0"
                                  Padding="6,2" FontSize="9">
                        <ToggleButton.Content>
                            <TextBlock Text="{Binding AutoAdvance, StringFormat='{}{0:On;On;Off}'}" FontSize="9"/>
                        </ToggleButton.Content>
                    </ToggleButton>
                    <TextBlock Grid.Column="2" Text="Resume:" Foreground="#888888"
                               FontSize="9" VerticalAlignment="Center" Margin="4,0,0,0"/>
                    <StackPanel Grid.Column="3" Orientation="Horizontal" Spacing="2" Margin="4,0,0,0">
                        <Button Content="Top"  Click="OnResumeTop"  Classes="tool-btn" FontSize="9" Padding="4,2"/>
                        <Button Content="Last" Click="OnResumeLast" Classes="tool-btn" FontSize="9" Padding="4,2"/>
                    </StackPanel>
                </Grid>

                <!-- Volume -->
                <Grid ColumnDefinitions="Auto,*,Auto">
                    <TextBlock Grid.Column="0" Text="🔊" Foreground="#888888"
                               FontSize="12" VerticalAlignment="Center"/>
                    <Slider Grid.Column="1"
                            Minimum="0" Maximum="1"
                            Value="{Binding Volume}"
                            Margin="6,0"
                            Foreground="#e07050"/>
                    <TextBlock Grid.Column="2"
                               Text="{Binding VolumeDisplay}"
                               Foreground="#888888" FontSize="9"
                               VerticalAlignment="Center" Width="32"/>
                </Grid>

            </StackPanel>
        </Border>

        <!-- ── Track list (fills remaining space) ── -->
        <DockPanel LastChildFill="True">
            <Button DockPanel.Dock="Bottom"
                    Content="⊕  Import Files…"
                    Click="OnImport"
                    HorizontalAlignment="Stretch"
                    Classes="btn-secondary"
                    Margin="6,4" FontSize="10"
                    IsEnabled="{Binding SelectedPlaylist,
                                Converter={x:Static ObjectConverters.IsNotNull}}"/>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ItemsControl ItemsSource="{Binding TrackList}" Padding="6">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="core:AudioTrack">
                            <Border Classes="surface-card" Padding="6,4" Margin="0,0,0,3">
                                <Grid ColumnDefinitions="16,*,Auto,26">
                                    <!-- Playing indicator -->
                                    <TextBlock Grid.Column="0"
                                               x:Name="PlayingIcon"
                                               Text="▶" FontSize="8"
                                               Foreground="#e07050"
                                               VerticalAlignment="Center"/>
                                    <!-- Title -->
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding Title}"
                                               Foreground="White" FontSize="10"
                                               TextTrimming="CharacterEllipsis"
                                               VerticalAlignment="Center"
                                               Margin="4,0"/>
                                    <!-- Duration -->
                                    <TextBlock Grid.Column="2"
                                               Text="{Binding DurationMs,
                                                      Converter={x:Static vm:AudioPlayerViewModel.MsToTimeConverter}}"
                                               Foreground="#666666" FontSize="9"
                                               VerticalAlignment="Center"/>
                                    <!-- Delete -->
                                    <Button Grid.Column="3"
                                            Content="✕"
                                            Tag="{Binding}"
                                            Click="OnDeleteTrack"
                                            Width="22" Height="22" Padding="0"
                                            Background="#444444" Foreground="#bbbbbb"
                                            CornerRadius="3" BorderThickness="0"
                                            FontSize="9"
                                            HorizontalContentAlignment="Center"
                                            VerticalContentAlignment="Center">
                                        <Button.Styles>
                                            <Style Selector="Button:pointerover /template/ ContentPresenter">
                                                <Setter Property="Background" Value="#8b1a1a"/>
                                            </Style>
                                        </Button.Styles>
                                    </Button>
                                </Grid>
                            </Border>
                        </DataTemplate>
                    </ItemsControl.ItemTemplate>
                </ItemsControl>
            </ScrollViewer>
        </DockPanel>

    </DockPanel>
</UserControl>
```

- [ ] **Step 2: Write AudioPlayerPanel.axaml.cs**

Replace stub `Views/AudioPlayerPanel.axaml.cs` with:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class AudioPlayerPanel : UserControl
{
    AudioPlayerViewModel? VM => DataContext as AudioPlayerViewModel;

    public AudioPlayerPanel() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RefreshSpeedButtons();
        RefreshResumeButtons();
    }

    // ── Playlist management ───────────────────────────────────────────────────

    async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("New Playlist", "Playlist name:", "New Playlist");
        await dlg.ShowDialog(owner);
        if (!string.IsNullOrWhiteSpace(dlg.Result))
            VM?.CreatePlaylist(dlg.Result);
    }

    void OnDeletePlaylist(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedPlaylist is { } pl)
            VM.DeletePlaylist(pl);
    }

    // ── Track management ──────────────────────────────────────────────────────

    void OnDeleteTrack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AudioTrack track)
            VM?.DeleteTrack(track);
    }

    async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var picker = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title          = "Import Audio Files",
            AllowMultiple  = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio Files")
                {
                    Patterns = new[] {
                        "*.mp3","*.wav","*.flac","*.aac","*.m4a","*.ogg","*.opus",
                        "*.wma","*.aiff","*.aif","*.mp4","*.m4b","*.mka","*.ape",
                        "*.wv","*.tta","*.caf","*.au","*.amr","*.spx"
                    }
                },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        };

        var files = await owner.StorageProvider.OpenFilePickerAsync(picker);
        if (files.Count == 0) return;

        var paths = files.Select(f => f.TryGetLocalPath() ?? "").Where(p => p.Length > 0);
        await VM.ImportFilesAsync(paths);
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (VM.State == PlaybackState.Playing) VM.Pause();
        else VM.Play();
    }

    void OnPrevious(object? sender, RoutedEventArgs e)    => VM?.Previous();
    void OnNext(object? sender, RoutedEventArgs e)        => VM?.Next();
    void OnSeekBack(object? sender, RoutedEventArgs e)    => VM?.SeekRelative(-10);
    void OnSeekForward(object? sender, RoutedEventArgs e) => VM?.SeekRelative(10);

    void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider s) VM?.Seek(s.Value);
    }

    // ── Options ───────────────────────────────────────────────────────────────

    void OnCycleRepeat(object? sender, RoutedEventArgs e)
    {
        VM?.CycleRepeat();
    }

    void OnSpd05(object? s, RoutedEventArgs e)  => SetSpeed(0.5f);
    void OnSpd075(object? s, RoutedEventArgs e) => SetSpeed(0.75f);
    void OnSpd1(object? s, RoutedEventArgs e)   => SetSpeed(1.0f);
    void OnSpd125(object? s, RoutedEventArgs e) => SetSpeed(1.25f);
    void OnSpd15(object? s, RoutedEventArgs e)  => SetSpeed(1.5f);
    void OnSpd2(object? s, RoutedEventArgs e)   => SetSpeed(2.0f);

    void SetSpeed(float speed)
    {
        VM?.SetSpeed(speed);
        RefreshSpeedButtons();
    }

    void RefreshSpeedButtons()
    {
        float cur = VM?.Speed ?? 1.0f;
        var active   = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3b82f6"));
        var inactive = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3a3a3a"));
        if (Spd05  is not null) Spd05.Background  = cur == 0.5f  ? active : inactive;
        if (Spd075 is not null) Spd075.Background = cur == 0.75f ? active : inactive;
        if (Spd1   is not null) Spd1.Background   = cur == 1.0f  ? active : inactive;
        if (Spd125 is not null) Spd125.Background = cur == 1.25f ? active : inactive;
        if (Spd15  is not null) Spd15.Background  = cur == 1.5f  ? active : inactive;
        if (Spd2   is not null) Spd2.Background   = cur == 2.0f  ? active : inactive;
    }

    void OnResumeTop(object? sender, RoutedEventArgs e)
    {
        VM?.SetResumeMode(ResumeMode.FromTop);
        RefreshResumeButtons();
    }

    void OnResumeLast(object? sender, RoutedEventArgs e)
    {
        VM?.SetResumeMode(ResumeMode.FromLastPosition);
        RefreshResumeButtons();
    }

    void RefreshResumeButtons()
    {
        // Visual feedback handled by IsResumeFromTop / IsResumeFromLast bindings on VM
    }
}
```

- [ ] **Step 3: Add MsToTimeConverter to AudioPlayerViewModel**

In `ViewModels/AudioPlayerViewModel.cs`, add this static converter class inside the namespace (outside the VM class):

```csharp
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
```

And on `AudioPlayerViewModel` class, add a static field:

```csharp
public static readonly MsToTimeStringConverter MsToTimeConverter = new();
```

- [ ] **Step 4: Build**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Fix any binding or compilation errors. Common issues:
- `ObjectConverters.IsNotNull` — add `xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"` is already present; ensure `x:Static ObjectConverters.IsNotNull` references `Avalonia.Data.Converters.ObjectConverters`.
- `x:DataType` on the root — if compiled bindings are off globally (`AvaloniaUseCompiledBindingsByDefault=false` is set in csproj), remove `x:DataType` attributes.

- [ ] **Step 5: Run the app and verify**

```powershell
dotnet run --project ShowCast.csproj
```

Verify:
- AUDIO tab shows the full player UI
- Playlist ComboBox shows "New Playlist" option after clicking "+"
- Import Files button opens a file picker
- Transport buttons are visible and respond to clicks (no playback yet without VLC DLLs)

- [ ] **Step 6: Commit**

```powershell
git add Views/AudioPlayerPanel.axaml Views/AudioPlayerPanel.axaml.cs ViewModels/AudioPlayerViewModel.cs
git commit -m "feat: implement AudioPlayerPanel UI with full transport controls and options"
```

---

### Task 11: Position persistence and resume logic

**Files:**
- Modify: `ViewModels/AudioPlayerViewModel.cs`

- [ ] **Step 1: Save last position on track change**

In `AudioPlayerViewModel`, update the `CurrentTrack` setter to save position before switching:

```csharp
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
        this.RaiseAndSetIfChanged(ref _currentTrack, value);
    }
}
```

- [ ] **Step 2: Add PersistLastPosition helper called on save**

Add a public method that `MainViewModel.SaveSessionAsync` can call:

```csharp
/// <summary>Called by MainViewModel before saving to persist the current playback position.</summary>
public void PersistPlaybackState()
{
    if (_selectedPlaylist is null || _currentTrack is null) return;
    _selectedPlaylist.LastTrackId    = _currentTrack.Id;
    _selectedPlaylist.LastPositionMs = _player is not null ? Math.Max(0, _player.Time) : 0;
}
```

- [ ] **Step 3: Call PersistPlaybackState in MainViewModel.SaveSessionAsync**

In `ViewModels/MainViewModel.cs`, at the start of `SaveSessionAsync()`, add:

```csharp
AudioPlayer.PersistPlaybackState();
```

- [ ] **Step 4: Apply resume logic when SelectPlaylist changes**

In `AudioPlayerViewModel`, update `SelectedPlaylist` setter to apply resume logic after `RefreshTrackList()`:

```csharp
public AudioPlaylist? SelectedPlaylist
{
    get => _selectedPlaylist;
    set
    {
        Stop(); // stop any playing track from previous playlist
        this.RaiseAndSetIfChanged(ref _selectedPlaylist, value);
        RefreshTrackList();
        RaisePlaylistPassthroughs();
        ApplyResume(value);
    }
}

void ApplyResume(AudioPlaylist? playlist)
{
    if (playlist is null || playlist.ResumeMode == ResumeMode.FromTop) return;

    // FromLastPosition: find the saved track and seek to saved position
    var track = playlist.Tracks.FirstOrDefault(t => t.Id == playlist.LastTrackId);
    if (track is null)
    {
        // Saved track no longer exists — fall back to top
        return;
    }

    // Pre-set state so the UI shows the cued track without auto-playing
    CurrentTrack      = track;
    CurrentTrackIndex = playlist.Tracks.IndexOf(track);
    // Do NOT call Play() — resume is cued only; user still presses play
}
```

- [ ] **Step 5: Build**

```powershell
dotnet build ShowCast.csproj -c Debug
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 6: Run all tests**

```powershell
dotnet test ShowCast.Tests -v normal
```

Expected: all tests pass.

- [ ] **Step 7: Run app and verify end-to-end**

```powershell
dotnet run --project ShowCast.csproj
```

Verify:
1. Switch to AUDIO tab — playlist area appears
2. Create a playlist — ComboBox updates
3. Import audio files — track list populates with titles and durations
4. Click play on a track — playback starts, seek bar advances
5. ⏪/⏩ seek ±10s
6. ⏮/⏭ skip tracks
7. Shuffle, Repeat, Speed, Volume all respond
8. Close and reopen — playlists and tracks persist (saved in session file)
9. `FromLastPosition` playlist: close while playing at 1:30, reopen — player is cued to that track

- [ ] **Step 8: Final commit**

```powershell
git add ViewModels/AudioPlayerViewModel.cs ViewModels/MainViewModel.cs
git commit -m "feat: add playback position persistence and playlist resume logic"
```

---

## Self-Review

**Spec coverage:**
- ✅ Tab strip replacing TimerPanel header → Task 8/9
- ✅ Multiple named playlists → Task 4
- ✅ Import audio files (all known containers) → Task 5, 10
- ✅ Files copied to media folder → Task 5 (`AppFolders.Media`)
- ✅ Saved in show file → Task 3, 6
- ✅ Full transport: play/pause/stop/prev/next/seek±10s → Task 5, 10
- ✅ Seek bar (scrub) → Task 10
- ✅ Shuffle, Repeat (None/One/All), Speed, Volume → Task 4, 5, 10
- ✅ Auto-advance with toggle → Task 4, 5
- ✅ Resume From Top / From Last Position → Task 4, 11
- ✅ LastTrackId + LastPositionMs persisted → Task 11
- ✅ LibVLC unavailable: `IsUnavailable` banner → Task 4, 10
- ✅ Missing file: status message + skip if AutoAdvance → Task 5
- ✅ Duplicate filenames on import: numeric suffix → Task 5
- ✅ Empty playlist, no-op on play → Task 5
- ✅ Delete playlist while playing: stop first → Task 4
- ✅ `FromLastPosition` but track deleted: fall back → Task 11
- ✅ Session restore: SelectedAudioPlaylistId → Task 3, 6

**Note on spec delta:** The spec said files go to `<showfile-dir>/media/`. Since the show file is always auto-saved to `AppFolders.SessionFile` (a fixed path), `AppFolders.Media` (`Documents/ShowCast/Media/`) is used instead — it's already provisioned at startup and achieves the same portability goal. `AudioTrack.RelativePath` stores just the filename, resolved against `AppFolders.Media`.

**Type consistency check:** `PickNextIndex` signature is `public static int PickNextIndex(AudioPlaylist, int)` — matches usage in Task 5 `Next()`. `SeekRelative(double)` defined in Task 5, called in Task 10. `MsToTimeConverter` static field added in Task 10 step 3, referenced in AXAML same task. ✅
