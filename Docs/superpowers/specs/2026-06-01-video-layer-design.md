# Video Layer Design Spec

**Date:** 2026-06-01
**Branch:** `feature/video-layer` (cut from `master`, merged back via PR after testing)
**Goal:** Add a `LayerType.Video` slide layer that auto-plays a video file on live output, composited into the existing Skia pipeline alongside other layers.

---

## Architecture Overview

Video frames are decoded continuously by LibVLC using its software callback API. Each live output owns a `VideoFrameRegistry` that manages one `VideoLayerPlayer` per active video layer. When `PageRenderer.Render()` is called, it receives the registry's current frame dictionary and draws each video layer's latest bitmap — identical in compositing behavior to Image layers. Video only plays on live outputs (`OutputWindow`, `NdiSender`); the editor canvas shows a static placeholder.

---

## Section 1: Data Model

### `LayerType` (in `Core/SlideLayer.cs`)
Add `Video` to the existing enum:
```csharp
public enum LayerType { Background, Text, Image, Shape, Clock, Feed, Video }
```

### New `SlideLayer` properties
```csharp
public VideoLoopMode VideoLoopMode           { get; set; } = VideoLoopMode.Loop;
public float         VideoVolume             { get; set; } = 1.0f;
public Guid?         VideoAudioDestinationId { get; set; } = null;
```

- `AssetPath` (already on `SlideLayer`) stores the **filename only**, resolved against `AppFolders.Video` at runtime.
- `Clone()` copies all three new fields.
- `ShowFileSerializer` picks them up automatically via `System.Text.Json`.

### `VideoLoopMode` enum (new, in `Core/SlideLayer.cs` or its own file)
```csharp
public enum VideoLoopMode { Loop, HoldLastFrame, GoBlack }
```

### `AppFolders` (in `Core/AppFolders.cs`)
Add a `Video` property alongside `Media`:
```csharp
public static string Video { get; private set; } = "";
// In EnsureCreated():
Video = Path.Combine(Root, "Video");
Directory.CreateDirectory(Video);
```

### Supported import extensions
`.mp4, .mov, .avi, .mkv, .wmv, .webm, .m4v, .av1`

LibVLC 3.x decodes AV1 via the dav1d decoder.

---

## Section 2: Decoder / Player Pipeline

### `IVideoLayerPlayer` interface (new, in `Core/`)
Extracted interface to allow faking in tests:
```csharp
public interface IVideoLayerPlayer : IDisposable
{
    SKBitmap? CurrentFrame { get; }
    void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId);
    void Stop();
}
```

### `VideoLayerPlayer` (new, in `Core/`)
Wraps a LibVLC `MediaPlayer` using the software callback API:

- **`SetVideoFormat`** — called by LibVLC to negotiate pixel format. Specifies BGRA (matches SkiaSharp's native format on Windows). Allocates a pinned `byte[]` buffer of `width × height × 4` bytes and a `GCHandle` to pin it.
- **`LockCallback`** — LibVLC calls before decoding a frame; returns the pointer to the pinned buffer.
- **`UnlockCallback`** — LibVLC calls after decoding; copies the buffer into `_currentFrame` under a `lock`.
- **`DisplayCallback`** — no-op; `CurrentFrame` is already updated in `UnlockCallback`.
- **`EndReached` event handling:**
  - `Loop` → restart playback (stop + play on a thread pool thread to avoid VLC deadlock)
  - `HoldLastFrame` → do nothing; last bitmap stays in `_currentFrame`
  - `GoBlack` → null out `_currentFrame` under the lock
- **Audio routing** — before `Play()`, configure LibVLC's audio output module with the `audioDeviceId` (WASAPI device ID from `AudioDestination.DeviceId`); `null` = OS default.
- `Dispose()` releases the `MediaPlayer`, `Media`, and unpins the `GCHandle`.

### `VideoFrameRegistry` (new, in `Core/`)
One instance per live output. Owns a `Dictionary<Guid, IVideoLayerPlayer>` keyed by layer ID.

```csharp
public void UpdateSlide(Page? page);           // diff old vs new video layers
public SKBitmap? TryGetFrame(Guid layerId);
public IReadOnlyDictionary<Guid, SKBitmap?> Frames { get; }
void Dispose();
```

`VideoFrameRegistry` takes `IReadOnlyList<AudioDestination> destinations` as a constructor parameter (passed in by the output window from `AppSettings.AudioDestinations`). This lets it resolve `layer.VideoAudioDestinationId → DeviceId` string without depending on the global `AppSettings` directly.

`UpdateSlide` logic:
1. Collect video layer IDs from the new page (or empty set if null).
2. Stop and remove players for IDs no longer present.
3. Start new players for IDs not yet tracked; resolve audio device ID from `destinations` at this point.
4. If `VideoAudioDestinationId` is null or not found in `destinations`, pass `null` to `VideoLayerPlayer.Start()` (OS default).

### `PageRenderer` changes (in `Engine/PageRenderer.cs`)
Add optional parameter to `Render()`:
```csharp
public static void Render(SKCanvas canvas, Page page, LayerRole roleFilter,
    int canvasWidth, int canvasHeight,
    double elapsedMs = -1.0, double exitElapsedMs = -1.0,
    bool useLiveTimers = true,
    IReadOnlyDictionary<Guid, SKBitmap?>? videoFrames = null)
```

New switch case:
```csharp
case LayerType.Video:
    if (videoFrames?.TryGetValue(layer.Id, out var frame) == true && frame is not null)
        DrawImage(canvas, frame, rect, layer.ImageFit);  // reuse existing image draw helper
    break;
```

> **Implementation note:** If the existing `case LayerType.Image:` rendering is inline rather than a named helper, extract it into a `static void DrawImage(SKCanvas, SKBitmap, SKRect, ImageFit)` method first, then call it from both cases.

---

## Section 3: Output Wiring

### `OutputWindow` (in `Views/OutputWindow.axaml.cs`)
- Owns a `VideoFrameRegistry _videoRegistry`.
- Subscribes to `OutputState.CurrentPageChanged` (or equivalent); on change calls `_videoRegistry.UpdateSlide(newPage)`.
- Passes `_videoRegistry.Frames` to `PageRenderer.Render()`.
- Disposes registry on window close.

### `NdiSender` (in `Core/NdiSender.cs`)
- Same pattern: owns a `VideoFrameRegistry`, updates on slide change, passes frames to `PageRenderer.Render()`.

### `ProgramViewport` (in `Views/ProgramViewport.axaml.cs`)
- Intentionally excluded — video does not play in the in-app program monitor.

---

## Section 4: Inspector UI

### `EditorInspectorPanel` — new VIDEO section
Visible only when selected layer is `LayerType.Video`. Follows the same collapsible-section pattern as ANIMATION, TIMING, etc.

Controls:
| Control | Binding |
|---------|---------|
| File label + "Browse..." button | `layer.AssetPath`; picker copies file to `AppFolders.Video` |
| Loop Mode dropdown | `layer.VideoLoopMode` (Loop / Hold Last Frame / Go Black) |
| Volume slider 0–100% | `layer.VideoVolume` |
| Audio Output dropdown | `layer.VideoAudioDestinationId`; populated from `AppSettings.AudioDestinations`; first item = "Default (OS)" |

### `EditorLayerPanel` — add-layer list
Add **Video** as a new option in the layer type picker, alongside Background, Text, Image, Shape, etc.

### Editor canvas placeholder
A Video layer at edit time renders a dark rectangle with a film-frame icon and the filename centered. No LibVLC instance is created in the editor.

---

## Section 5: Testing

| Test file | What it covers |
|-----------|----------------|
| `ShowCast.Tests/Core/ShowFileSerializerTests.cs` | Round-trip serialization of `VideoLoopMode`, `VideoVolume`, `VideoAudioDestinationId` |
| `ShowCast.Tests/Core/VideoFrameRegistryTests.cs` | `UpdateSlide` diff: players started for new video layers, stopped for removed ones; uses `IVideoLayerPlayer` fake |

No LibVLC integration tests (consistent with audio player test policy).

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `Core/SlideLayer.cs` | Add `LayerType.Video`, `VideoLoopMode` enum, 3 new props, update `Clone()` |
| Modify | `Core/AppFolders.cs` | Add `Video` folder |
| Create | `Core/IVideoLayerPlayer.cs` | Interface for testability |
| Create | `Core/VideoLayerPlayer.cs` | LibVLC software-callback decoder |
| Create | `Core/VideoFrameRegistry.cs` | Per-output player manager |
| Modify | `Engine/PageRenderer.cs` | `LayerType.Video` case + `videoFrames` param |
| Modify | `Views/OutputWindow.axaml.cs` | Own `VideoFrameRegistry`, wire slide changes |
| Modify | `NDI/NdiSender.cs` | Own `VideoFrameRegistry`, wire slide changes |
| Modify | `Views/EditorInspectorPanel.axaml(.cs)` | VIDEO collapsible section |
| Modify | `Views/EditorLayerPanel.axaml(.cs)` | Video option in add-layer list |
| Modify | `ShowCast.Tests/Core/ShowFileSerializerTests.cs` | Video property round-trip |
| Create | `ShowCast.Tests/Core/VideoFrameRegistryTests.cs` | Registry diff logic tests |
