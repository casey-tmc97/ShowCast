# Audio Channel Routing Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a multi-channel audio routing system — named channels each with their own MediaPlayer, a channel tab strip in the Audio panel, and a Settings → Audio dialog with a routing matrix that wires channels to hardware output devices.

**Architecture:** `AudioChannel` and `AudioDestination` models are added to `AppSettings` (serialized in ShowFile). Each channel is wrapped by `AudioChannelViewModel` which owns an `AudioPlayerViewModel`. `MainViewModel.AudioChannels` replaces the single `AudioPlayer`. The Audio panel's DataContext changes from `AudioPlayerViewModel` to `MainViewModel`; all existing bindings gain a `SelectedAudioChannel.Player.` prefix, and a channel tab strip is added at the top. `AudioSettingsDialog` builds its routing matrix programmatically in code-behind. Device enumeration uses LibVLC's `AudioOutputDeviceEnum("wasapi")`.

**Tech Stack:** C# / .NET 9, Avalonia UI, ReactiveUI, LibVLCSharp (`MediaPlayer.SetAudioOutputDevice`, `LibVLC.AudioOutputDeviceEnum`), STJ (auto-serialization with empty-list defaults), xUnit

**Natural split:** Tasks 1–5 produce working multi-channel audio (routing to OS default). Tasks 6–7 add the hardware routing dialog. Tasks 1–5 can be shipped independently if needed.

---

## File Map

| File | Action | Task |
|------|--------|------|
| `Core/AudioChannel.cs` | **Create** | 1 |
| `Core/AudioPlaylist.cs` | Modify — add `ChannelId` | 1 |
| `Core/AppSettings.cs` | Modify — add `AudioChannels`, `AudioDestinations`; remove `SelectedAudioPlaylistId` | 1 |
| `ShowCast.Tests/Core/AudioChannelTests.cs` | **Create** | 1 |
| `ViewModels/AudioChannelViewModel.cs` | **Create** | 2 |
| `ViewModels/AudioPlayerViewModel.cs` | Modify — add `SetAudioDevice`, apply in `Play()` | 2 |
| `ShowCast.Tests/ViewModels/AudioChannelViewModelTests.cs` | **Create** | 2 |
| `ViewModels/MainViewModel.cs` | Modify — replace `AudioPlayer` with collection | 3 |
| `ShowCast.Tests/ViewModels/MainViewModelTests.cs` | Modify — update for multi-channel | 3 |
| `Views/AudioPlayerPanel.axaml` | Modify — DataContext → MainViewModel, add tab strip | 4 |
| `Views/AudioPlayerPanel.axaml.cs` | Modify — update VM reference, add tab handlers | 4 |
| `Views/TabbedRightPanel.axaml` | Modify — remove explicit `DataContext` override | 4 |
| `Core/AudioDeviceEnumerator.cs` | **Create** | 5 |
| `ShowCast.Tests/Core/AudioDeviceEnumeratorTests.cs` | **Create** | 5 |
| `ViewModels/AudioSettingsViewModel.cs` | **Create** | 6 |
| `ShowCast.Tests/ViewModels/AudioSettingsViewModelTests.cs` | **Create** | 6 |
| `Views/AudioSettingsDialog.axaml` | **Create** | 7 |
| `Views/AudioSettingsDialog.axaml.cs` | **Create** | 7 |
| `Views/MainWindow.axaml` | Modify — add Settings → Audio menu item | 7 |
| `Views/MainWindow.axaml.cs` | Modify — add `OnAudioSettings` handler | 7 |

---

## Task 1: Core Data Models

**Files:**
- Create: `Core/AudioChannel.cs`
- Modify: `Core/AudioPlaylist.cs`
- Modify: `Core/AppSettings.cs`
- Create: `ShowCast.Tests/Core/AudioChannelTests.cs`

- [ ] **Step 1.1: Write failing tests**

Create `ShowCast.Tests/Core/AudioChannelTests.cs`:

```csharp
using System;
using System.Text.Json;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class AudioChannelTests
{
    // ── AudioChannel defaults ─────────────────────────────────────────────────

    [Fact]
    public void AudioChannel_DefaultValues_AreCorrect()
    {
        var ch = new AudioChannel();
        Assert.NotEqual(Guid.Empty, ch.Id);
        Assert.Equal("New Channel", ch.Name);
        Assert.Null(ch.ActiveDestinationId);
        Assert.Equal(Guid.Empty, ch.SelectedPlaylistId);
    }

    // ── AudioDestination defaults ─────────────────────────────────────────────

    [Fact]
    public void AudioDestination_DefaultValues_AreCorrect()
    {
        var dest = new AudioDestination();
        Assert.NotEqual(Guid.Empty, dest.Id);
        Assert.Equal("", dest.DisplayName);
        Assert.Equal("", dest.DeviceId);
        Assert.Equal("", dest.SystemName);
        Assert.Equal(AudioRouteType.Hardware, dest.Type);
    }

    // ── AudioPlaylist.ChannelId ───────────────────────────────────────────────

    [Fact]
    public void AudioPlaylist_ChannelId_DefaultsToEmpty()
    {
        var pl = new AudioPlaylist();
        Assert.Equal(Guid.Empty, pl.ChannelId);
    }

    [Fact]
    public void AudioPlaylist_ChannelId_SurvivesShowFileRoundTrip()
    {
        var channelId = Guid.NewGuid();
        var file = new ShowFile();
        var pl = new AudioPlaylist { Name = "Test", ChannelId = channelId };
        file.AudioPlaylists.Add(pl);

        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var json   = JsonSerializer.Serialize(file, opts);
        var loaded = JsonSerializer.Deserialize<ShowFile>(json, opts)!;

        Assert.Equal(channelId, loaded.AudioPlaylists[0].ChannelId);
    }

    // ── AppSettings collections ───────────────────────────────────────────────

    [Fact]
    public void AppSettings_AudioChannels_DeserializesToEmptyList_WhenMissing()
    {
        var json = """{"Version":1}""";
        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var file   = JsonSerializer.Deserialize<ShowFile>(json, opts)!;
        Assert.NotNull(file.Settings.AudioChannels);
        Assert.Empty(file.Settings.AudioChannels);
    }

    [Fact]
    public void AppSettings_AudioDestinations_DeserializesToEmptyList_WhenMissing()
    {
        var json = """{"Version":1}""";
        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var file   = JsonSerializer.Deserialize<ShowFile>(json, opts)!;
        Assert.NotNull(file.Settings.AudioDestinations);
        Assert.Empty(file.Settings.AudioDestinations);
    }

    [Fact]
    public void AppSettings_AudioChannelsAndDestinations_SurviveRoundTrip()
    {
        var file = new ShowFile();
        var dest = new AudioDestination
        {
            DisplayName = "Front of House",
            DeviceId    = "hw-001",
            SystemName  = "Realtek",
            Type        = AudioRouteType.Hardware
        };
        file.Settings.AudioDestinations.Add(dest);
        var ch = new AudioChannel { Name = "Main PA", ActiveDestinationId = dest.Id };
        file.Settings.AudioChannels.Add(ch);

        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var json   = JsonSerializer.Serialize(file, opts);
        var loaded = JsonSerializer.Deserialize<ShowFile>(json, opts)!;

        Assert.Single(loaded.Settings.AudioChannels);
        Assert.Single(loaded.Settings.AudioDestinations);
        Assert.Equal("Main PA", loaded.Settings.AudioChannels[0].Name);
        Assert.Equal(dest.Id, loaded.Settings.AudioChannels[0].ActiveDestinationId);
        Assert.Equal("Front of House", loaded.Settings.AudioDestinations[0].DisplayName);
        Assert.Equal("hw-001", loaded.Settings.AudioDestinations[0].DeviceId);
    }
}
```

- [ ] **Step 1.2: Run tests — verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioChannelTests" -v m
```

Expected: compilation errors — `AudioChannel`, `AudioDestination`, `AudioRouteType`, `ChannelId` not defined yet.

- [ ] **Step 1.3: Create `Core/AudioChannel.cs`**

```csharp
namespace ShowCast.Core;

public enum AudioRouteType { Hardware, Ndi }

/// <summary>
/// A named, user-configurable audio output destination (hardware device or NDI).
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
    /// <summary>System-assigned name. Auto-updated on Refresh; never user-editable.</summary>
    public string         SystemName  { get; set; } = "";
}

/// <summary>
/// A named audio channel, each backed by its own MediaPlayer.
/// Stored in AppSettings (machine-specific).
/// </summary>
public class AudioChannel
{
    public Guid   Id                  { get; set; } = Guid.NewGuid();
    public string Name                { get; set; } = "New Channel";
    /// <summary>Id of the AudioDestination this channel routes to. Null = OS default device.</summary>
    public Guid?  ActiveDestinationId { get; set; }
    /// <summary>Last selected playlist on this channel (restored on load).</summary>
    public Guid   SelectedPlaylistId  { get; set; } = Guid.Empty;
}
```

- [ ] **Step 1.4: Add `ChannelId` to `Core/AudioPlaylist.cs`**

Open `Core/AudioPlaylist.cs`. After the `LastPositionMs` property, add:

```csharp
    /// <summary>Channel this playlist belongs to. Guid.Empty = legacy/Default channel.</summary>
    public Guid ChannelId { get; set; } = Guid.Empty;
```

Full file after change:

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
    /// <summary>Channel this playlist belongs to. Guid.Empty = legacy/Default channel.</summary>
    public Guid             ChannelId      { get; set; } = Guid.Empty;
}
```

- [ ] **Step 1.5: Modify `Core/AppSettings.cs`**

Replace the entire file:

```csharp
namespace ShowCast.Core;

public class AppSettings
{
    // Editor display toggles
    public bool   ShowGrid           { get; set; } = true;
    public bool   ShowRulers         { get; set; } = true;
    public int    GridSpacing        { get; set; } = 100;
    public bool   SnapToGrid         { get; set; } = false;
    public bool   ShowSafeBoundaries { get; set; } = false;

    // Page grid view
    public double ThumbSize { get; set; } = 160;
    public string ViewMode  { get; set; } = "Grid";

    // Window geometry (null = use OS default)
    public double WindowWidth     { get; set; } = 1600;
    public double WindowHeight    { get; set; } = 900;
    public int?   WindowX         { get; set; } = null;
    public int?   WindowY         { get; set; } = null;
    public bool   WindowMaximized { get; set; } = false;

    // Panel splitter positions
    public double LeftPanelWidth     { get; set; } = 200;
    public double RightPanelWidth    { get; set; } = 300;
    public double LeftTopStarWeight  { get; set; } = 1;
    public double LeftMidStarWeight  { get; set; } = 1;
    public double LeftBotStarWeight  { get; set; } = 1;
    public double RightTopStarWeight { get; set; } = 1;
    public double RightBotStarWeight { get; set; } = 1;

    // Transition bar state
    public string NextTransitionType     { get; set; } = "Cut";
    public int    NextTransitionDuration { get; set; } = 0;

    // Last-selected items (restored by ID)
    public Guid SelectedOutputId      { get; set; } = Guid.Empty;
    public Guid SelectedShowId        { get; set; } = Guid.Empty;
    public Guid SelectedRundownId     { get; set; } = Guid.Empty;
    public Guid SelectedPackageItemId { get; set; } = Guid.Empty;
    // NOTE: SelectedAudioPlaylistId removed — now stored per-channel in AudioChannel.SelectedPlaylistId

    // Audio routing
    public List<AudioChannel>     AudioChannels     { get; set; } = new();
    public List<AudioDestination> AudioDestinations { get; set; } = new();
}
```

- [ ] **Step 1.6: Run tests — verify they pass**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioChannelTests" -v m
```

Expected: All 7 tests PASS.

- [ ] **Step 1.7: Build the whole solution to catch any compile errors from `SelectedAudioPlaylistId` removal**

```
dotnet build ShowCast.sln
```

Expected: Build errors on any remaining references to `SelectedAudioPlaylistId`. Fix each one:
- `ViewModels/MainViewModel.cs` — will be fully replaced in Task 3; for now just comment it out or use `Guid.Empty` as placeholder.

- [ ] **Step 1.8: Commit**

```
git add Core/AudioChannel.cs Core/AudioPlaylist.cs Core/AppSettings.cs ShowCast.Tests/Core/AudioChannelTests.cs
git commit -m "feat(audio-routing): add AudioChannel, AudioDestination models; ChannelId on AudioPlaylist"
```

---

## Task 2: AudioChannelViewModel + AudioPlayerViewModel.SetAudioDevice

**Files:**
- Create: `ViewModels/AudioChannelViewModel.cs`
- Modify: `ViewModels/AudioPlayerViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/AudioChannelViewModelTests.cs`

- [ ] **Step 2.1: Write failing tests**

Create `ShowCast.Tests/ViewModels/AudioChannelViewModelTests.cs`:

```csharp
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class AudioChannelViewModelTests
{
    [Fact]
    public void AudioChannelViewModel_Player_IsNotNull()
    {
        var model = new AudioChannel { Name = "Main PA" };
        var vm = new AudioChannelViewModel(model);
        Assert.NotNull(vm.Player);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_Name_ReflectsModel()
    {
        var model = new AudioChannel { Name = "Lobby" };
        var vm = new AudioChannelViewModel(model);
        Assert.Equal("Lobby", vm.Name);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_SetName_UpdatesModel()
    {
        var model = new AudioChannel { Name = "Old" };
        var vm = new AudioChannelViewModel(model);
        vm.Name = "New";
        Assert.Equal("New", model.Name);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_Dispose_DoesNotThrow()
    {
        var model = new AudioChannel();
        var vm = new AudioChannelViewModel(model);
        var ex = Record.Exception(() => vm.Dispose());
        Assert.Null(ex);
    }

    [Fact]
    public void AudioChannelViewModel_ApplyRoute_Null_DoesNotThrow()
    {
        var model = new AudioChannel();
        var vm = new AudioChannelViewModel(model);
        var ex = Record.Exception(() => vm.ApplyRoute(null));
        Assert.Null(ex);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_ApplyRoute_Hardware_DoesNotThrow()
    {
        var model = new AudioChannel();
        var vm = new AudioChannelViewModel(model);
        var dest = new AudioDestination
        {
            Type     = AudioRouteType.Hardware,
            DeviceId = "hw-001"
        };
        var ex = Record.Exception(() => vm.ApplyRoute(dest));
        Assert.Null(ex);
        vm.Dispose();
    }
}
```

- [ ] **Step 2.2: Run tests — verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioChannelViewModelTests" -v m
```

Expected: compilation errors — `AudioChannelViewModel` not defined yet.

- [ ] **Step 2.3: Create `ViewModels/AudioChannelViewModel.cs`**

```csharp
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class AudioChannelViewModel : ReactiveObject, IDisposable
{
    public AudioChannel         Model  { get; }
    public AudioPlayerViewModel Player { get; }

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
    /// Configures the Player to output to the assigned destination on next Play() call.
    /// </summary>
    public void ApplyRoute(AudioDestination? destination)
    {
        if (destination is null) return;
        if (destination.Type == AudioRouteType.Hardware)
            Player.SetAudioDevice("wasapi", destination.DeviceId);
        // NDI: no-op in V1 (requires video integration — next phase)
    }

    public void Dispose() => Player.Dispose();
}
```

- [ ] **Step 2.4: Add `SetAudioDevice` to `ViewModels/AudioPlayerViewModel.cs`**

Find the field declarations section near the top of the class (around line 20–22 where `LibVLC? _libVlc` and `MediaPlayer? _player` are declared). Add two new fields immediately after:

```csharp
    string? _pendingOutputModule;
    string? _pendingDeviceId;
```

After the existing `Stop()` method (or near any other public playback method), add the new public method:

```csharp
    /// <summary>Sets the audio output device for the next Play() call.</summary>
    public void SetAudioDevice(string outputModule, string deviceId)
    {
        _pendingOutputModule = outputModule;
        _pendingDeviceId     = deviceId;
    }
```

In `Play(AudioTrack? track = null)`, find the line `_player.Play();` (around line 368). Insert the pending-device block immediately before it:

```csharp
        if (_pendingDeviceId is not null)
        {
            _player.SetAudioOutputDevice(_pendingOutputModule!, _pendingDeviceId);
            _pendingDeviceId     = null;
            _pendingOutputModule = null;
        }
        _player.Play();
```

- [ ] **Step 2.5: Run tests — verify they pass**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioChannelViewModelTests" -v m
```

Expected: All 6 tests PASS.

- [ ] **Step 2.6: Run full test suite to confirm no regressions**

```
dotnet test ShowCast.Tests -v m
```

Expected: All existing tests still PASS.

- [ ] **Step 2.7: Commit**

```
git add ViewModels/AudioChannelViewModel.cs ViewModels/AudioPlayerViewModel.cs ShowCast.Tests/ViewModels/AudioChannelViewModelTests.cs
git commit -m "feat(audio-routing): AudioChannelViewModel with routing; SetAudioDevice on AudioPlayerViewModel"
```

---

## Task 3: MainViewModel — Replace AudioPlayer with AudioChannels

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `ShowCast.Tests/ViewModels/MainViewModelTests.cs`

The `AudioPlayer` property (line 24) is replaced by `AudioChannels` + `SelectedAudioChannel`. `SaveSessionAsync`, `RebuildFromShowFile`, `SeedDemoContent`, and `FirePageAudioTrigger` are all updated. Two new public methods `AddAudioChannel` and `RemoveAudioChannel` are added.

- [ ] **Step 3.1: Write failing tests**

Replace the entire contents of `ShowCast.Tests/ViewModels/MainViewModelTests.cs`:

```csharp
using System;
using System.Reflection;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelTests
{
    static void InvokeFirePageAudioTrigger(MainViewModel vm, Page page)
    {
        var method = typeof(MainViewModel).GetMethod("FirePageAudioTrigger",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("FirePageAudioTrigger not found");
        method.Invoke(vm, new object[] { page });
    }

    // ── Channel seeding ───────────────────────────────────────────────────────

    [Fact]
    public void MainViewModel_Init_SeedsOneDefaultChannel()
    {
        var vm = new MainViewModel();
        Assert.NotEmpty(vm.AudioChannels);
        Assert.NotNull(vm.SelectedAudioChannel);
    }

    [Fact]
    public void MainViewModel_Init_DefaultChannelHasAtLeastOnePlaylist()
    {
        var vm = new MainViewModel();
        Assert.NotEmpty(vm.SelectedAudioChannel!.Player.Playlists);
    }

    // ── AddAudioChannel / RemoveAudioChannel ──────────────────────────────────

    [Fact]
    public void AddAudioChannel_AddsChannelAndSelectsIt()
    {
        var vm = new MainViewModel();
        var before = vm.AudioChannels.Count;
        vm.AddAudioChannel("Stage Monitors");
        Assert.Equal(before + 1, vm.AudioChannels.Count);
        Assert.Equal("Stage Monitors", vm.SelectedAudioChannel!.Name);
    }

    [Fact]
    public void RemoveAudioChannel_RemovesAndSelectsFirst()
    {
        var vm = new MainViewModel();
        vm.AddAudioChannel("Stage Monitors");
        var toRemove = vm.AudioChannels[1];
        vm.RemoveAudioChannel(toRemove);
        Assert.DoesNotContain(toRemove, vm.AudioChannels);
        Assert.Equal(vm.AudioChannels[0], vm.SelectedAudioChannel);
    }

    [Fact]
    public void RemoveAudioChannel_CannotRemoveLastChannel()
    {
        var vm = new MainViewModel();
        Assert.Single(vm.AudioChannels);
        vm.RemoveAudioChannel(vm.AudioChannels[0]);
        Assert.Single(vm.AudioChannels); // still there
    }

    // ── FirePageAudioTrigger (multi-channel) ──────────────────────────────────

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistIdEmpty_DoesNotChangeSelectedPlaylist()
    {
        var vm             = new MainViewModel();
        var player         = vm.SelectedAudioChannel!.Player;
        var originalPlaylist = player.SelectedPlaylist;
        InvokeFirePageAudioTrigger(vm, new Page { TriggerAudioPlaylistId = Guid.Empty });
        Assert.Equal(originalPlaylist, player.SelectedPlaylist);
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistIdMatchesDefaultChannel_SetsSelectedPlaylist()
    {
        var vm     = new MainViewModel();
        var player = vm.SelectedAudioChannel!.Player;
        player.CreatePlaylist("Worship");
        var target = player.Playlists[^1];

        InvokeFirePageAudioTrigger(vm, new Page { TriggerAudioPlaylistId = target.Id });

        Assert.Equal(target, player.SelectedPlaylist);
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistNotFound_DoesNotChangeSelectedPlaylist()
    {
        var vm             = new MainViewModel();
        var player         = vm.SelectedAudioChannel!.Player;
        var originalPlaylist = player.SelectedPlaylist;

        InvokeFirePageAudioTrigger(vm, new Page { TriggerAudioPlaylistId = Guid.NewGuid() });

        Assert.Equal(originalPlaylist, player.SelectedPlaylist);
    }
}
```

- [ ] **Step 3.2: Run tests — verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~MainViewModelTests" -v m
```

Expected: FAIL — `AudioChannels` and `SelectedAudioChannel` not defined on `MainViewModel`.

- [ ] **Step 3.3: Replace `AudioPlayer` with channel collection in `ViewModels/MainViewModel.cs`**

**3.3a — Replace the `AudioPlayer` property (line 24):**

Find:
```csharp
    public AudioPlayerViewModel AudioPlayer { get; } = new();
```

Replace with:
```csharp
    public System.Collections.ObjectModel.ObservableCollection<AudioChannelViewModel> AudioChannels { get; } = new();

    AudioChannelViewModel? _selectedAudioChannel;
    public AudioChannelViewModel? SelectedAudioChannel
    {
        get => _selectedAudioChannel;
        set => this.RaiseAndSetIfChanged(ref _selectedAudioChannel, value);
    }
```

**3.3b — Update `SaveSessionAsync` (around line 48–54):**

Find:
```csharp
        s.SelectedAudioPlaylistId  = AudioPlayer.SelectedPlaylist?.Id ?? Guid.Empty;
        AudioPlayer.PersistPlaybackState();

        // Sync AudioPlayer playlists back to the ShowFile model before saving
        _showFile.AudioPlaylists.Clear();
        foreach (var pl in AudioPlayer.Playlists)
            _showFile.AudioPlaylists.Add(pl);
```

Replace with:
```csharp
        // Persist each channel's playback state and selected playlist
        foreach (var ch in AudioChannels)
        {
            ch.Model.SelectedPlaylistId = ch.Player.SelectedPlaylist?.Id ?? Guid.Empty;
            ch.Player.PersistPlaybackState();
        }

        // Sync all channel playlists back to ShowFile before saving
        _showFile.AudioPlaylists.Clear();
        foreach (var ch in AudioChannels)
            foreach (var pl in ch.Player.Playlists)
                _showFile.AudioPlaylists.Add(pl);
```

**3.3c — Update `RebuildFromShowFile` (around line 174–180):**

Find:
```csharp
        AudioPlayer.LoadPlaylists(
            _showFile.AudioPlaylists,
            _showFile.Settings.SelectedAudioPlaylistId);

        // Always ensure at least one playlist exists (migrating from older saves)
        if (AudioPlayer.Playlists.Count == 0)
            AudioPlayer.CreatePlaylist("Default");
```

Replace with:
```csharp
        // Dispose existing channels before rebuilding
        foreach (var ch in AudioChannels) ch.Dispose();
        AudioChannels.Clear();
        _selectedAudioChannel = null; this.RaisePropertyChanged(nameof(SelectedAudioChannel));

        // Legacy files: AudioChannels list is empty → seed a Default channel
        if (_showFile.Settings.AudioChannels.Count == 0)
        {
            var defaultModel = new ShowCast.Core.AudioChannel { Name = "Default" };
            _showFile.Settings.AudioChannels.Add(defaultModel);
        }

        foreach (var channelModel in _showFile.Settings.AudioChannels)
        {
            var channelVm = new AudioChannelViewModel(channelModel);
            bool isFirst  = channelModel == _showFile.Settings.AudioChannels[0];

            // Playlists with matching ChannelId, or Guid.Empty (legacy) into the first channel
            var channelPlaylists = _showFile.AudioPlaylists
                .Where(p => p.ChannelId == channelModel.Id ||
                            (isFirst && p.ChannelId == Guid.Empty))
                .ToList();

            // Normalise ChannelId so future saves persist correctly
            foreach (var pl in channelPlaylists)
                pl.ChannelId = channelModel.Id;

            if (channelPlaylists.Count == 0)
                channelVm.Player.CreatePlaylist("Default");
            else
                channelVm.Player.LoadPlaylists(channelPlaylists, channelModel.SelectedPlaylistId);

            // Restore routing
            if (channelModel.ActiveDestinationId.HasValue)
            {
                var dest = _showFile.Settings.AudioDestinations
                    .FirstOrDefault(d => d.Id == channelModel.ActiveDestinationId);
                channelVm.ApplyRoute(dest);
            }

            AudioChannels.Add(channelVm);
        }

        SelectedAudioChannel = AudioChannels.Count > 0 ? AudioChannels[0] : null;
```

**3.3d — Update `SeedDemoContent` (line 1926):**

Find:
```csharp
        // ── Default audio playlist ────────────────────────────────────────────
        AudioPlayer.CreatePlaylist("Default");
```

Replace with:
```csharp
        // ── Default audio channel + playlist ─────────────────────────────────
        var defaultChannelModel = new ShowCast.Core.AudioChannel { Name = "Default" };
        _showFile.Settings.AudioChannels.Add(defaultChannelModel);
        var defaultChannelVm = new AudioChannelViewModel(defaultChannelModel);
        defaultChannelVm.Player.CreatePlaylist("Default");
        AudioChannels.Add(defaultChannelVm);
        SelectedAudioChannel = defaultChannelVm;
```

**3.3e — Replace `FirePageAudioTrigger` method:**

Find the entire existing `FirePageAudioTrigger` method and replace it:

```csharp
    void FirePageAudioTrigger(Page page)
    {
        if (page.TriggerAudioPlaylistId == Guid.Empty) return;

        // Find the playlist and its owning channel
        var playlist = _showFile.AudioPlaylists
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

**3.3f — Add `AddAudioChannel` and `RemoveAudioChannel` public methods:**

Add these two methods near the audio section (e.g., just before or after `FirePageAudioTrigger`):

```csharp
    public void AddAudioChannel(string name)
    {
        var model = new ShowCast.Core.AudioChannel { Name = name };
        _showFile.Settings.AudioChannels.Add(model);
        var channelVm = new AudioChannelViewModel(model);
        channelVm.Player.CreatePlaylist("Default");
        AudioChannels.Add(channelVm);
        SelectedAudioChannel = channelVm;
    }

    public void RemoveAudioChannel(AudioChannelViewModel ch)
    {
        if (AudioChannels.Count <= 1) return; // Cannot remove last channel

        // Move playlists to the first remaining channel
        var target = AudioChannels.First(c => c != ch);
        foreach (var pl in ch.Player.Playlists.ToList())
        {
            pl.ChannelId = target.Model.Id;
            target.Player.Playlists.Add(pl);
        }

        _showFile.Settings.AudioChannels.Remove(ch.Model);
        AudioChannels.Remove(ch);
        if (SelectedAudioChannel == ch)
            SelectedAudioChannel = target;
        ch.Dispose();
    }
```

- [ ] **Step 3.4: Fix the RestoreSettings method**

Find in `RestoreSettings`:
```csharp
        if (s.SelectedPackageItemId != Guid.Empty)
        {
            int idx = PackageItems.IndexOf(PackageItems.FirstOrDefault(p => p.Id == s.SelectedPackageItemId)!);
            if (idx >= 0) SelectedPackageItemIndex = idx;
        }
```

There is no longer a `SelectedAudioPlaylistId` reference in `RestoreSettings` — it was already removed from `AppSettings`. Verify the method compiles cleanly. No change needed here.

- [ ] **Step 3.5: Build and verify there are no remaining `AudioPlayer` references**

```
dotnet build ShowCast.sln 2>&1 | grep -i "audioPlayer\|SelectedAudioPlaylistId"
```

If any references remain, fix them by replacing with `SelectedAudioChannel?.Player` (for method calls) or `AudioChannels` (for collection usage).

- [ ] **Step 3.6: Run tests**

```
dotnet test ShowCast.Tests -v m
```

Expected: All tests PASS including the new MainViewModelTests.

- [ ] **Step 3.7: Commit**

```
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MainViewModelTests.cs
git commit -m "feat(audio-routing): replace AudioPlayer with AudioChannels collection in MainViewModel"
```

---

## Task 4: Audio Panel — Channel Tab Strip

**Files:**
- Modify: `Views/AudioPlayerPanel.axaml`
- Modify: `Views/AudioPlayerPanel.axaml.cs`
- Modify: `Views/TabbedRightPanel.axaml`

The panel's `DataContext` changes from `AudioPlayerViewModel` to `MainViewModel`. All body bindings gain a `SelectedAudioChannel.Player.` prefix. A channel tab strip is added as the first child of the `DockPanel`.

No new tests needed — this is a pure UI change. Manual verification in Step 4.5.

- [ ] **Step 4.1: Update `Views/TabbedRightPanel.axaml`**

Find:
```xml
        <!-- Audio player panel (hidden by default) -->
        <views:AudioPlayerPanel x:Name="AudioContent"
                                DataContext="{Binding AudioPlayer}"
                                IsVisible="False" />
```

Replace with:
```xml
        <!-- Audio player panel — DataContext inherits MainViewModel from parent -->
        <views:AudioPlayerPanel x:Name="AudioContent"
                                IsVisible="False" />
```

- [ ] **Step 4.2: Replace `Views/AudioPlayerPanel.axaml` entirely**

Replace the entire file with the following (adds the channel tab strip at the top and updates all body bindings to prefix `SelectedAudioChannel.Player.`):

```xml
<UserControl xmlns="https://github.com/avaloniaui"
             xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
             xmlns:vm="using:ShowCast.ViewModels"
             xmlns:core="using:ShowCast.Core"
             x:Class="ShowCast.Views.AudioPlayerPanel"
             x:DataType="vm:MainViewModel"
             x:Name="Root">

    <DockPanel LastChildFill="True">

        <!-- ── Channel tab strip ── -->
        <Border DockPanel.Dock="Top" Background="#1a1a1a"
                BorderBrush="#444444" BorderThickness="0,0,0,1">
            <DockPanel>
                <!-- + (Add Channel) button -->
                <Button DockPanel.Dock="Right"
                        Content="+"
                        Width="28" Height="28"
                        Padding="0"
                        Background="Transparent"
                        Foreground="#555555"
                        FontSize="16" FontWeight="Bold"
                        BorderThickness="0"
                        VerticalAlignment="Center"
                        Margin="0,0,4,0"
                        Click="OnAddChannel"
                        ToolTip.Tip="Add Channel">
                    <Button.Styles>
                        <Style Selector="Button:pointerover">
                            <Setter Property="Foreground" Value="White"/>
                        </Style>
                    </Button.Styles>
                </Button>
                <!-- Channel tabs (ListBox for built-in selection binding) -->
                <ListBox x:Name="ChannelTabList"
                         ItemsSource="{Binding AudioChannels}"
                         SelectedItem="{Binding SelectedAudioChannel}"
                         Background="Transparent"
                         BorderThickness="0"
                         Padding="0">
                    <ListBox.ItemsPanel>
                        <ItemsPanelTemplate>
                            <StackPanel Orientation="Horizontal" Spacing="0"/>
                        </ItemsPanelTemplate>
                    </ListBox.ItemsPanel>
                    <ListBox.ItemTemplate>
                        <DataTemplate x:DataType="vm:AudioChannelViewModel">
                            <TextBlock Text="{Binding Name}"
                                       Padding="12,6"
                                       FontSize="10"/>
                        </DataTemplate>
                    </ListBox.ItemTemplate>
                    <ListBox.Styles>
                        <Style Selector="ListBoxItem">
                            <Setter Property="Padding" Value="0"/>
                            <Setter Property="Background" Value="Transparent"/>
                            <Setter Property="Foreground" Value="#888888"/>
                            <Setter Property="BorderThickness" Value="0,0,0,2"/>
                            <Setter Property="BorderBrush" Value="Transparent"/>
                            <Setter Property="CornerRadius" Value="0"/>
                            <Setter Property="Cursor" Value="Hand"/>
                        </Style>
                        <Style Selector="ListBoxItem:selected /template/ ContentPresenter">
                            <Setter Property="Background" Value="#2d2d2d"/>
                        </Style>
                        <Style Selector="ListBoxItem:selected">
                            <Setter Property="BorderBrush" Value="#e07050"/>
                            <Setter Property="Foreground" Value="White"/>
                        </Style>
                        <Style Selector="ListBoxItem:pointerover /template/ ContentPresenter">
                            <Setter Property="Background" Value="#252525"/>
                        </Style>
                    </ListBox.Styles>
                    <ListBox.ContextMenu>
                        <ContextMenu>
                            <MenuItem Header="Rename Channel..." Click="OnRenameChannel"/>
                            <Separator/>
                            <MenuItem Header="Remove Channel..." Click="OnRemoveChannel"/>
                        </ContextMenu>
                    </ListBox.ContextMenu>
                </ListBox>
            </DockPanel>
        </Border>

        <!-- ── Unavailable notice ── -->
        <Border DockPanel.Dock="Top"
                IsVisible="{Binding SelectedAudioChannel.Player.IsUnavailable}"
                Background="#2a2a2a" Padding="8,6">
            <TextBlock Text="Audio unavailable — VLC libraries not found"
                       Foreground="#888888" FontSize="10" TextWrapping="Wrap"/>
        </Border>

        <!-- ── Playlist bar ── -->
        <Border DockPanel.Dock="Top" Background="#1a1a1a" Padding="6,4"
                BorderBrush="#555555" BorderThickness="0,0,0,1">
            <Border.ContextMenu>
                <ContextMenu>
                    <MenuItem Header="Playlist Properties" IsEnabled="False"
                              FontWeight="Bold" Foreground="#888888"/>
                    <Separator/>
                    <MenuItem Header="Auto-advance"
                              IsChecked="{Binding SelectedAudioChannel.Player.AutoAdvance}"/>
                    <Separator/>
                    <MenuItem Header="Resume from Top"
                              IsChecked="{Binding SelectedAudioChannel.Player.IsResumeFromTop, Mode=OneWay}"
                              Click="OnResumeTop"/>
                    <MenuItem Header="Resume from Last Position"
                              IsChecked="{Binding SelectedAudioChannel.Player.IsResumeFromLast, Mode=OneWay}"
                              Click="OnResumeLast"/>
                </ContextMenu>
            </Border.ContextMenu>
            <Grid ColumnDefinitions="*,Auto,Auto">
                <ComboBox Grid.Column="0"
                          ItemsSource="{Binding SelectedAudioChannel.Player.Playlists}"
                          SelectedItem="{Binding SelectedAudioChannel.Player.SelectedPlaylist}"
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
                <TextBlock Text="{Binding SelectedAudioChannel.Player.CurrentTrack.Title, FallbackValue='—'}"
                           FontWeight="Bold" Foreground="White" FontSize="11"
                           TextTrimming="CharacterEllipsis"/>
                <Grid ColumnDefinitions="*,Auto">
                    <Slider x:Name="SeekSlider"
                            Grid.Column="0"
                            Minimum="0"
                            Maximum="{Binding SelectedAudioChannel.Player.Duration.TotalSeconds}"
                            Value="{Binding SelectedAudioChannel.Player.PositionSeconds, Mode=OneWay}"
                            PointerReleased="OnSeekReleased"
                            Foreground="#e07050"/>
                    <TextBlock Grid.Column="1"
                               Text="{Binding SelectedAudioChannel.Player.PositionDisplay}"
                               Foreground="#888888" FontSize="9"
                               VerticalAlignment="Center"
                               Margin="6,0,0,0"/>
                </Grid>

                <!-- Transport row -->
                <StackPanel Orientation="Horizontal"
                            HorizontalAlignment="Center" Spacing="4">
                    <Button Content="⏮" Click="OnPrevious"
                            Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White"
                            CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                    <Button Content="⏪" Click="OnSeekBack"
                            Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White"
                            CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                    <Button Content="{Binding SelectedAudioChannel.Player.PlayPauseIcon}" Click="OnPlayPause"
                            Width="34" Height="34" Padding="0"
                            Background="#e07050" Foreground="White"
                            CornerRadius="17" BorderThickness="0"
                            FontSize="14" HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#e08060"/></Style></Button.Styles>
                    </Button>
                    <Button Content="⏩" Click="OnSeekForward"
                            Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White"
                            CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                    <Button Content="⏭" Click="OnNext"
                            Width="28" Height="28" Padding="0"
                            Background="#444444" Foreground="White"
                            CornerRadius="4" BorderThickness="0"
                            FontSize="13" HorizontalContentAlignment="Center"
                            VerticalContentAlignment="Center">
                        <Button.Styles><Style Selector="Button:pointerover /template/ ContentPresenter"><Setter Property="Background" Value="#555555"/></Style></Button.Styles>
                    </Button>
                </StackPanel>
            </StackPanel>
        </Border>

        <!-- ── Track list ── -->
        <DockPanel LastChildFill="True">
            <Button DockPanel.Dock="Bottom"
                    Content="Import Files..."
                    Click="OnImport"
                    HorizontalAlignment="Stretch"
                    Margin="6,4" FontSize="10"
                    IsEnabled="{Binding SelectedAudioChannel.Player.SelectedPlaylist,
                                Converter={x:Static ObjectConverters.IsNotNull}}"/>
            <ScrollViewer VerticalScrollBarVisibility="Auto">
                <ItemsControl ItemsSource="{Binding SelectedAudioChannel.Player.TrackList}" Padding="6">
                    <ItemsControl.ItemTemplate>
                        <DataTemplate x:DataType="vm:AudioTrackRow">
                            <Border Padding="6,4" Margin="0,0,0,3"
                                    Background="{Binding RowBackground}"
                                    CornerRadius="3"
                                    PointerPressed="OnTrackPointerPressed">
                                <Border.ContextMenu>
                                    <!-- DataContext overridden to ViewModel so Volume/VolumeDisplay bind correctly -->
                                    <ContextMenu DataContext="{Binding DataContext, ElementName=Root}">
                                        <MenuItem Header="Clip Properties" IsEnabled="False"
                                                  FontWeight="Bold" Foreground="#888888"/>
                                        <Separator/>
                                        <MenuItem Header="Speed">
                                            <MenuItem Header=".5x"   Click="OnSpd05"/>
                                            <MenuItem Header=".75x"  Click="OnSpd075"/>
                                            <MenuItem Header="1x"    Click="OnSpd1"/>
                                            <MenuItem Header="1.25x" Click="OnSpd125"/>
                                            <MenuItem Header="1.5x"  Click="OnSpd15"/>
                                            <MenuItem Header="2x"    Click="OnSpd2"/>
                                        </MenuItem>
                                        <Separator/>
                                        <MenuItem>
                                            <MenuItem.Header>
                                                <StackPanel Orientation="Horizontal"
                                                            Width="190" Margin="0,3">
                                                    <TextBlock Text="🔊" Foreground="#888888"
                                                               FontSize="12"
                                                               VerticalAlignment="Center"
                                                               Margin="0,0,6,0"/>
                                                    <Slider Minimum="0" Maximum="1" Width="110"
                                                            Value="{Binding SelectedAudioChannel.Player.Volume}"
                                                            Foreground="#e07050"
                                                            VerticalAlignment="Center"/>
                                                    <TextBlock Text="{Binding SelectedAudioChannel.Player.VolumeDisplay}"
                                                               Foreground="#888888" FontSize="9"
                                                               VerticalAlignment="Center"
                                                               Margin="6,0,0,0" Width="32"/>
                                                </StackPanel>
                                            </MenuItem.Header>
                                        </MenuItem>
                                    </ContextMenu>
                                </Border.ContextMenu>
                                <Grid ColumnDefinitions="*,Auto,26">
                                    <TextBlock Grid.Column="0"
                                               Text="{Binding Title}"
                                               Foreground="White" FontSize="10"
                                               TextTrimming="CharacterEllipsis"
                                               VerticalAlignment="Center"/>
                                    <TextBlock Grid.Column="1"
                                               Text="{Binding DurationMs,
                                                      Converter={x:Static vm:AudioPlayerViewModel.MsToTimeConverter}}"
                                               Foreground="#888888" FontSize="9"
                                               VerticalAlignment="Center"
                                               Margin="6,0"/>
                                    <Button Grid.Column="2"
                                            Content="✕"
                                            Tag="{Binding Track}"
                                            Click="OnDeleteTrack"
                                            Width="22" Height="22" Padding="0"
                                            Background="Transparent" Foreground="#666666"
                                            CornerRadius="3" BorderThickness="0"
                                            FontSize="9"
                                            HorizontalContentAlignment="Center"
                                            VerticalContentAlignment="Center">
                                        <Button.Styles>
                                            <Style Selector="Button:pointerover /template/ ContentPresenter">
                                                <Setter Property="Background" Value="#8b1a1a"/>
                                            </Style>
                                            <Style Selector="Button:pointerover">
                                                <Setter Property="Foreground" Value="White"/>
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

- [ ] **Step 4.3: Replace `Views/AudioPlayerPanel.axaml.cs` entirely**

```csharp
using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShowCast.Core;
using ShowCast.ViewModels;
using AudioTrack = ShowCast.Core.AudioTrack;

namespace ShowCast.Views;

public partial class AudioPlayerPanel : UserControl
{
    MainViewModel?        VM     => DataContext as MainViewModel;
    AudioPlayerViewModel? Player => VM?.SelectedAudioChannel?.Player;

    public AudioPlayerPanel() => InitializeComponent();

    // ── Channel tab strip ─────────────────────────────────────────────────────

    async void OnAddChannel(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("New Audio Channel", "Channel name:", "New Channel");
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            VM.AddAudioChannel(result.Trim());
    }

    async void OnRenameChannel(object? sender, RoutedEventArgs e)
    {
        if (ChannelTabList.SelectedItem is not AudioChannelViewModel ch) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("Rename Channel", "Channel name:", ch.Name);
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            ch.Name = result.Trim();
    }

    void OnRemoveChannel(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (ChannelTabList.SelectedItem is not AudioChannelViewModel ch) return;
        VM.RemoveAudioChannel(ch);
    }

    // ── Playlist management ───────────────────────────────────────────────────

    async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("New Playlist", "Playlist name:", "New Playlist");
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            Player?.CreatePlaylist(result);
    }

    void OnDeletePlaylist(object? sender, RoutedEventArgs e)
    {
        if (Player?.SelectedPlaylist is { } pl)
            Player.DeletePlaylist(pl);
    }

    // ── Track management ──────────────────────────────────────────────────────

    void OnDeleteTrack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AudioTrack track)
            Player?.DeleteTrack(track);
    }

    void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Border)?.DataContext is not AudioTrackRow row) return;

        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsRightButtonPressed) return;

        Player?.SelectTrack(row);

        if (e.ClickCount == 2)
        {
            Player?.Play(row.Track);
            e.Handled = true;
        }
    }

    async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Player is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var picker = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title         = "Import Audio Files",
            AllowMultiple = true,
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
                new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };

        var files = await owner.StorageProvider.OpenFilePickerAsync(picker);
        if (files.Count == 0) return;

        var paths = files.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrEmpty(p));
        await Player.ImportFilesAsync(paths, async fileName =>
        {
            var dlg = new FileConflictDialog(fileName);
            return await dlg.ShowAsync(owner);
        });
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (Player is null) return;
        if (Player.State == PlaybackState.Playing) Player.Pause();
        else Player.Play();
    }

    void OnPrevious(object? sender, RoutedEventArgs e)    => Player?.Previous();
    void OnNext(object? sender, RoutedEventArgs e)        => Player?.Next();
    void OnSeekBack(object? sender, RoutedEventArgs e)    => Player?.SeekRelative(-10);
    void OnSeekForward(object? sender, RoutedEventArgs e) => Player?.SeekRelative(10);

    void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider s) Player?.Seek(s.Value);
    }

    // ── Clip Properties context menu ──────────────────────────────────────────

    void OnSpd05(object? s, RoutedEventArgs e)  => Player?.SetSpeed(0.5f);
    void OnSpd075(object? s, RoutedEventArgs e) => Player?.SetSpeed(0.75f);
    void OnSpd1(object? s, RoutedEventArgs e)   => Player?.SetSpeed(1.0f);
    void OnSpd125(object? s, RoutedEventArgs e) => Player?.SetSpeed(1.25f);
    void OnSpd15(object? s, RoutedEventArgs e)  => Player?.SetSpeed(1.5f);
    void OnSpd2(object? s, RoutedEventArgs e)   => Player?.SetSpeed(2.0f);

    // ── Playlist Properties context menu ─────────────────────────────────────

    void OnResumeTop(object? sender, RoutedEventArgs e)  => Player?.SetResumeMode(ResumeMode.FromTop);
    void OnResumeLast(object? sender, RoutedEventArgs e) => Player?.SetResumeMode(ResumeMode.FromLastPosition);
}
```

- [ ] **Step 4.4: Build**

```
dotnet build ShowCast.sln
```

Expected: BUILD SUCCEEDED. Fix any Avalonia binding compile warnings (the AXAML compiler may warn about path depth — these are warnings, not errors).

- [ ] **Step 4.5: Run the app and verify channel tab strip**

```
dotnet run --project ShowCast
```

Verify:
1. Audio panel shows a tab strip with "Default" channel at the top
2. Click "+" → dialog prompts for channel name → new tab appears
3. Right-click a tab → "Rename Channel..." and "Remove Channel..." appear
4. Switching tabs changes the playlist/transport content below
5. Existing playlist/playback features still work

- [ ] **Step 4.6: Commit**

```
git add Views/AudioPlayerPanel.axaml Views/AudioPlayerPanel.axaml.cs Views/TabbedRightPanel.axaml
git commit -m "feat(audio-routing): channel tab strip in Audio panel; panel DataContext → MainViewModel"
```

---

## Task 5: AudioDeviceEnumerator

**Files:**
- Create: `Core/AudioDeviceEnumerator.cs`
- Create: `ShowCast.Tests/Core/AudioDeviceEnumeratorTests.cs`

- [ ] **Step 5.1: Write failing tests**

Create `ShowCast.Tests/Core/AudioDeviceEnumeratorTests.cs`:

```csharp
using System.Collections.Generic;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class AudioDeviceEnumeratorTests
{
    // ── EnumerateNdi ─────────────────────────────────────────────────────────
    // (EnumerateHardware requires a live LibVLC instance so we only test NDI here)

    [Fact]
    public void EnumerateNdi_NoNdiOutputs_ReturnsEmptyList()
    {
        var showFile = new ShowFile(); // no NDI outputs
        var result   = AudioDeviceEnumerator.EnumerateNdi(showFile);
        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateNdi_WithNdiOutput_ReturnsDestination()
    {
        var showFile  = new ShowFile();
        var ndiConfig = new OutputConfig
        {
            Name          = "ShowCast Main",
            Type          = OutputType.NDI,
            NdiStreamName = "SC-Main"
        };
        showFile.AddOutput(ndiConfig);

        var result = AudioDeviceEnumerator.EnumerateNdi(showFile);

        Assert.Single(result);
        Assert.Equal(AudioRouteType.Ndi,  result[0].Type);
        Assert.Equal("SC-Main",           result[0].DeviceId);
        Assert.Equal("ShowCast Main",     result[0].SystemName);
        Assert.Equal("ShowCast Main",     result[0].DisplayName);
    }

    [Fact]
    public void EnumerateNdi_IgnoresNonNdiOutputs()
    {
        var showFile    = new ShowFile();
        var displayCfg  = new OutputConfig { Name = "Program", Type = OutputType.Display };
        showFile.AddOutput(displayCfg);

        var result = AudioDeviceEnumerator.EnumerateNdi(showFile);
        Assert.Empty(result);
    }

    // ── MergeHardware ─────────────────────────────────────────────────────────

    [Fact]
    public void MergeHardware_NewDevice_AddsToList()
    {
        var existing = new List<AudioDestination>();
        var fresh    = new List<AudioDestination>
        {
            new() { DeviceId = "hw-001", SystemName = "Realtek", DisplayName = "Realtek" }
        };

        AudioDeviceEnumerator.MergeHardware(existing, fresh);

        Assert.Single(existing);
        Assert.Equal("hw-001", existing[0].DeviceId);
    }

    [Fact]
    public void MergeHardware_ExistingDevice_UpdatesSystemNameOnly()
    {
        var dest = new AudioDestination
        {
            DeviceId    = "hw-001",
            SystemName  = "Old System Name",
            DisplayName = "My Custom Name"   // user-renamed — must not be touched
        };
        var existing = new List<AudioDestination> { dest };
        var fresh    = new List<AudioDestination>
        {
            new() { DeviceId = "hw-001", SystemName = "New System Name", DisplayName = "New System Name" }
        };

        AudioDeviceEnumerator.MergeHardware(existing, fresh);

        Assert.Single(existing);
        Assert.Equal("New System Name", existing[0].SystemName);
        Assert.Equal("My Custom Name",  existing[0].DisplayName); // user name preserved
    }
}
```

- [ ] **Step 5.2: Run tests — verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioDeviceEnumeratorTests" -v m
```

Expected: compilation errors — `AudioDeviceEnumerator` not defined.

- [ ] **Step 5.3: Create `Core/AudioDeviceEnumerator.cs`**

```csharp
using System.Collections.Generic;
using System.Linq;
using LibVLCSharp.Shared;

namespace ShowCast.Core;

public static class AudioDeviceEnumerator
{
    /// <summary>
    /// Enumerates hardware output devices via LibVLC WASAPI.
    /// Returns fresh <see cref="AudioDestination"/> objects — call
    /// <see cref="MergeHardware"/> to reconcile with stored destinations.
    /// </summary>
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

    /// <summary>
    /// Returns NDI destinations derived from the show file's NDI outputs.
    /// </summary>
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

    /// <summary>
    /// Reconciles freshly enumerated hardware devices into the stored list:
    /// - New devices are appended.
    /// - Existing devices have their <see cref="AudioDestination.SystemName"/> updated.
    /// - <see cref="AudioDestination.DisplayName"/> (user-editable alias) is never touched.
    /// - Devices no longer present remain in the list (greyed out in the UI).
    /// </summary>
    public static void MergeHardware(
        List<AudioDestination> existing,
        IReadOnlyList<AudioDestination> fresh)
    {
        foreach (var freshDest in fresh)
        {
            var match = existing.FirstOrDefault(e =>
                e.Type == AudioRouteType.Hardware &&
                e.DeviceId == freshDest.DeviceId);

            if (match is null)
            {
                existing.Add(freshDest);
            }
            else
            {
                // Only update the system-assigned name — preserve user DisplayName
                match.SystemName = freshDest.SystemName;
            }
        }
    }
}
```

- [ ] **Step 5.4: Run tests — verify they pass**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioDeviceEnumeratorTests" -v m
```

Expected: All 5 tests PASS.

- [ ] **Step 5.5: Commit**

```
git add Core/AudioDeviceEnumerator.cs ShowCast.Tests/Core/AudioDeviceEnumeratorTests.cs
git commit -m "feat(audio-routing): AudioDeviceEnumerator with hardware enumeration and merge logic"
```

---

## Task 6: AudioSettingsViewModel

**Files:**
- Create: `ViewModels/AudioSettingsViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/AudioSettingsViewModelTests.cs`

- [ ] **Step 6.1: Write failing tests**

Create `ShowCast.Tests/ViewModels/AudioSettingsViewModelTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class AudioSettingsViewModelTests
{
    static MainViewModel MakeMainVm()
    {
        var vm = new MainViewModel();
        return vm;
    }

    [Fact]
    public void AudioSettingsViewModel_Constructs_WithChannelsFromMain()
    {
        var mainVm = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        Assert.NotEmpty(settingsVm.Channels);
    }

    [Fact]
    public void SetRoute_AssignsActiveDestinationId_ToChannelModel()
    {
        var mainVm = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        var dest = new AudioDestination
        {
            Id      = Guid.NewGuid(),
            DeviceId = "hw-001",
            Type    = AudioRouteType.Hardware
        };
        settingsVm.Destinations.Add(dest);

        var channel = settingsVm.Channels[0];
        settingsVm.SetRoute(channel, dest);

        Assert.Equal(dest.Id, channel.Model.ActiveDestinationId);
    }

    [Fact]
    public void SetRoute_Null_ClearsActiveDestinationId()
    {
        var mainVm = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        var channel = settingsVm.Channels[0];
        channel.Model.ActiveDestinationId = Guid.NewGuid();

        settingsVm.SetRoute(channel, null);

        Assert.Null(channel.Model.ActiveDestinationId);
    }

    [Fact]
    public void SetRoute_SameDestinationTwice_TogglesOff()
    {
        var mainVm = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        var dest = new AudioDestination { Id = Guid.NewGuid(), DeviceId = "hw-001" };
        settingsVm.Destinations.Add(dest);

        var channel = settingsVm.Channels[0];
        settingsVm.SetRoute(channel, dest);
        settingsVm.SetRoute(channel, dest); // toggle off

        Assert.Null(channel.Model.ActiveDestinationId);
    }
}
```

- [ ] **Step 6.2: Run tests — verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioSettingsViewModelTests" -v m
```

Expected: compilation errors — `AudioSettingsViewModel` not defined.

- [ ] **Step 6.3: Create `ViewModels/AudioSettingsViewModel.cs`**

```csharp
using System.Collections.ObjectModel;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class AudioSettingsViewModel : ReactiveObject
{
    readonly MainViewModel _main;

    /// <summary>Reflects MainViewModel.AudioChannels — rows in the routing matrix.</summary>
    public ObservableCollection<AudioChannelViewModel> Channels => _main.AudioChannels;

    /// <summary>All known hardware and NDI destinations — columns in the routing matrix.</summary>
    public ObservableCollection<AudioDestination> Destinations { get; } = new();

    public AudioSettingsViewModel(MainViewModel main)
    {
        _main = main;

        // Populate destinations from AppSettings
        foreach (var d in main.ShowFileDestinations)
            Destinations.Add(d);
    }

    /// <summary>
    /// Sets or toggles the route for a channel.
    /// Passing the currently active destination clears the route (OS default).
    /// </summary>
    public void SetRoute(AudioChannelViewModel channel, AudioDestination? destination)
    {
        if (destination is not null &&
            channel.Model.ActiveDestinationId == destination.Id)
        {
            // Toggle off: clear the route
            channel.Model.ActiveDestinationId = null;
            channel.ApplyRoute(null);
        }
        else
        {
            channel.Model.ActiveDestinationId = destination?.Id;
            channel.ApplyRoute(destination);
        }
    }

    /// <summary>
    /// Re-enumerates hardware devices and refreshes the Destinations list.
    /// Requires a LibVLC instance from the first available channel's player.
    /// </summary>
    public void RefreshDevices()
    {
        // Enumerate hardware via first available LibVLC instance
        var libVlc = _main.AudioChannels
            .Select(c => c.Player.LibVlc)
            .FirstOrDefault(v => v is not null);

        if (libVlc is not null)
        {
            var fresh = AudioDeviceEnumerator.EnumerateHardware(libVlc);
            var stored = _main.ShowFileDestinations;
            AudioDeviceEnumerator.MergeHardware(stored, fresh);

            // Sync to observable collection
            Destinations.Clear();
            foreach (var d in stored)
                Destinations.Add(d);
        }

        // Enumerate NDI
        var ndiDestinations = AudioDeviceEnumerator.EnumerateNdi(_main.ShowFile);
        foreach (var nd in ndiDestinations)
        {
            if (!Destinations.Any(d => d.Type == AudioRouteType.Ndi && d.DeviceId == nd.DeviceId))
                Destinations.Add(nd);
        }
    }
}
```

**Note:** This requires two small additions to `MainViewModel`:
1. `public ShowFile ShowFile => _showFile;` — exposes the show file (read-only)
2. `public List<AudioDestination> ShowFileDestinations => _showFile.Settings.AudioDestinations;` — shortcut for the stored destinations list
3. `AudioPlayerViewModel` must expose `public LibVLC? LibVlc => _libVlc;` — see Step 6.4

- [ ] **Step 6.4: Add supporting members**

**In `ViewModels/AudioPlayerViewModel.cs`**, add a public accessor for the LibVLC instance (after the field declarations):

```csharp
    /// <summary>Exposes the LibVLC instance for device enumeration. Null if unavailable.</summary>
    public LibVLC? LibVlc => _libVlc;
```

**In `ViewModels/MainViewModel.cs`**, add two public read-only accessors near the file/settings section:

```csharp
    /// <summary>Exposes ShowFile for dialogs that need read-only access.</summary>
    public ShowFile ShowFile => _showFile;

    /// <summary>Shortcut to the stored audio destination list in AppSettings.</summary>
    public List<ShowCast.Core.AudioDestination> ShowFileDestinations
        => _showFile.Settings.AudioDestinations;
```

- [ ] **Step 6.5: Run tests — verify they pass**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~AudioSettingsViewModelTests" -v m
```

Expected: All 4 tests PASS.

- [ ] **Step 6.6: Run full test suite**

```
dotnet test ShowCast.Tests -v m
```

Expected: All tests PASS.

- [ ] **Step 6.7: Commit**

```
git add ViewModels/AudioSettingsViewModel.cs ViewModels/AudioPlayerViewModel.cs ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/AudioSettingsViewModelTests.cs
git commit -m "feat(audio-routing): AudioSettingsViewModel with SetRoute and RefreshDevices"
```

---

## Task 7: AudioSettingsDialog + MainWindow Menu Item

**Files:**
- Create: `Views/AudioSettingsDialog.axaml`
- Create: `Views/AudioSettingsDialog.axaml.cs`
- Modify: `Views/MainWindow.axaml`
- Modify: `Views/MainWindow.axaml.cs`

No unit tests for dialog UI code. Manual verification in Step 7.5.

- [ ] **Step 7.1: Create `Views/AudioSettingsDialog.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="ShowCast.Views.AudioSettingsDialog"
        Title="Audio Routing"
        Width="800" Height="520"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="#1a1a1a"
        Foreground="White"
        FontFamily="Segoe UI, sans-serif">

    <Grid RowDefinitions="Auto,*,Auto" Margin="16">

        <!-- ── Header ── -->
        <Grid Grid.Row="0" ColumnDefinitions="*,Auto" Margin="0,0,0,12">
            <TextBlock Text="AUDIO ROUTING"
                       FontSize="14" FontWeight="Bold"
                       Foreground="#e07050"
                       VerticalAlignment="Center"/>
            <Button Grid.Column="1"
                    Content="↺  Refresh Devices"
                    Click="OnRefresh"
                    Background="#333333" Foreground="#aaaaaa"
                    BorderThickness="0" CornerRadius="4"
                    Padding="10,5" FontSize="11">
                <Button.Styles>
                    <Style Selector="Button:pointerover /template/ ContentPresenter">
                        <Setter Property="Background" Value="#444444"/>
                    </Style>
                </Button.Styles>
            </Button>
        </Grid>

        <!-- ── Matrix (built in code-behind) ── -->
        <Border Grid.Row="1"
                Background="#111111"
                BorderBrush="#333333" BorderThickness="1"
                CornerRadius="4">
            <ScrollViewer HorizontalScrollBarVisibility="Auto"
                          VerticalScrollBarVisibility="Auto">
                <Grid x:Name="MatrixGrid" Margin="0"/>
            </ScrollViewer>
        </Border>

        <!-- ── Footer ── -->
        <Grid Grid.Row="2" Margin="0,12,0,0">
            <Button Content="Close"
                    Click="OnClose"
                    HorizontalAlignment="Right"
                    Background="#333333" Foreground="White"
                    BorderThickness="0" CornerRadius="4"
                    Padding="16,6" FontSize="11">
                <Button.Styles>
                    <Style Selector="Button:pointerover /template/ ContentPresenter">
                        <Setter Property="Background" Value="#444444"/>
                    </Style>
                </Button.Styles>
            </Button>
        </Grid>

    </Grid>
</Window>
```

- [ ] **Step 7.2: Create `Views/AudioSettingsDialog.axaml.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class AudioSettingsDialog : Window
{
    readonly AudioSettingsViewModel _vm;

    // Radio cells: key = (channelVm, destinationId), value = the cell Border
    readonly Dictionary<(AudioChannelViewModel, Guid), Border> _cells = new();

    public AudioSettingsDialog(MainViewModel mainVm)
    {
        InitializeComponent();
        _vm = new AudioSettingsViewModel(mainVm);
    }

    protected override void OnOpened(EventArgs e)
    {
        base.OnOpened(e);
        _vm.RefreshDevices();
        BuildMatrix();
    }

    // ── Matrix build ──────────────────────────────────────────────────────────

    void BuildMatrix()
    {
        _cells.Clear();
        MatrixGrid.Children.Clear();
        MatrixGrid.ColumnDefinitions.Clear();
        MatrixGrid.RowDefinitions.Clear();

        var channels     = _vm.Channels.ToList();
        var destinations = _vm.Destinations.ToList();

        if (channels.Count == 0 || destinations.Count == 0)
        {
            MatrixGrid.Children.Add(new TextBlock
            {
                Text       = destinations.Count == 0
                    ? "No audio devices found — click Refresh Devices."
                    : "No audio channels defined.",
                Foreground = new SolidColorBrush(Color.Parse("#666666")),
                FontSize   = 11,
                Margin     = new Thickness(16)
            });
            return;
        }

        // Column definitions: label column + one per destination + grouping headers
        MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition(120, GridUnitType.Pixel)); // channel labels
        foreach (var _ in destinations)
            MatrixGrid.ColumnDefinitions.Add(new ColumnDefinition(90, GridUnitType.Pixel));

        // Row definitions: section header + device header + one per channel
        MatrixGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // HARDWARE/NDI section header
        MatrixGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // device name headers
        foreach (var _ in channels)
            MatrixGrid.RowDefinitions.Add(new RowDefinition(GridLength.Auto)); // channel rows

        // ── Section header row (row 0) ────────────────────────────────────────
        int hwCount  = destinations.Count(d => d.Type == AudioRouteType.Hardware);
        int ndiCount = destinations.Count(d => d.Type == AudioRouteType.Ndi);

        if (hwCount > 0)
        {
            var hwHeader = MakeHeaderLabel("HARDWARE", "#aaaaaa");
            Grid.SetRow(hwHeader,     0);
            Grid.SetColumn(hwHeader,  1);
            Grid.SetColumnSpan(hwHeader, hwCount);
            MatrixGrid.Children.Add(hwHeader);
        }
        if (ndiCount > 0)
        {
            var ndiHeader = MakeHeaderLabel("NDI", "#4a9eff");
            Grid.SetRow(ndiHeader,    0);
            Grid.SetColumn(ndiHeader, 1 + hwCount);
            Grid.SetColumnSpan(ndiHeader, ndiCount);
            MatrixGrid.Children.Add(ndiHeader);
        }

        // ── Device name headers (row 1) ───────────────────────────────────────
        for (int col = 0; col < destinations.Count; col++)
        {
            var dest     = destinations[col];
            bool isNdi   = dest.Type == AudioRouteType.Ndi;
            var nameColor = isNdi ? Color.Parse("#4a9eff") : Color.Parse("#aaaaaa");
            var sysColor  = Color.Parse("#555555");

            // Editable display name
            var nameBox = new TextBox
            {
                Text            = dest.DisplayName,
                Background      = Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Foreground      = new SolidColorBrush(nameColor),
                FontSize        = 10,
                IsReadOnly      = isNdi,
                Padding         = new Thickness(4, 2),
                HorizontalContentAlignment = HorizontalAlignment.Center,
            };
            nameBox.Tag = dest;
            nameBox.LostFocus += OnDestNameCommit;
            nameBox.KeyDown   += OnDestNameKeyDown;

            // System name label (read-only, below)
            var sysLabel = new TextBlock
            {
                Text       = dest.SystemName,
                Foreground = new SolidColorBrush(sysColor),
                FontSize   = 8,
                TextAlignment = Avalonia.Media.TextAlignment.Center,
                Margin     = new Thickness(0, 0, 0, 4),
            };

            var headerStack = new StackPanel
            {
                Spacing = 0,
                HorizontalAlignment = HorizontalAlignment.Center,
            };
            headerStack.Children.Add(nameBox);
            headerStack.Children.Add(sysLabel);

            var headerBorder = new Border
            {
                Child           = headerStack,
                Background      = new SolidColorBrush(Color.Parse("#1a1a1a")),
                BorderBrush     = new SolidColorBrush(Color.Parse("#333333")),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding         = new Thickness(4, 6),
            };
            Grid.SetRow(headerBorder,    1);
            Grid.SetColumn(headerBorder, col + 1);
            MatrixGrid.Children.Add(headerBorder);
        }

        // ── Channel rows ──────────────────────────────────────────────────────
        for (int row = 0; row < channels.Count; row++)
        {
            var ch = channels[row];

            // Channel label
            var label = new TextBlock
            {
                Text       = ch.Name,
                Foreground = Brushes.White,
                FontSize   = 11,
                VerticalAlignment   = VerticalAlignment.Center,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin     = new Thickness(10, 0),
            };
            var labelBorder = new Border
            {
                Child           = label,
                Background      = new SolidColorBrush(Color.Parse("#111111")),
                BorderBrush     = new SolidColorBrush(Color.Parse("#222222")),
                BorderThickness = new Thickness(0, 0, 1, 1),
                Height          = 40,
            };
            Grid.SetRow(labelBorder,    row + 2);
            Grid.SetColumn(labelBorder, 0);
            MatrixGrid.Children.Add(labelBorder);

            // Radio cells
            for (int col = 0; col < destinations.Count; col++)
            {
                var dest   = destinations[col];
                bool isNdi = dest.Type == AudioRouteType.Ndi;
                bool active = ch.Model.ActiveDestinationId == dest.Id;

                var circle = new Ellipse(active);
                var cellBorder = new Border
                {
                    Child           = circle,
                    Background      = new SolidColorBrush(Color.Parse("#111111")),
                    BorderBrush     = new SolidColorBrush(Color.Parse("#222222")),
                    BorderThickness = new Thickness(0, 0, 1, 1),
                    IsEnabled       = !isNdi,
                    Opacity         = isNdi ? 0.35 : 1.0,
                    HorizontalAlignment = HorizontalAlignment.Stretch,
                    Height          = 40,
                    Cursor          = isNdi ? new Cursor(StandardCursorType.No)
                                           : new Cursor(StandardCursorType.Hand),
                };
                if (isNdi)
                    ToolTip.SetTip(cellBorder, "NDI audio routing available in next phase");

                var capturedCh   = ch;
                var capturedDest = dest;
                cellBorder.PointerPressed += (_, _) =>
                {
                    _vm.SetRoute(capturedCh, capturedDest);
                    RefreshCellStates(capturedCh);
                };

                _cells[(ch, dest.Id)] = cellBorder;
                Grid.SetRow(cellBorder,    row + 2);
                Grid.SetColumn(cellBorder, col + 1);
                MatrixGrid.Children.Add(cellBorder);
            }
        }
    }

    /// <summary>Refreshes the filled/empty state of all cells in a channel's row.</summary>
    void RefreshCellStates(AudioChannelViewModel ch)
    {
        foreach (var kvp in _cells.Where(k => k.Key.Item1 == ch))
        {
            bool active = ch.Model.ActiveDestinationId == kvp.Key.Item2;
            if (kvp.Value.Child is Ellipse ellipse)
                ellipse.Update(active);
        }
    }

    /// <summary>Creates a circle indicating routing state.</summary>
    static Border MakeHeaderLabel(string text, string colorHex) => new()
    {
        Child = new TextBlock
        {
            Text              = text,
            Foreground        = new SolidColorBrush(Color.Parse(colorHex)),
            FontSize          = 9,
            FontWeight        = FontWeight.Bold,
            LetterSpacing     = 1,
            TextAlignment     = Avalonia.Media.TextAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        },
        Background      = new SolidColorBrush(Color.Parse("#1a1a1a")),
        BorderBrush     = new SolidColorBrush(Color.Parse("#333333")),
        BorderThickness = new Thickness(0, 0, 0, 1),
        Padding         = new Thickness(4, 6),
        HorizontalAlignment = HorizontalAlignment.Stretch,
    };

    // ── Column header rename ──────────────────────────────────────────────────

    void OnDestNameCommit(object? sender, RoutedEventArgs e)
    {
        if (sender is TextBox tb && tb.Tag is AudioDestination dest)
            dest.DisplayName = tb.Text ?? dest.SystemName;
    }

    void OnDestNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && sender is TextBox tb && tb.Tag is AudioDestination dest)
        {
            dest.DisplayName = tb.Text ?? dest.SystemName;
            tb.IsReadOnly = true; // unfocus effect
            tb.IsReadOnly = false;
        }
    }

    // ── Toolbar ───────────────────────────────────────────────────────────────

    void OnRefresh(object? sender, RoutedEventArgs e)
    {
        _vm.RefreshDevices();
        BuildMatrix();
    }

    void OnClose(object? sender, RoutedEventArgs e) => Close();
}

// ── Helper: radio-button circle ───────────────────────────────────────────────

internal class Ellipse : Border
{
    public Ellipse(bool active)
    {
        Width           = 16;
        Height          = 16;
        CornerRadius    = new Avalonia.CornerRadius(8);
        HorizontalAlignment = HorizontalAlignment.Center;
        VerticalAlignment   = VerticalAlignment.Center;
        Update(active);
    }

    public void Update(bool active)
    {
        Background  = active
            ? new SolidColorBrush(Color.Parse("#e07050"))
            : Brushes.Transparent;
        BorderBrush     = new SolidColorBrush(active
            ? Color.Parse("#e07050")
            : Color.Parse("#444444"));
        BorderThickness = new Thickness(1);
    }
}
```

- [ ] **Step 7.3: Add Settings → Audio to `Views/MainWindow.axaml`**

Find:
```xml
                        <MenuItem Header="Settings">
                            <MenuItem Header="Screens"  Click="OnScreenConfig"/>
                            <MenuItem Header="Schedule" Click="OnScheduler"/>
                            <MenuItem Header="Network"/>
                            <MenuItem Header="Advanced"/>
                        </MenuItem>
```

Replace with:
```xml
                        <MenuItem Header="Settings">
                            <MenuItem Header="Screens"  Click="OnScreenConfig"/>
                            <MenuItem Header="Audio"    Click="OnAudioSettings"/>
                            <MenuItem Header="Schedule" Click="OnScheduler"/>
                            <MenuItem Header="Network"/>
                            <MenuItem Header="Advanced"/>
                        </MenuItem>
```

- [ ] **Step 7.4: Add `OnAudioSettings` to `Views/MainWindow.axaml.cs`**

In `MainWindow.axaml.cs`, find the `OnScreenConfig` handler (or any `OnXxx` event handler near it). Add the following handler alongside it:

```csharp
    async void OnAudioSettings(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new AudioSettingsDialog(VM);
        await dialog.ShowDialog(this);
    }
```

- [ ] **Step 7.5: Build and run the app**

```
dotnet build ShowCast.sln
dotnet run --project ShowCast
```

Verify:
1. **ShowCast → Settings → Audio** menu item appears between Screens and Schedule
2. Clicking it opens the Audio Routing dialog (800×520, dark theme)
3. The Refresh button discovers hardware output devices and shows them as columns
4. NDI outputs from the show file appear as greyed NDI columns with tooltip
5. Clicking a hardware cell fills its circle and clears any other filled cell in that row
6. Clicking the same cell again clears it (toggle-off)
7. Column header name is editable — double-click, type, press Enter; `SystemName` shown below in grey is read-only
8. Close button closes the dialog

- [ ] **Step 7.6: Run full test suite**

```
dotnet test ShowCast.Tests -v m
```

Expected: All tests PASS.

- [ ] **Step 7.7: Commit**

```
git add Views/AudioSettingsDialog.axaml Views/AudioSettingsDialog.axaml.cs Views/MainWindow.axaml Views/MainWindow.axaml.cs
git commit -m "feat(audio-routing): AudioSettingsDialog routing matrix; Settings → Audio menu item"
```

---

## Self-Review

### 1. Spec Coverage

| Spec Requirement | Covered By |
|---|---|
| `AudioChannel` / `AudioDestination` models in `AppSettings` | Task 1 |
| `AudioPlaylist.ChannelId` | Task 1 |
| `AudioChannelViewModel` wrapping `AudioPlayerViewModel` | Task 2 |
| `AudioPlayerViewModel.SetAudioDevice` + apply in `Play()` | Task 2 |
| `MainViewModel.AudioChannels` + `SelectedAudioChannel` replaces `AudioPlayer` | Task 3 |
| `FirePageAudioTrigger` updated for multi-channel | Task 3 |
| Legacy file migration (empty `AudioChannels` → Default channel) | Task 3 |
| Save/load per-channel playlists and `SelectedPlaylistId` | Task 3 |
| Channel tab strip in Audio panel | Task 4 |
| `+` tab creates new channel | Task 4 |
| Right-click tab → Rename / Remove | Task 4 |
| Remove channel migrates playlists to Default | Task 3 (`RemoveAudioChannel`) |
| `AudioDeviceEnumerator.EnumerateHardware` + `EnumerateNdi` | Task 5 |
| `MergeHardware` preserves user `DisplayName` | Task 5 |
| `AudioSettingsViewModel.SetRoute` + `RefreshDevices` | Task 6 |
| `AudioSettingsDialog` routing matrix | Task 7 |
| Column header rename (DisplayName editable, SystemName read-only) | Task 7 |
| NDI cells disabled with tooltip | Task 7 |
| Refresh button re-enumerates devices | Task 7 |
| Settings → Audio menu item | Task 7 |
| NDI / ASIO / metering out of scope | ✓ (not implemented) |

No gaps found.

### 2. Placeholder Scan

No TBDs, TODOs, or vague steps. All code blocks are complete.

### 3. Type Consistency

- `AudioChannelViewModel.Player` → `AudioPlayerViewModel` ✓
- `AudioChannelViewModel.ApplyRoute(AudioDestination?)` ✓ (matches usage in Tasks 3, 6)
- `AudioPlayerViewModel.SetAudioDevice(string, string)` ✓ (matches Task 2 call in `ApplyRoute`)
- `AudioDeviceEnumerator.MergeHardware(List<AudioDestination>, IReadOnlyList<AudioDestination>)` ✓ (matches usage in Task 6)
- `MainViewModel.ShowFileDestinations` → `List<AudioDestination>` ✓ (matches Task 6 usage)
- `MainViewModel.ShowFile` → `ShowFile` ✓ (matches `EnumerateNdi` parameter type)
- `AudioPlayerViewModel.LibVlc` → `LibVLC?` ✓ (matches Task 6 enumeration usage)

---

**Plan complete and saved to `docs/superpowers/plans/2026-05-26-audio-channel-routing.md`.**

Two execution options:

**1. Subagent-Driven (recommended)** — fresh subagent per task, two-stage spec + quality review between tasks, fast iteration

**2. Inline Execution** — execute tasks in this session using the executing-plans skill, with checkpoints for review

Which approach?
