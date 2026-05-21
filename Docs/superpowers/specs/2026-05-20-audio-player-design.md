# Audio Player Feature — Design Spec
**Date:** 2026-05-20  
**Branch:** Media-Player  
**Status:** Approved

---

## Overview

Add a multi-playlist audio player to ShowCast's right panel. The "TIMERS" panel header is replaced with a two-tab strip (TIMERS / AUDIO). The audio player supports multiple named playlists saved inside the show file, full transport controls, shuffle/repeat/speed/volume, auto-advance, and resume-from-last-position. Audio files are copied into a `media/` folder next to the show file on import.

---

## Architecture

### Approach

A new `TabbedRightPanel` UserControl replaces the bare `<views:TimerPanel />` in `MainWindow.axaml`. It owns a custom tab strip and shows/hides the two child panels. Both panels remain in the visual tree at all times so timers keep running when the Audio tab is active.

### New Files

| File | Purpose |
|---|---|
| `Core/AudioTrack.cs` | Model: single audio track |
| `Core/AudioPlaylist.cs` | Model: playlist + playback settings |
| `ViewModels/AudioPlayerViewModel.cs` | All playback state, playlist management, file import |
| `Views/TabbedRightPanel.axaml` + `.cs` | Custom tab strip hosting TimerPanel & AudioPlayerPanel |
| `Views/AudioPlayerPanel.axaml` + `.cs` | Full audio player UI |

### Modified Files

| File | Change |
|---|---|
| `Views/MainWindow.axaml` | Replace `<views:TimerPanel />` with `<views:TabbedRightPanel />` |
| `Core/ShowFile.cs` | Add `List<AudioPlaylist> AudioPlaylists` |
| `Core/AppSettings.cs` | Add `Guid SelectedAudioPlaylistId` |
| `Core/ShowFileSerializer.cs` | Serialize new fields; version bump + migration (empty list for old files) |
| `ViewModels/MainViewModel.cs` | Instantiate `AudioPlayerViewModel`; wire save/load |
| `ShowCast.csproj` | Add `LibVLCSharp` + `VideoLAN.LibVLC.Windows` NuGet packages |

### Audio Backend

**LibVLCSharp** (`LibVLCSharp` + `VideoLAN.LibVLC.Windows` NuGet packages).  
Chosen for maximum format coverage: MP3, WAV, FLAC, AAC, M4A, OGG, OPUS, WMA, AIFF, APE, MKA, ALAC, CAF, AU, AMR, SPX, and any other container VLC supports.

One `LibVLC` instance and one `MediaPlayer` instance owned by `AudioPlayerViewModel`, disposed on shutdown.

---

## Data Models

### `AudioTrack`

```csharp
public class AudioTrack
{
    public Guid   Id           { get; set; } = Guid.NewGuid();
    public string Title        { get; set; } = "";
    public string RelativePath { get; set; } = ""; // e.g. "media/song.mp3"
    public long   DurationMs   { get; set; }
}
```

### `RepeatMode` / `ResumeMode`

```csharp
public enum RepeatMode  { None, One, All }
public enum ResumeMode  { FromTop, FromLastPosition }
```

### `AudioPlaylist`

```csharp
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

### `ShowFile` addition

```csharp
public List<AudioPlaylist> AudioPlaylists { get; set; } = new();
```

### `AppSettings` addition

```csharp
public Guid SelectedAudioPlaylistId { get; set; }
```

---

## File Storage

- On import, files are copied to `<showfile-dir>/media/<original-filename>`.
- Duplicate filenames get a numeric suffix: `song(1).mp3`, `song(2).mp3`, etc.
- `AudioTrack.RelativePath` stores `media/<filename>` — resolved against the show file's directory at load and playback time.
- Deleting a track removes it from the playlist model only; the copied file is **not** deleted (avoids accidental data loss).
- If the show file has not been saved yet, import is blocked with an alert: *"Please save your show before importing audio files."*

### Supported Import Extensions (file picker filter)

`.mp3`, `.wav`, `.flac`, `.aac`, `.m4a`, `.ogg`, `.opus`, `.wma`, `.aiff`, `.aif`, `.mp4`, `.m4b`, `.mka`, `.ape`, `.wv`, `.tta`, `.caf`, `.au`, `.amr`, `.spx`

---

## ViewModel — `AudioPlayerViewModel`

### State

```
Playlists:          ObservableCollection<AudioPlaylist>
SelectedPlaylist:   AudioPlaylist?
CurrentTrack:       AudioTrack?
CurrentTrackIndex:  int

State:              PlaybackState { Stopped, Playing, Paused }
Position:           TimeSpan        // updated every ~250ms
Duration:           TimeSpan
PositionSeconds:    double          // for slider binding

Volume:             double (0.0–1.0)
IsMuted:            bool
Speed:              float (0.5 | 0.75 | 1.0 | 1.25 | 1.5 | 2.0)
IsShuffleOn:        bool
RepeatMode:         RepeatMode
AutoAdvance:        bool
```

### Methods

| Method | Description |
|---|---|
| `Play(AudioTrack? track = null)` | Play specific track or resume current |
| `Pause()` | Pause playback |
| `Stop()` | Stop and reset position |
| `Next()` | Advance to next track (respects Shuffle, Repeat, AutoAdvance) |
| `Previous()` | Go to previous track or restart current if >3s in |
| `Seek(double seconds)` | Seek to position (bound to scrub slider) |
| `CreatePlaylist(string name)` | Add playlist, select it |
| `DeletePlaylist(AudioPlaylist)` | Stop if playing from it, then remove |
| `RenamePlaylist(AudioPlaylist, string name)` | Rename in place |
| `ImportFilesAsync(string[] paths, string showFilePath)` | Copy files, read duration via LibVLC, add tracks |
| `DeleteTrack(AudioTrack)` | Stop if current, remove from playlist |
| `Dispose()` | Release LibVLC resources |

### Position Persistence

`LastTrackId` and `LastPositionMs` are written to the playlist model:
- On every track change
- On app close / show save (debounced — not on every 250ms tick)

### LibVLC Integration

- `MediaPlayer.EndReached` → calls `Next()` on the UI thread
- Position polling via `System.Timers.Timer` at 250ms (same pattern as existing timer system)
- `MediaPlayer.LengthChanged` → updates `Duration`

### `MainViewModel` Integration

```csharp
public AudioPlayerViewModel AudioPlayer { get; } = new();
```

- `SaveSessionAsync`: writes `AudioPlayer.SelectedPlaylist?.Id` → `AppSettings.SelectedAudioPlaylistId`
- `LoadSessionAsync`: restores selected playlist by ID after loading show file
- `RebuildFromShowFile`: calls `AudioPlayer.LoadPlaylists(showFile.AudioPlaylists)`

---

## UI Layout

### `TabbedRightPanel`

Custom tab strip using two `Button` controls styled to match the existing dark theme:

- **Inactive tab:** background `#2d2d2d`, foreground `#888888`
- **Active tab:** background `#1e1e1e`, foreground `White`, `#e07050` bottom accent line (3px)

Both `TimerPanel` and `AudioPlayerPanel` are always in the visual tree; `IsVisible` toggles on tab switch.

```
┌────────────────────────────────┐
│  [TIMERS]  [AUDIO]             │
│────────────────────────────────│
│  <TimerPanel />                │
│  OR                            │
│  <AudioPlayerPanel />          │
└────────────────────────────────┘
```

### `AudioPlayerPanel`

Stacked vertically in a `DockPanel`, styled with the existing dark theme palette:

```
┌─ Playlist bar ───────────────────────────────────┐
│  ▾ Sunday Service        [+ New]  [✕ Delete]     │
├─ Track list (ScrollViewer, fills space) ──────────┤
│  ▶  01  Opening Song             3:42   [✕]      │
│     02  Worship Set              5:15   [✕]      │
│     03  Offertory                2:30   [✕]      │
│                          [⊕ Import Files…]       │
├─ Now Playing ─────────────────────────────────────┤
│  Opening Song                                     │
│  ░░░░████████░░░░░░░░░░  1:23 / 3:42             │
├─ Transport ───────────────────────────────────────┤
│      [⏮]  [⏪]  [⏯]  [⏩]  [⏭]                   │
├─ Options ─────────────────────────────────────────┤
│  [🔀 Shuffle]  [🔁 None | One | All]             │
│  Speed: [.5] [.75] [1×] [1.25] [1.5] [2×]       │
│  Auto-advance: [toggle]                           │
│  Resume:  [From Top]  [Last Position]             │
│  🔊 ─────●───────────────── 80%                  │
└──────────────────────────────────────────────────┘
```

**Transport buttons:** `⏮` = previous track, `⏪` = seek back 10s, `⏯` = play/pause, `⏩` = seek forward 10s, `⏭` = next track.

**Track list:** `ItemsControl` with `surface-card` styled rows (matches timer cards). Playing track highlighted. Track title + duration displayed; delete button on each row.

**Seek bar:** Avalonia `Slider`, bound to `PositionSeconds` with `Maximum` bound to `Duration.TotalSeconds`.

**Speed:** Row of `ToggleButton` with `tool-btn` style.

**Repeat:** Cycles through `None → One → All → None` on click; single button shows current state.

**Resume:** Two `ToggleButton` controls acting as a radio group bound to `SelectedPlaylist.ResumeMode`.

---

## Error Handling & Edge Cases

| Scenario | Behavior |
|---|---|
| Show not saved, user clicks Import | Alert: *"Please save your show before importing audio files."* |
| Duplicate filename on import | Auto-rename: `song(1).mp3`, `song(2).mp3`, etc. |
| Track file missing at load time | Track shown with ⚠ icon, dimmed, disabled for playback |
| Track file missing on Play | Status message: *"File not found: media/song.mp3"* — skip if AutoAdvance on |
| LibVLC decode failure | Same as missing file — show error, skip if AutoAdvance on |
| LibVLC native DLLs absent | Audio tab hidden or shows *"Audio playback unavailable"* |
| Delete playlist while playing | Stop playback, then remove playlist |
| Delete currently-playing track | Stop playback, remove track, clear Now Playing |
| Empty playlist, user hits Play | No-op |
| Shuffle + Repeat All | Shuffle order reseeded each full pass |
| End of playlist, AutoAdvance on, Repeat None | Stop — do not loop |
| Show file moved to different directory | Relative paths resolve correctly (`media/` moves with show file) |
| `FromLastPosition` but saved track deleted | Silently fall back to track 1, position 0 |

---

## Serialization

- `AudioPlaylist` and `AudioTrack` are plain data models — serialize naturally via the existing `System.Text.Json` pipeline.
- Show file version bumped; migration: old files receive `AudioPlaylists = []`.
- `RepeatMode`, `ResumeMode` serialized as strings (existing pattern for enums in this codebase).
