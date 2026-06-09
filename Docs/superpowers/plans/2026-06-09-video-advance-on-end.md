# Video Layer "Advance on End" Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `AdvanceOnEnd` as a 4th `VideoLoopMode` value that automatically advances to the next slide when a video finishes, or goes black if already on the last page.

**Architecture:** A callback chain threads from LibVLC's end-reached event through `VideoLayerPlayer` → `VideoFrameRegistry` → `OutputState` → `MainViewModel.HandleVideoEnded`. Each layer only knows its immediate neighbor; Core classes remain Avalonia-free. UI-thread dispatch is the MainViewModel's responsibility.

**Tech Stack:** C# / .NET 9, Avalonia, LibVLCSharp, ReactiveUI, xUnit

---

## File Map

| File | Change |
|------|--------|
| `Core/SlideLayer.cs` | Add `AdvanceOnEnd` to `VideoLoopMode` enum |
| `Core/IVideoLayerPlayer.cs` | Add `Action? VideoEnded { get; set; }` |
| `Core/VideoLayerPlayer.cs` | Add `AdvanceOnEnd` case in `OnEndReached` |
| `Core/VideoFrameRegistry.cs` | Add `OnVideoEnded` property; wire into `UpdateSlide` |
| `Core/OutputState.cs` | Add `VideoEndedCallback`; expand `VideoRegistry` setter |
| `ViewModels/MainViewModel.cs` | Inject callback at output creation; add `HandleVideoEnded` |
| `Views/EditorInspectorPanel.axaml` | Add 4th `ComboBoxItem` to `VideoLoopModeBox` |
| `ShowCast.Tests/Core/VideoFrameRegistryTests.cs` | Update `FakePlayer`; add `OnVideoEnded` test |
| `ShowCast.Tests/ViewModels/MainViewModelAdvanceOnEndTests.cs` | New file: 6 tests for `HandleVideoEnded` |

---

### Task 1: Core Signal Layer

Wire the `AdvanceOnEnd` signal from the LibVLC end-reached event through to `VideoFrameRegistry`.

**Files:**
- Modify: `Core/SlideLayer.cs`
- Modify: `Core/IVideoLayerPlayer.cs`
- Modify: `Core/VideoLayerPlayer.cs`
- Modify: `Core/VideoFrameRegistry.cs`
- Modify: `ShowCast.Tests/Core/VideoFrameRegistryTests.cs`

- [ ] **Step 1: Write the failing test for `OnVideoEnded` callback**

Add this test to `ShowCast.Tests/Core/VideoFrameRegistryTests.cs` after the last existing test:

```csharp
[Fact]
public void UpdateSlide_WiresVideoEndedCallbackToNewPlayer()
{
    // FakePlayer needs to expose VideoEnded so we can check it was set.
    // (After this test is added, FakePlayer must implement the new interface member.)
    bool callbackFired = false;
    var players = new List<FakePlayer>();
    var registry = new VideoFrameRegistry(
        new List<AudioDestination>(),
        () => { var p = new FakePlayer(); players.Add(p); return p; });

    registry.OnVideoEnded = () => callbackFired = true;

    var id = Guid.NewGuid();
    var layer = new SlideLayer
    {
        Id = id, Type = LayerType.Video, AssetPath = "clip.mp4",
        VideoLoopMode = VideoLoopMode.AdvanceOnEnd
    };
    var page = new Page();
    page.AddLayer(layer);
    registry.UpdateSlide(page);

    // Simulate the player calling VideoEnded (as VLC would after video finishes).
    players[0].VideoEnded?.Invoke();

    Assert.True(callbackFired);
}
```

- [ ] **Step 2: Run the test to verify it fails**

```
dotnet test ShowCast.Tests --filter "VideoFrameRegistryTests" -v minimal
```

Expected: compile error — `VideoLoopMode.AdvanceOnEnd` does not exist and `FakePlayer` does not have `VideoEnded`.

- [ ] **Step 3: Add `AdvanceOnEnd` to the enum**

In `Core/SlideLayer.cs`, find:

```csharp
public enum VideoLoopMode { Loop, HoldLastFrame, GoBlack }
```

Replace with:

```csharp
public enum VideoLoopMode { Loop, HoldLastFrame, GoBlack, AdvanceOnEnd }
```

- [ ] **Step 4: Add `VideoEnded` to the interface**

In `Core/IVideoLayerPlayer.cs`, add after the `LengthMs` property:

```csharp
Action? VideoEnded { get; set; }
```

Full updated file:

```csharp
using System;
using SkiaSharp;

namespace ShowCast.Core;

public interface IVideoLayerPlayer : IDisposable
{
    SKImage? CurrentFrame { get; }
    long TimeMs   { get; }
    long LengthMs { get; }
    Action? VideoEnded { get; set; }
    void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId, NdiSender? ndiSender = null);
    void Stop();
}
```

- [ ] **Step 5: Implement `VideoEnded` in `VideoLayerPlayer`**

In `Core/VideoLayerPlayer.cs`, add the backing field and property after the `_loopMode` field (line ~28) and add the `AdvanceOnEnd` case in `OnEndReached`.

Add field after `VideoLoopMode _loopMode;`:

```csharp
public Action? VideoEnded { get; set; }
```

Replace the `OnEndReached` method body (lines 121–138):

```csharp
void OnEndReached(object? sender, EventArgs e)
{
    // Queue to thread pool — calling Stop/Play on the VLC event thread deadlocks.
    var capturedMode  = _loopMode;
    var capturedEnded = VideoEnded;
    System.Threading.ThreadPool.QueueUserWorkItem(_ =>
    {
        switch (capturedMode)
        {
            case VideoLoopMode.Loop:
                _player.Stop();
                _player.Play();
                break;
            case VideoLoopMode.GoBlack:
                _currentFrame = null;
                break;
            case VideoLoopMode.AdvanceOnEnd:
                _player.Stop();
                capturedEnded?.Invoke();
                break;
            // HoldLastFrame: do nothing.
        }
    });
}
```

- [ ] **Step 6: Add `OnVideoEnded` to `VideoFrameRegistry` and wire it in `UpdateSlide`**

In `Core/VideoFrameRegistry.cs`:

1. Add the property after the `_players` field:

```csharp
/// <summary>Invoked (on the threadpool) when a video with AdvanceOnEnd mode reaches its end.</summary>
public Action? OnVideoEnded { get; set; }
```

2. In `UpdateSlide`, after `player.Start(...)`:

```csharp
player.Start(filePath, layer.VideoLoopMode, layer.VideoVolume, deviceId, ndiSender);
if (layer.VideoLoopMode == VideoLoopMode.AdvanceOnEnd)
    player.VideoEnded = OnVideoEnded;
_players[layer.Id] = player;
```

Full updated `UpdateSlide` method:

```csharp
public void UpdateSlide(Page? page)
{
    var newLayers = page?.Layers
        .Where(l => l.Type == LayerType.Video && !string.IsNullOrEmpty(l.AssetPath))
        .ToList() ?? new List<SlideLayer>();

    var newIds = newLayers.Select(l => l.Id).ToHashSet();

    // Stop and dispose players for removed layers.
    foreach (var id in _players.Keys.Except(newIds).ToList())
    {
        _players[id].Stop();
        _players[id].Dispose();
        _players.TryRemove(id, out _);
    }

    // Start players for new layers.
    foreach (var layer in newLayers.Where(l => !_players.ContainsKey(l.Id)))
    {
        var player = _playerFactory();
        var filePath = Path.Combine(AppFolders.Video, layer.AssetPath);

        // Resolve audio routing: hardware uses a WASAPI device ID; NDI uses a sender reference.
        var dest = layer.VideoAudioDestinationId is { } destId
            ? _destinations.FirstOrDefault(d => d.Id == destId)
            : null;
        string? deviceId = dest?.Type == AudioRouteType.Hardware ? dest.DeviceId : null;
        NdiSender? ndiSender = dest?.Type == AudioRouteType.Ndi
            ? _ndiLookup?.Invoke(dest.DeviceId)
            : null;

        player.Start(filePath, layer.VideoLoopMode, layer.VideoVolume, deviceId, ndiSender);
        if (layer.VideoLoopMode == VideoLoopMode.AdvanceOnEnd)
            player.VideoEnded = OnVideoEnded;
        _players[layer.Id] = player;
    }
}
```

- [ ] **Step 7: Update `FakePlayer` in the test file to implement the new interface member**

In `ShowCast.Tests/Core/VideoFrameRegistryTests.cs`, add after `public long LengthMs { get; set; }`:

```csharp
public Action? VideoEnded { get; set; }
```

- [ ] **Step 8: Run the test to verify it passes**

```
dotnet test ShowCast.Tests --filter "VideoFrameRegistryTests" -v minimal
```

Expected: All tests pass (including the new `UpdateSlide_WiresVideoEndedCallbackToNewPlayer`).

- [ ] **Step 9: Commit**

```
git add Core/SlideLayer.cs Core/IVideoLayerPlayer.cs Core/VideoLayerPlayer.cs Core/VideoFrameRegistry.cs ShowCast.Tests/Core/VideoFrameRegistryTests.cs
git commit -m "feat: add AdvanceOnEnd VideoLoopMode with callback chain through registry"
```

---

### Task 2: OutputState Wiring

Propagate the `VideoEndedCallback` from `MainViewModel` through `OutputState` into any `VideoFrameRegistry` assigned to it.

**Files:**
- Modify: `Core/OutputState.cs`

- [ ] **Step 1: Add `VideoEndedCallback` and expand the `VideoRegistry` setter**

In `Core/OutputState.cs`, add the new property after the `VideoRegistry` property (replacing lines 60–65):

```csharp
private VideoFrameRegistry? _videoRegistry;
public VideoFrameRegistry? VideoRegistry
{
    get => _videoRegistry;
    set
    {
        this.RaiseAndSetIfChanged(ref _videoRegistry, value);
        if (value is not null) value.OnVideoEnded = VideoEndedCallback;
    }
}

/// <summary>
/// Set by MainViewModel immediately after creating this OutputState.
/// Propagated into VideoRegistry whenever a new registry is assigned.
/// </summary>
public Action? VideoEndedCallback { get; set; }
```

No tests needed for this step — it is pure wiring with no logic. The integration is tested in Task 3.

- [ ] **Step 2: Build to verify no compile errors**

```
dotnet build ShowCast -v minimal
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```
git add Core/OutputState.cs
git commit -m "feat: add VideoEndedCallback to OutputState; propagate into VideoRegistry on assign"
```

---

### Task 3: MainViewModel — Inject Callback and Handle Video End

Inject `VideoEndedCallback` at every `OutputState` creation site, and implement `HandleVideoEnded` that mirrors the non-looping advance path already in `_pageTimer.Elapsed`.

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/MainViewModelAdvanceOnEndTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ShowCast.Tests/ViewModels/MainViewModelAdvanceOnEndTests.cs`:

```csharp
using System;
using System.Reflection;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelAdvanceOnEndTests
{
    static MainViewModel MakeVm() => new MainViewModel(
        new ShowFile { Name = "Test" },
        isTestMode: true);

    static void CallHandleVideoEnded(MainViewModel vm, OutputState output)
    {
        var method = typeof(MainViewModel).GetMethod(
            "HandleVideoEnded",
            BindingFlags.NonPublic | BindingFlags.Instance)!;
        method.Invoke(vm, new object[] { output });
    }

    // ── Flat-view tests ────────────────────────────────────────────────────────

    [Fact]
    public void HandleVideoEnded_FlatView_AdvancesToNextPage()
    {
        var vm = MakeVm();
        var pkg = vm.ShowFile.AddPackage("Pkg");
        var p1  = pkg.AddPage(); var p2 = pkg.AddPage();
        vm.LoadPackageToSelectedOutput(pkg);
        vm.SelectedPage = vm.Pages[0];
        vm.GoLive();

        var output = vm.SelectedOutput!;
        Assert.Equal(p1, output.LivePage);

        CallHandleVideoEnded(vm, output);

        Assert.Equal(p2, output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_FlatView_LastPage_GoesBlack()
    {
        var vm = MakeVm();
        var pkg = vm.ShowFile.AddPackage("Pkg");
        pkg.AddPage(); pkg.AddPage();
        vm.LoadPackageToSelectedOutput(pkg);
        vm.SelectedPage = vm.Pages[1]; // last page
        vm.GoLive();

        var output = vm.SelectedOutput!;
        CallHandleVideoEnded(vm, output);

        Assert.Null(output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_FlatView_NoLivePage_IsNoOp()
    {
        var vm = MakeVm();
        var pkg = vm.ShowFile.AddPackage("Pkg");
        pkg.AddPage();
        vm.LoadPackageToSelectedOutput(pkg);
        // Don't call GoLive — output.LivePage is null

        var output = vm.SelectedOutput!;
        // Should not throw
        CallHandleVideoEnded(vm, output);

        Assert.Null(output.LivePage);
    }

    // ── Rundown-view tests ─────────────────────────────────────────────────────

    [Fact]
    public void HandleVideoEnded_RundownView_AdvancesToNextPageInGroup()
    {
        var vm = MakeVm();
        vm.ShowingRundown = true;
        var pkg = vm.ShowFile.AddPackage("Pkg");
        var p1  = pkg.AddPage(); var p2 = pkg.AddPage();
        vm.RefreshPageGroups();

        var group = vm.PageGroups[0];
        vm.GoLiveFromGroup(group.Pages[0]);

        var output = group.SelectedOutput!;
        Assert.Equal(p1, output.LivePage);

        CallHandleVideoEnded(vm, output);

        Assert.Equal(p2, output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_RundownView_LastPage_GoesBlack()
    {
        var vm = MakeVm();
        vm.ShowingRundown = true;
        var pkg = vm.ShowFile.AddPackage("Pkg");
        pkg.AddPage(); pkg.AddPage();
        vm.RefreshPageGroups();

        var group = vm.PageGroups[0];
        vm.GoLiveFromGroup(group.Pages[1]); // last page

        var output = group.SelectedOutput!;
        CallHandleVideoEnded(vm, output);

        Assert.Null(output.LivePage);
    }

    [Fact]
    public void HandleVideoEnded_RundownView_UnknownOutput_IsNoOp()
    {
        var vm = MakeVm();
        vm.ShowingRundown = true;
        var pkg = vm.ShowFile.AddPackage("Pkg");
        pkg.AddPage();
        vm.RefreshPageGroups();

        // An output not tied to any group
        var orphan = new OutputState(new OutputConfig { Name = "Orphan" });
        orphan.GoLive(pkg.Pages[0], 0);

        // Should not throw
        CallHandleVideoEnded(vm, orphan);
    }
}
```

- [ ] **Step 2: Run the tests to verify they fail**

```
dotnet test ShowCast.Tests --filter "MainViewModelAdvanceOnEndTests" -v minimal
```

Expected: Compile errors — `HandleVideoEnded` does not exist yet; `RefreshPageGroups` and `GoLiveFromGroup` may need public visibility check.

- [ ] **Step 3: Add `HandleVideoEnded` to `MainViewModel`**

In `ViewModels/MainViewModel.cs`, add this method in the `// ── Page timer (auto-advance) ──` region, after `FormatCountdown`:

```csharp
void HandleVideoEnded(OutputState output)
{
    // Called on the UI thread via InvokeAsync.
    if (ShowingRundown)
    {
        var group = PageGroups.FirstOrDefault(g => g.SelectedOutput == output);
        if (group is null) return;

        var groupLive = output.LivePage;
        var liveVm    = groupLive is not null
            ? group.Pages.FirstOrDefault(p => p.Model == groupLive)
            : null;
        int liveIdx = liveVm is not null ? group.Pages.IndexOf(liveVm) : -1;
        int nextIdx = liveIdx + 1;

        if (nextIdx < group.Pages.Count && nextIdx > 0)
            GoLiveFromGroup(group.Pages[nextIdx]);
        else
        {
            output.Clear();
            UpdateIsLiveFlags();
        }
    }
    else
    {
        var liveVm  = output.LivePage is not null
            ? Pages.FirstOrDefault(p => p.Model == output.LivePage)
            : null;
        int liveIdx = liveVm is not null ? Pages.IndexOf(liveVm) : -1;
        int nextIdx = liveIdx + 1;

        if (nextIdx < Pages.Count && nextIdx > 0)
        {
            SelectedPage = Pages[nextIdx];
            GoLive();
        }
        else
            ClearLive();
    }
}
```

- [ ] **Step 4: Inject `VideoEndedCallback` at all three `OutputState` creation sites**

**Site 1** — load show loop (around line 235), after `var state = new OutputState(cfg);`:

```csharp
var state = new OutputState(cfg);
state.VideoEndedCallback = () =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HandleVideoEnded(state));
```

**Site 2** — `AddOutput` method (around line 1363), after `var state = new OutputState(cfg);`:

```csharp
var state = new OutputState(cfg);
state.VideoEndedCallback = () =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HandleVideoEnded(state));
```

**Site 3** — `CreateDefaultShow` method (around lines 2660, 2669, 2678), after each `new OutputState(...)`:

```csharp
var progState = new OutputState(progConfig);
progState.VideoEndedCallback = () =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HandleVideoEnded(progState));

// ...

var monitorState = new OutputState(monitorConfig);
monitorState.VideoEndedCallback = () =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HandleVideoEnded(monitorState));

// ...

var ndiState = new OutputState(ndiConfig);
ndiState.VideoEndedCallback = () =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HandleVideoEnded(ndiState));
```

- [ ] **Step 5: Run the tests to verify they pass**

```
dotnet test ShowCast.Tests --filter "MainViewModelAdvanceOnEndTests" -v minimal
```

Expected: 6 tests pass.

- [ ] **Step 6: Run full test suite to check for regressions**

```
dotnet test ShowCast.Tests -v minimal
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MainViewModelAdvanceOnEndTests.cs
git commit -m "feat: inject VideoEndedCallback and implement HandleVideoEnded in MainViewModel"
```

---

### Task 4: Inspector UI

Add the 4th `ComboBoxItem` to the loop mode ComboBox in the inspector panel. Index 3 maps to `AdvanceOnEnd` automatically — no handler or load-path changes needed.

**Files:**
- Modify: `Views/EditorInspectorPanel.axaml`

- [ ] **Step 1: Add the 4th ComboBoxItem**

In `Views/EditorInspectorPanel.axaml`, find (around line 502–506):

```xml
<ComboBox x:Name="VideoLoopModeBox" SelectionChanged="OnVideoLoopModeChanged">
    <ComboBoxItem Content="Loop"/>
    <ComboBoxItem Content="Hold Last Frame"/>
    <ComboBoxItem Content="Go Black"/>
</ComboBox>
```

Replace with:

```xml
<ComboBox x:Name="VideoLoopModeBox" SelectionChanged="OnVideoLoopModeChanged">
    <ComboBoxItem Content="Loop"/>
    <ComboBoxItem Content="Hold Last Frame"/>
    <ComboBoxItem Content="Go Black"/>
    <ComboBoxItem Content="Advance on End"/>
</ComboBox>
```

- [ ] **Step 2: Build and run the app to verify the ComboBox shows 4 options**

```
dotnet run --project ShowCast
```

Open the inspector for a video layer. Confirm the Loop Mode ComboBox shows:
- Loop
- Hold Last Frame
- Go Black
- Advance on End

Select "Advance on End", save the show file, reopen — verify it persists as `"AdvanceOnEnd"` in the JSON.

- [ ] **Step 3: Commit**

```
git add Views/EditorInspectorPanel.axaml
git commit -m "feat: add Advance on End option to video loop mode inspector ComboBox"
```

---

## Self-Review

**Spec coverage:**

| Spec section | Task |
|---|---|
| `VideoLoopMode.AdvanceOnEnd` enum value | Task 1 Step 3 |
| `IVideoLayerPlayer.VideoEnded` property | Task 1 Step 4 |
| `VideoLayerPlayer` `AdvanceOnEnd` case in `OnEndReached` | Task 1 Step 5 |
| `VideoFrameRegistry.OnVideoEnded` + `UpdateSlide` wiring | Task 1 Step 6 |
| `OutputState.VideoEndedCallback` | Task 2 Step 1 |
| `OutputState.VideoRegistry` setter propagation | Task 2 Step 1 |
| `HandleVideoEnded` — rundown advance | Task 3 Step 3 |
| `HandleVideoEnded` — flat-view advance | Task 3 Step 3 |
| `HandleVideoEnded` — last page → go black | Task 3 Step 3 |
| Callback injection at output creation | Task 3 Step 4 |
| Inspector 4th ComboBoxItem | Task 4 Step 1 |
| Serialization (no changes needed) | N/A — `JsonStringEnumConverter` handles by name |

**Placeholder scan:** No TBDs, no "similar to above", no vague steps.

**Type consistency:** `VideoEnded` (Action?) consistent across `IVideoLayerPlayer`, `VideoLayerPlayer`, `FakePlayer`, and `VideoFrameRegistry.UpdateSlide`. `OnVideoEnded` (Action?) on `VideoFrameRegistry` consistent with `OutputState.VideoEndedCallback` (Action?).

**Edge case: `HandleVideoEnded` guard on `nextIdx > 0`:** The `nextIdx > 0` check ensures we don't advance when `liveIdx == -1` (live page not found in Pages collection) and `nextIdx` accidentally equals 0. This mirrors the intent of "live page not found → go black" from the spec's edge case for unknown output.
