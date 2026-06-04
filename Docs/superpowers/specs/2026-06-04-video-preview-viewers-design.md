# Design: Video Playback in Output Preview Viewers

**Date:** 2026-06-04  
**Status:** Approved

## Problem

The right-side output preview viewers (`WebView2PreviewControl`) render animated page thumbnails but show a `[ Video ]` placeholder for video layers. The `OutputWindow` (fullscreen broadcast) already decodes and renders live video frames via `VideoFrameRegistry`. The preview should share those frames at zero additional decode cost.

## Constraints

- No second video decoder. Previews share frames already decoded by `OutputWindow`.
- If `OutputWindow` is not open, preview shows `[ Video ]` placeholder — no fallback decoder.
- No audio in preview viewers.
- Video during a page-to-page transition is not required (existing behavior in `OutputWindow` as well).

## Architecture

### Approach chosen: Registry reference on OutputState (Option A)

`OutputState` is already the shared data model between `OutputWindow` and `WebView2PreviewControl`. Adding a nullable `VideoFrameRegistry?` property to it is the minimal bridge — no new services, events, or coordination layers.

`OutputWindow` owns the registry lifecycle. `OutputState` holds a reference to it only while the window is open.

---

## Changes

### 1. `Core/OutputState.cs`

Add one reactive property:

```csharp
private VideoFrameRegistry? _videoRegistry;
public VideoFrameRegistry? VideoRegistry
{
    get => _videoRegistry;
    set => this.RaiseAndSetIfChanged(ref _videoRegistry, value);
}
```

### 2. `Views/OutputWindow.axaml.cs`

**Constructor** — after creating `_videoRegistry`, publish it on the shared state:
```csharp
_videoRegistry = new VideoFrameRegistry(audioDestinations, ndiLookup: ndiLookup);
output.VideoRegistry = _videoRegistry;
```

**`OnClosed()`** — clear the reference *before* disposing, so no reader can call `TryGetFrame` on a disposed registry:
```csharp
_output.VideoRegistry = null;
_videoRegistry?.Dispose();
```

### 3. `Views/WebView2PreviewControl.cs`

**a) Subscribe to `VideoRegistry` changes**

In `OnPropertyChanged` for `OutputProperty`, alongside the existing `LivePage` subscription:
```csharp
_subs.Add(_currentOutput.WhenAnyValue(o => o.VideoRegistry).Subscribe(_ =>
    StartTimerIfNeeded(_currentPage)));
```

When a `VideoRegistry` appears (output window opens) the timer starts if video layers are present. When it clears (output window closes) the timer stops naturally on the next tick that finds no animations or video.

**b) `StartTimerIfNeeded` — keep timer alive for video layers**

Add a `HasVideoLayers` check:
```csharp
static bool HasVideoLayers(Page? page) =>
    page?.Layers.Any(l => l.Type == LayerType.Video && !string.IsNullOrEmpty(l.AssetPath)) == true;
```

```csharp
void StartTimerIfNeeded(Page? page)
{
    bool hasAnims = page?.Layers.Any(l =>
        l.EntryAnim != LayerAnimation.None ||
        (l.ExitAnim != LayerExitAnimation.None && l.HoldDurationMs > 0)) == true;
    bool hasVideo = _currentOutput?.VideoRegistry is not null && HasVideoLayers(page);

    if (hasAnims || hasVideo)
        { if (!_timer.IsEnabled) _timer.Start(); }
    else
        RenderStatic(page);
}
```

**c) `OnTick` — keep timer alive while video is playing**

After the animation check, before stopping the timer:
```csharp
bool hasVideo = _currentOutput?.VideoRegistry is not null && HasVideoLayers(_currentPage);

if (animating || hasVideo)
    { RenderAnimFrame(elapsed); return; }

_timer.Stop();
RenderAnimFrame(elapsed);
```

**d) `RenderAnimFrame` and `RenderStatic` — pass `getVideoFrame`**

```csharp
void RenderAnimFrame(double elapsed)
{
    using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888));
    if (_currentPage is not null)
        PageRenderer.Render(surface.Canvas, _currentPage, Roles, W, H, elapsed,
                            getVideoFrame: _currentOutput?.VideoRegistry?.TryGetFrame);
    else
        surface.Canvas.Clear(SKColors.Black);
    _img.Source = ToWriteableBitmap(surface);
}

void RenderStatic(Page? page)
{
    using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888));
    if (page is not null)
        PageRenderer.Render(surface.Canvas, page, Roles, W, H,
                            getVideoFrame: _currentOutput?.VideoRegistry?.TryGetFrame);
    else
        surface.Canvas.Clear(SKColors.Black);
    _img.Source = ToWriteableBitmap(surface);
}
```

`RenderTransition` does not need to change — `OutputWindow` also does not pass video frames during transitions.

---

## Data Flow

```
OutputWindow (opens)
  └─ creates VideoFrameRegistry
  └─ sets OutputState.VideoRegistry = registry
  └─ subscribes to LivePage → calls registry.UpdateSlide()
       └─ starts VideoLayerPlayer per video layer
            └─ LibVLC decodes → player.CurrentFrame (volatile SKImage)

WebView2PreviewControl (per preview card)
  └─ subscribes to OutputState.VideoRegistry → starts DispatcherTimer
  └─ DispatcherTimer (~16ms) → RenderAnimFrame()
       └─ PageRenderer.Render(..., getVideoFrame: registry.TryGetFrame)
            └─ TryGetFrame(layerId) → player.CurrentFrame (read-only, no lock)
                 └─ DrawSKImageInRect() → rendered at 320×180

OutputWindow (closes)
  └─ sets OutputState.VideoRegistry = null  ← preview sees null, stops timer
  └─ disposes VideoFrameRegistry            ← stops VideoLayerPlayers
```

## Threading

`VideoLayerPlayer.CurrentFrame` is a `volatile` field — reads from the UI timer thread are safe without locks. `VideoFrameRegistry.TryGetFrame` only reads from `_players` dictionary; the dictionary is only mutated on the UI thread (via `UpdateSlide` called from the `LivePage` subscription, which runs on the dispatcher). No locking needed.

## Non-goals

- Audio in the preview viewers
- Video playback during page-to-page transitions in the preview
- Fallback decode when `OutputWindow` is closed
- Frame rate throttling for the preview (it runs at the same ~60 fps as today)
