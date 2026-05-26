# Audio Channel Routing — Design Spec

**Date:** 2026-05-26
**Branch:** master (new branch at implementation time)
**Status:** Approved
**Next phase:** NDI audio embedding, multi-destination per channel, ASIO, per-destination volume, metering

---

## Overview

Add a multi-channel audio routing system to ShowCast. Users create named audio channels (e.g. "Main PA", "Stage Monitors", "Lobby"), each backed by its own independent `MediaPlayer`. A full-width routing matrix in **Settings → Audio** wires each channel to a named destination (hardware output or NDI). Multiple channels play simultaneously, enabling true zone routing.

This replaces the single `AudioPlayerViewModel` on `MainViewModel` with a collection of `AudioChannelViewModel` objects. The existing Audio panel gains a channel tab strip. All existing playlists migrate seamlessly to a "Default" channel.

---

## 1. Data Model

### New file: `Core/AudioChannel.cs`

```csharp
public enum AudioRouteType { Hardware, Ndi }

/// <summary>
/// A named, user-configurable audio output destination.
/// Stored in AppSettings (machine-specific) so routing survives show file changes.
/// </summary>
public class AudioDestination
{
    public Guid           Id          { get; set; } = Guid.NewGuid();
    /// <summary>User-editable display name. Defaults to SystemName on first discovery.</summary>
    public string         DisplayName { get; set; } = "";
    public AudioRouteType Type        { get; set; }
    /// <summary>WASAPI device ID (hardware) or NDI stream name.</summary>
    public string         DeviceId    { get; set; } = "";
    /// <summary>System-assigned name. Auto-updated on Refresh; read-only in UI.</summary>
    public string         SystemName  { get; set; } = "";
}

/// <summary>
/// A named audio channel, each backed by its own MediaPlayer.
/// Stored in AppSettings (machine-specific).
/// </summary>
public class AudioChannel
{
    public Guid   Id                { get; set; } = Guid.NewGuid();
    public string Name              { get; set; } = "New Channel";
    /// <summary>Id of the AudioDestination this channel routes to. Null = OS default device.</summary>
    public Guid?  ActiveDestinationId { get; set; }
    /// <summary>Last selected playlist on this channel (restored on load).</summary>
    public Guid   SelectedPlaylistId  { get; set; } = Guid.Empty;
}
```

### Modified: `Core/AppSettings.cs`

Add two new collections:
```csharp
public List<AudioChannel>     AudioChannels     { get; set; } = new();
public List<AudioDestination> AudioDestinations { get; set; } = new();
```

Remove `SelectedAudioPlaylistId` (moved into `AudioChannel.SelectedPlaylistId`).

### Modified: `Core/AudioPlaylist.cs`

Add one property:
```csharp
/// <summary>Channel this playlist belongs to. Guid.Empty = Default channel.</summary>
public Guid ChannelId { get; set; } = Guid.Empty;
```

### Serialization

All new properties use STJ automatic serialization. Old `AppSettings` files missing `AudioChannels`/`AudioDestinations` deserialize to empty lists — the Default channel is seeded at runtime. Old `AudioPlaylist` objects missing `ChannelId` get `Guid.Empty` and load into the Default channel.

---

## 2. ViewModel Layer

### New file: `ViewModels/AudioChannelViewModel.cs`

```csharp
public class AudioChannelViewModel : ReactiveObject
{
    public AudioChannel          Model  { get; }
    public AudioPlayerViewModel  Player { get; }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; this.RaisePropertyChanged(); }
    }

    public AudioChannelViewModel(AudioChannel model)
    {
        Model  = model;
        Player = new AudioPlayerViewModel();
    }

    /// <summary>
    /// Configures the Player's MediaPlayer to output to the assigned destination.
    /// Called after routing changes and before playback starts.
    /// </summary>
    public void ApplyRoute(AudioDestination? destination)
    {
        if (destination is null) return;
        if (destination.Type == AudioRouteType.Hardware)
            Player.SetAudioDevice("wasapi", destination.DeviceId);
        // NDI: no-op in V1 (requires video integration — next phase)
    }
}
```

### Modified: `ViewModels/AudioPlayerViewModel.cs`

Add one new public method:
```csharp
/// <summary>Sets the audio output device for the next Play() call.</summary>
public void SetAudioDevice(string outputModule, string deviceId)
{
    _pendingOutputModule = outputModule;
    _pendingDeviceId     = deviceId;
}
```

In `Play(AudioTrack? track = null)`, before `_player.Play()`, apply any pending device:
```csharp
if (_pendingDeviceId is not null)
{
    _player.SetAudioOutputDevice(_pendingOutputModule!, _pendingDeviceId);
    _pendingDeviceId     = null;
    _pendingOutputModule = null;
}
```

### Modified: `ViewModels/MainViewModel.cs`

Replace:
```csharp
public AudioPlayerViewModel AudioPlayer { get; } = new();
```

With:
```csharp
public ObservableCollection<AudioChannelViewModel> AudioChannels { get; } = new();

AudioChannelViewModel? _selectedAudioChannel;
public AudioChannelViewModel? SelectedAudioChannel
{
    get => _selectedAudioChannel;
    set => this.RaiseAndSetIfChanged(ref _selectedAudioChannel, value);
}
```

**All existing `AudioPlayer.*` references** are replaced with `SelectedAudioChannel?.Player.*`.

**Seeding:** In `SeedDemoContent()`, create one Default channel and add it to `AudioChannels`. Set `SelectedAudioChannel` to it.

**Loading:** In the file load path, restore `AudioChannels` from `AppSettings`. For each channel, call `LoadPlaylists()` on its `Player` with the filtered subset of `ShowFile.AudioPlaylists` where `ChannelId == channel.Id`. Playlists with `ChannelId == Guid.Empty` load into the Default channel.

**Saving:** Persist `AudioChannels` to `AppSettings`. Persist all playlists (across all channels) to `ShowFile.AudioPlaylists`.

**`FirePageAudioTrigger` updated:**
```csharp
void FirePageAudioTrigger(Page page)
{
    if (page.TriggerAudioPlaylistId == Guid.Empty) return;

    // Find the playlist and its owning channel
    var playlist = ShowFile.AudioPlaylists
        .FirstOrDefault(p => p.Id == page.TriggerAudioPlaylistId);
    if (playlist is null) return;

    var channelVm = playlist.ChannelId == Guid.Empty
        ? AudioChannels.FirstOrDefault()
        : AudioChannels.FirstOrDefault(c => c.Model.Id == playlist.ChannelId);
    if (channelVm is null) return;

    var pl = channelVm.Player.Playlists
        .FirstOrDefault(p => p.Id == page.TriggerAudioPlaylistId);
    if (pl is null) return;

    channelVm.Player.SelectedPlaylist = pl;

    if (page.TriggerAudioTrackId != Guid.Empty)
    {
        var track = pl.Tracks.FirstOrDefault(t => t.Id == page.TriggerAudioTrackId);
        if (track is not null) { channelVm.Player.Play(track); return; }
    }

    channelVm.Player.Play();
}
```

---

## 3. Audio Panel — Channel Tab Strip

**File:** `Views/AudioPlayerPanel.axaml` + `Views/AudioPlayerPanel.axaml.cs`

Add a tab strip as the first child of the `DockPanel`, docked to `Top`:

```
┌──────────────────────────────────────────┐
│ Main PA │ Stage Monitors │ Lobby │  +    │  ← new tab strip
├──────────────────────────────────────────┤
│ [Sunday Worship ▼]  [+]  [✕]            │  ← existing playlist bar
│                                          │
│  ▶ Amazing Grace              3:24  [✕] │
│    How Great Thou Art         4:11  [✕] │
│    In Christ Alone            3:58  [✕] │
│                                          │
│  [Import Files...]                       │
│ ─────────────────────────────────────── │
│ ▶ Amazing Grace                          │
│ ══════════════●══════════  1:12 / 3:24   │
│        ⏮  ⏪  ⏸  ⏩  ⏭                   │
└──────────────────────────────────────────┘
```

- Each tab bound to an `AudioChannelViewModel` in `MainViewModel.AudioChannels`
- Selected tab = `MainViewModel.SelectedAudioChannel`
- Active tab underlined with `#e07050`
- `+` tab creates a new channel (prompts for name via inline edit or small dialog — same pattern as `OnNewPlaylist`)
- Right-click a tab → Rename / Remove Channel
- Removing a channel with playlists prompts: "Move playlists to Default?" or "Delete playlists too?"
- The rest of the panel binds to `SelectedAudioChannel.Player` (all existing bindings unchanged in structure)

**`DataContext` wiring:** `AudioPlayerPanel` currently binds to `MainViewModel.AudioPlayer`. Change to bind to `MainViewModel.SelectedAudioChannel.Player`. The panel's AXAML `x:DataType` changes from `AudioPlayerViewModel` to `AudioPlayerViewModel` (same type — just the source changes in the parent binding).

---

## 4. Settings → Audio Dialog

### Entry point

`Views/MainWindow.axaml` — add menu item:
```xml
<MenuItem Header="Audio" Click="OnAudioSettings"/>
```
Between "Screens" and "Schedule".

`Views/MainWindow.axaml.cs`:
```csharp
async void OnAudioSettings(object? sender, RoutedEventArgs e)
{
    if (VM is null) return;
    var dialog = new AudioSettingsDialog(VM);
    await dialog.ShowDialog(this);
}
```

### Dialog: `Views/AudioSettingsDialog.axaml`

Size: 800 × 520. `CanResize="False"`. Same dark theme as `ScreenConfigDialog`.

**Layout:**

```
┌─────────────────────────────────────────────────────────┐
│  AUDIO ROUTING                               [Refresh ↺] │
│─────────────────────────────────────────────────────────│
│  [+ Channel]  [Rename]  [Remove]                         │
│─────────────────────────────────────────────────────────│
│             ║  HARDWARE                 ║  NDI           │
│             ║  Front of  │  Focusrite   ║  ShowCast │ L  │
│             ║  House     │  2i2         ║  Main     │ ob │
│─────────────║────────────┼─────────────║───────────┼────│
│  Main PA    ║     ◉      │      ○       ║     ○     │  ○ │
│  St.Monitors║     ○      │      ◉       ║     ○     │  ○ │
│  Lobby      ║     ○      │      ○       ║     ○     │  ◉ │
│─────────────────────────────────────────────────────────│
│                                              [Close]     │
└─────────────────────────────────────────────────────────┘
```

**Matrix behavior:**
- Cells are radio-button style per row — clicking a cell activates it and clears any other in that row
- Clicking an active cell clears it (no route — plays on OS default)
- NDI cells are visible but **disabled** with a tooltip: *"NDI audio routing available in next phase"*
- Offline/disconnected hardware destinations are shown greyed out but not removed

**Column header rename:**
- Double-click a column header → header becomes an inline `TextBox`
- Press Enter or click away → commits new `DisplayName` to `AudioDestination`
- `SystemName` (raw device name) shown below in smaller grey text, never editable

**Refresh Devices button (↺):**
- Re-enumerates hardware via `libVlc.AudioOutputDeviceEnum("wasapi")`
- Adds newly discovered devices as new `AudioDestination` entries (DisplayName = SystemName)
- Updates `SystemName` on existing destinations matched by `DeviceId`
- Greyed-out destinations whose `DeviceId` is no longer present remain in the list
- NDI destinations are re-read from `ShowFile.Outputs` where `Type == OutputType.NDI`

**Channel management toolbar:**
- **+ Channel** → adds a new `AudioChannelViewModel` with a placeholder name ("New Channel"), then immediately enters rename mode on that row label
- **Rename** → enters inline edit on the selected channel's row label
- **Remove** → confirms and removes; offers to migrate orphaned playlists to Default

### `ViewModels/AudioSettingsViewModel.cs` (new)

Holds the UI state for the dialog:
- `ObservableCollection<AudioDestination> Destinations` (from AppSettings + freshly enumerated)
- Exposes `MainViewModel.AudioChannels` for matrix rows
- `RefreshDevices()` — calls `AudioDeviceEnumerator`
- `SetRoute(AudioChannelViewModel channel, AudioDestination? dest)` — sets `channel.Model.ActiveDestinationId = dest?.Id`, calls `channel.ApplyRoute(dest)`. Passing `null` clears the route (plays on OS default).
- Changes are **live** (no Apply button) and persisted to `AppSettings` immediately

---

## 5. Device Enumeration

### New file: `Core/AudioDeviceEnumerator.cs`

```csharp
public static class AudioDeviceEnumerator
{
    public static IReadOnlyList<AudioDestination> EnumerateHardware(LibVLC libVlc)
    {
        return libVlc.AudioOutputDeviceEnum("wasapi")
            .Select(d => new AudioDestination
            {
                DeviceId    = d.DeviceIdentifier,
                SystemName  = d.Description,
                DisplayName = d.Description,
                Type        = AudioRouteType.Hardware
            })
            .ToList();
    }

    public static IReadOnlyList<AudioDestination> EnumerateNdi(ShowFile showFile)
    {
        return showFile.Outputs
            .Where(o => o.Type == OutputType.NDI)
            .Select(o => new AudioDestination
            {
                DeviceId    = o.NdiStreamName,
                SystemName  = o.Name,
                DisplayName = o.Name,
                Type        = AudioRouteType.Ndi
            })
            .ToList();
    }
}
```

Called by `AudioSettingsViewModel.RefreshDevices()` and once on dialog open.

---

## 6. Out of Scope (next phase)

These are intentionally deferred and saved for the next feature:

| Item | Reason deferred |
|------|-----------------|
| NDI audio embedding | Requires video player integration (NDI audio carried in video stream) |
| Multi-destination per channel | Requires multiple MediaPlayers per channel + sync |
| ASIO / low-latency drivers | Separate output module from WASAPI |
| Per-destination volume control | Requires per-player volume management |
| Audio metering in matrix | Requires level monitoring infrastructure |
| SDI audio destinations | Requires capture card SDK integration |

---

## 7. Files Changed

| File | Action | Notes |
|------|--------|-------|
| `Core/AudioChannel.cs` | **Create** | `AudioChannel`, `AudioDestination`, `AudioRouteType` |
| `Core/AudioDeviceEnumerator.cs` | **Create** | Hardware + NDI enumeration |
| `Core/AppSettings.cs` | Modify | Add `AudioChannels`, `AudioDestinations`; remove `SelectedAudioPlaylistId` |
| `Core/AudioPlaylist.cs` | Modify | Add `ChannelId` property |
| `ViewModels/AudioChannelViewModel.cs` | **Create** | Wraps `AudioPlayerViewModel` + routing |
| `ViewModels/AudioSettingsViewModel.cs` | **Create** | Dialog state for matrix + refresh |
| `ViewModels/AudioPlayerViewModel.cs` | Modify | Add `SetAudioDevice()`, apply pending device in `Play()` |
| `ViewModels/MainViewModel.cs` | Modify | Replace `AudioPlayer` with `AudioChannels` collection; update all audio references; update `FirePageAudioTrigger` |
| `Views/AudioPlayerPanel.axaml` | Modify | Add channel tab strip at top |
| `Views/AudioPlayerPanel.axaml.cs` | Modify | Tab strip event handlers |
| `Views/AudioSettingsDialog.axaml` | **Create** | Routing matrix dialog |
| `Views/AudioSettingsDialog.axaml.cs` | **Create** | Dialog code-behind |
| `Views/MainWindow.axaml` | Modify | Add Settings → Audio menu item |
| `Views/MainWindow.axaml.cs` | Modify | `OnAudioSettings` handler |
| `ShowCast.Tests/...` | Modify/Create | Channel model tests, routing tests, serialization tests |
