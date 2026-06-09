# Video Layer "Advance on End" Loop Mode Design

**Date:** 2026-06-09
**Status:** Approved

## Overview

Add a 4th `VideoLoopMode` value — `AdvanceOnEnd` — that automatically advances to the next slide when the video finishes playing. If the page is already the last in its package/rundown group, the output goes black instead.

---

## Section 1: Core Layer

### `Core/SlideLayer.cs`

Add `AdvanceOnEnd` as the 4th enum value:

```csharp
public enum VideoLoopMode { Loop, HoldLastFrame, GoBlack, AdvanceOnEnd }
```

### `Core/IVideoLayerPlayer.cs`

Add one property:

```csharp
Action? VideoEnded { get; set; }
```

Called from the threadpool when the video ends with `AdvanceOnEnd` mode. The callback is responsible for its own UI-thread dispatch (set by `MainViewModel`).

### `Core/VideoLayerPlayer.cs`

Add `AdvanceOnEnd` case in `OnEndReached` (already running on threadpool via `QueueUserWorkItem`):

```csharp
case VideoLoopMode.AdvanceOnEnd:
    _player.Stop();
    VideoEnded?.Invoke();
    break;
```

No Avalonia reference in the Core layer — UI-thread dispatch is the caller's responsibility.

### `Core/VideoFrameRegistry.cs`

Add `Action? OnVideoEnded` property. In `UpdateSlide`, when starting a new player whose layer has `VideoLoopMode.AdvanceOnEnd`, assign:

```csharp
player.VideoEnded = OnVideoEnded;
```

---

## Section 2: OutputState Wiring

### `Core/OutputState.cs`

Add `Action? VideoEndedCallback` property. Expand the `VideoRegistry` setter to propagate the callback into any newly-assigned registry:

```csharp
public Action? VideoEndedCallback { get; set; }

public VideoFrameRegistry? VideoRegistry
{
    get => _videoRegistry;
    set
    {
        this.RaiseAndSetIfChanged(ref _videoRegistry, value);
        if (value is not null) value.OnVideoEnded = VideoEndedCallback;
    }
}
```

`VideoEndedCallback` is always set by `MainViewModel` immediately after creating each `OutputState` — before any sender ever assigns a registry — so the propagation order is guaranteed.

---

## Section 3: MainViewModel Wiring

### Callback injection (at output-state creation)

```csharp
outputState.VideoEndedCallback = () =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => HandleVideoEnded(outputState));
```

### `HandleVideoEnded(OutputState output)`

Mirrors the non-looping advance path already in `_pageTimer.Elapsed`:

- **Rundown view:** Find the group whose `SelectedOutput == output`. Advance to the next page within that group (same logic as the rundown timer path). If already at the last page of the group, call `output.Clear()` + `UpdateIsLiveFlags()` (go black).
- **Flat view:** Find the live page index in `Pages`, advance to `Pages[liveIdx + 1]` (same as the flat-view timer path). If already at the last page, call `ClearLive()` (go black). Do NOT use `GoLiveAndAdvance()` — that method is selection-aware and would fire the operator's pre-cued page rather than the page after the live one.

`HandleVideoEnded` is only called on the UI thread (via `InvokeAsync`).

---

## Section 4: Inspector UI

### `Views/EditorInspectorPanel.axaml`

Add a 4th `ComboBoxItem` to `VideoLoopModeBox`:

```xml
<ComboBoxItem Content="Loop"/>
<ComboBoxItem Content="Hold Last Frame"/>
<ComboBoxItem Content="Go Black"/>
<ComboBoxItem Content="Advance on End"/>
```

Index 3 maps to `AdvanceOnEnd` automatically. No changes needed in the handler or load path.

---

## Section 5: Serialization

No changes required. `JsonStringEnumConverter` serializes by name (`"AdvanceOnEnd"`). Existing show files without this value parse fine — missing enum member defaults to `Loop` (zero value).

---

## Edge Cases

- **Last page in group/flat view:** go black (`output.Clear()` / `ClearLive()`).
- **Multiple video layers on one page:** `VideoFrameRegistry` stores one `OnVideoEnded` callback shared across all layers. The first layer to end triggers advance. This is acceptable — pages with multiple video layers and `AdvanceOnEnd` are an unusual configuration.
- **AdvanceOnEnd + DurationMs both set:** Both the page timer and the video end event can fire. Whichever fires first calls advance; the second is a no-op (page has already changed, `_livePageVm` is null for countdown, `output.LivePage` is already different for `HandleVideoEnded`).
- **Countdown bar:** The 100ms bar tick already handles `AdvanceOnEnd` pages — bar drains from 1.0 to 0 as the video plays. When the video ends and advance fires, the bar disappears with the page change.
- **`GoLiveAndAdvance` at last page:** The method already returns early when there's no next page. `HandleVideoEnded` calls `ClearLive()` in that case.
