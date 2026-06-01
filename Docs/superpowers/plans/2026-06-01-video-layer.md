# Video Layer Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `LayerType.Video` — a slide layer that auto-plays a video file on live output, composited into the Skia pipeline alongside other layers.

**Architecture:** LibVLC's software callback API decodes video frames into a pinned `byte[]`, which is copied under a lock into an `SKBitmap`. A `VideoFrameRegistry` (one per live output) manages per-layer `VideoLayerPlayer` instances, starting/stopping them as slides change. `PageRenderer.Render()` accepts an optional `Func<Guid, SKBitmap?>? getVideoFrame` parameter and draws live frames or an editor placeholder. Video plays only on `OutputWindow` and `NdiSender`; the editor canvas shows a static dark placeholder.

**Tech Stack:** .NET 9, Avalonia 11.2.2, LibVLCSharp 3.9.0 / VideoLAN.LibVLC.Windows 3.x, SkiaSharp 2.88.9, xUnit

**Branch:** `feature/video-layer` cut from `master`. Merge back via PR after testing.

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `Core/SlideLayer.cs` | Add `LayerType.Video`, `VideoLoopMode` enum, 3 new props, update `Clone()` |
| Modify | `Core/AppFolders.cs` | Add `Video` folder |
| Create | `Core/IVideoLayerPlayer.cs` | Interface for testability |
| Create | `Core/VideoLayerPlayer.cs` | LibVLC software-callback decoder |
| Create | `Core/VideoFrameRegistry.cs` | Per-output player manager |
| Modify | `Engine/PageRenderer.cs` | Extract `DrawBitmapInRect`, add `getVideoFrame` param, `LayerType.Video` case |
| Modify | `Views/OutputWindow.axaml.cs` | Own `VideoFrameRegistry`; wire slide changes; pass frames to renderer |
| Modify | `Core/NdiSender.cs` | Own `VideoFrameRegistry`; update constructor; pass frames to renderer |
| Modify | `Views/MainWindow.axaml.cs` | Pass `AudioDestinations` to `OutputWindow` constructor (2 sites) |
| Modify | `ViewModels/MainViewModel.cs` | Pass `AudioDestinations` to `NdiSender` constructor; add `AddVideoLayer()` |
| Modify | `Views/EditorInspectorPanel.axaml` | Add VIDEO collapsible section |
| Modify | `Views/EditorInspectorPanel.axaml.cs` | Wire VIDEO section: `LoadLayer`, `FlushVideoLayerFields`, handlers |
| Modify | `Views/PageEditorOverlay.axaml` | Add `+ Video` button to toolbar |
| Modify | `Views/PageEditorOverlay.axaml.cs` | Add `OnAddVideo` handler |
| Modify | `Views/EditorLayerPanel.cs` | Add `Video` → `"VID"` to `TypeBadge` converter |
| Modify | `ShowCast.Tests/Core/ShowFileSerializerTests.cs` | Round-trip test for video properties |
| Create | `ShowCast.Tests/Core/VideoFrameRegistryTests.cs` | Registry diff logic tests |

---

## Task 1: Create the feature branch

**Files:** none (git only)

- [ ] **Create and switch to the feature branch**

```bash
git checkout -b feature/video-layer
```

Expected: `Switched to a new branch 'feature/video-layer'`

---

## Task 2: Add `AppFolders.Video`

**Files:**
- Modify: `Core/AppFolders.cs`

- [ ] **Add the `Video` property and `Directory.CreateDirectory` call**

In `Core/AppFolders.cs`, add `Video` alongside `Media`:

```csharp
public static string Video { get; private set; } = "";
```

And in `EnsureCreated()`, after the `Media` line:

```csharp
Video = Path.Combine(Root, "Video");
Directory.CreateDirectory(Video);
```

The full `EnsureCreated()` after the change:

```csharp
public static void EnsureCreated()
{
    Root          = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShowCast");
    Configuration = Path.Combine(Root, "Configuration");
    Libraries     = Path.Combine(Root, "Libraries");
    Playlists     = Path.Combine(Root, "Playlists");
    Media         = Path.Combine(Root, "Media");
    Video         = Path.Combine(Root, "Video");

    Directory.CreateDirectory(Configuration);
    Directory.CreateDirectory(Libraries);
    Directory.CreateDirectory(Playlists);
    Directory.CreateDirectory(Media);
    Directory.CreateDirectory(Video);

    Directory.CreateDirectory(Path.Combine(Libraries, "Default"));
}
```

- [ ] **Build to verify no errors**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

- [ ] **Commit**

```bash
git add Core/AppFolders.cs
git commit -m "feat(video): add AppFolders.Video folder"
```

---

## Task 3: Add `VideoLoopMode` enum and video properties to `SlideLayer`

**Files:**
- Modify: `Core/SlideLayer.cs`

- [ ] **Add `Video` to `LayerType` and add `VideoLoopMode` enum**

In `Core/SlideLayer.cs`, change the `LayerType` enum:

```csharp
public enum LayerType { Background, Text, Image, Shape, Clock, Feed, Video }
```

Add the new enum directly after `LayerType`:

```csharp
public enum VideoLoopMode { Loop, HoldLastFrame, GoBlack }
```

- [ ] **Add three new properties to `SlideLayer`**

Add after the `ImageFit` property (line 74):

```csharp
// ── Video ──────────────────────────────────────────────────────────────────
public VideoLoopMode VideoLoopMode           { get; set; } = VideoLoopMode.Loop;
public float         VideoVolume             { get; set; } = 1.0f;
public Guid?         VideoAudioDestinationId { get; set; } = null;
```

- [ ] **Update `Clone()` to copy the three new fields**

Add to the `Clone()` return initializer (after `ExitEasing = ExitEasing`):

```csharp
VideoLoopMode           = VideoLoopMode,
VideoVolume             = VideoVolume,
VideoAudioDestinationId = VideoAudioDestinationId,
```

- [ ] **Build to verify**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

- [ ] **Commit**

```bash
git add Core/SlideLayer.cs
git commit -m "feat(video): add VideoLoopMode enum and video properties to SlideLayer"
```

---

## Task 4: Serialization round-trip test for video properties

**Files:**
- Modify: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Write the failing test**

Add at the end of `ShowFileSerializerTests`:

```csharp
[Fact]
public async Task SlideLayer_VideoProperties_RoundTripThroughShowFile()
{
    // Arrange
    var destId = Guid.NewGuid();
    var layer = new SlideLayer
    {
        Type                    = LayerType.Video,
        AssetPath               = "clip.mp4",
        VideoLoopMode           = VideoLoopMode.HoldLastFrame,
        VideoVolume             = 0.75f,
        VideoAudioDestinationId = destId,
    };
    var show = new Show();
    var page = new Page();
    page.AddLayer(layer);
    show.Pages.Add(page);
    var file = new ShowFile { Shows = new List<Show> { show } };

    var path = Path.GetTempFileName();
    try
    {
        // Act
        var options = ShowFileSerializer.CreateSerializerOptions();
        var json    = System.Text.Json.JsonSerializer.Serialize(file, options);
        await File.WriteAllTextAsync(path, json);
        var result = await ShowFileSerializer.LoadAsync(path);

        // Assert
        Assert.NotNull(result);
        var loaded = result.ShowFile.Shows[0].Pages[0].Layers[0];
        Assert.Equal(LayerType.Video,            loaded.Type);
        Assert.Equal("clip.mp4",                 loaded.AssetPath);
        Assert.Equal(VideoLoopMode.HoldLastFrame, loaded.VideoLoopMode);
        Assert.Equal(0.75f,                       loaded.VideoVolume, precision: 4);
        Assert.Equal(destId,                      loaded.VideoAudioDestinationId);
    }
    finally { File.Delete(path); }
}
```

- [ ] **Run the test to confirm it compiles and fails for the right reason, or passes**

```
dotnet test ShowCast.Tests --filter "SlideLayer_VideoProperties_RoundTripThroughShowFile" --logger "console;verbosity=detailed"
```

Expected: PASS (System.Text.Json serializes new properties automatically; no migration needed since the properties have defaults).

- [ ] **Commit**

```bash
git add ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "test(video): round-trip serialization of VideoLoopMode, VideoVolume, VideoAudioDestinationId"
```

---

## Task 5: `IVideoLayerPlayer` interface

**Files:**
- Create: `Core/IVideoLayerPlayer.cs`

- [ ] **Create the interface**

```csharp
using System;
using SkiaSharp;

namespace ShowCast.Core;

public interface IVideoLayerPlayer : IDisposable
{
    SKBitmap? CurrentFrame { get; }
    void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId);
    void Stop();
}
```

- [ ] **Build**

```
dotnet build ShowCast.csproj
```

- [ ] **Commit**

```bash
git add Core/IVideoLayerPlayer.cs
git commit -m "feat(video): add IVideoLayerPlayer interface"
```

---

## Task 6: `VideoLayerPlayer` — LibVLC software-callback decoder

**Files:**
- Create: `Core/VideoLayerPlayer.cs`

- [ ] **Create the implementation**

```csharp
using System;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Decodes one video file into a continuously-updated <see cref="CurrentFrame"/> SKBitmap
/// using LibVLC's software callback API. Thread-safe: frame is read on the render thread,
/// written on LibVLC's decode thread.
/// </summary>
public sealed class VideoLayerPlayer : IVideoLayerPlayer
{
    readonly LibVLC      _libVlc;
    readonly MediaPlayer _player;

    byte[]?  _frameBuffer;
    GCHandle _pin;
    uint     _frameWidth;
    uint     _frameHeight;

    readonly object _frameLock = new();
    SKBitmap?       _currentFrame;
    VideoLoopMode   _loopMode;

    // Managed delegate fields — must stay rooted while VLC holds unmanaged pointers.
    readonly LibVLC.VideoFormatCallback  _fmtCb;
    readonly LibVLC.VideoCleanupCallback _cleanupCb;
    readonly LibVLC.VideoLockCallback    _lockCb;
    readonly LibVLC.VideoUnlockCallback  _unlockCb;
    readonly LibVLC.VideoDisplayCallback _displayCb;

    public SKBitmap? CurrentFrame
    {
        get { lock (_frameLock) return _currentFrame; }
    }

    public VideoLayerPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC();
        _player  = new MediaPlayer(_libVlc);

        _fmtCb     = OnVideoFormat;
        _cleanupCb = OnVideoCleanup;
        _lockCb    = OnVideoLock;
        _unlockCb  = OnVideoUnlock;
        _displayCb = OnVideoDisplay;

        _player.SetVideoFormatCallbacks(_fmtCb, _cleanupCb);
        _player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
        _player.EndReached += OnEndReached;
    }

    uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
                       ref uint pitches, ref uint lines)
    {
        // Tell VLC to output BGRA (matches SkiaSharp native on Windows).
        Marshal.WriteByte(chroma, 0, (byte)'B');
        Marshal.WriteByte(chroma, 1, (byte)'G');
        Marshal.WriteByte(chroma, 2, (byte)'R');
        Marshal.WriteByte(chroma, 3, (byte)'A');

        _frameWidth  = width;
        _frameHeight = height;
        pitches      = width * 4;
        lines        = height;

        if (_pin.IsAllocated) _pin.Free();
        _frameBuffer = new byte[pitches * lines];
        _pin         = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);

        return 1; // one picture buffer
    }

    void OnVideoCleanup(IntPtr opaque)
    {
        if (_pin.IsAllocated) _pin.Free();
        _frameBuffer = null;
    }

    IntPtr OnVideoLock(IntPtr opaque, ref IntPtr planes)
    {
        planes = _pin.IsAllocated ? _pin.AddrOfPinnedObject() : IntPtr.Zero;
        return IntPtr.Zero; // picture handle (unused by VLC when opaque is null)
    }

    void OnVideoUnlock(IntPtr opaque, IntPtr picture, ref IntPtr planes)
    {
        if (_frameBuffer is null || !_pin.IsAllocated) return;

        var info   = new SKImageInfo((int)_frameWidth, (int)_frameHeight,
                                     SKColorType.Bgra8888, SKAlphaType.Premul);
        var newBmp = new SKBitmap(info);
        Marshal.Copy(_frameBuffer, 0, newBmp.GetPixels(), _frameBuffer.Length);

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = newBmp;
        }
    }

    void OnVideoDisplay(IntPtr opaque, IntPtr picture) { }

    void OnEndReached(object? sender, EventArgs e)
    {
        // Calling Stop/Play directly on the VLC event thread causes a deadlock.
        // Queue to thread pool so we return from the event handler first.
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            switch (_loopMode)
            {
                case VideoLoopMode.Loop:
                    _player.Stop();
                    _player.Play();
                    break;
                case VideoLoopMode.GoBlack:
                    lock (_frameLock)
                    {
                        _currentFrame?.Dispose();
                        _currentFrame = null;
                    }
                    break;
                // HoldLastFrame: do nothing — last decoded frame stays in _currentFrame.
            }
        });
    }

    public void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId)
    {
        _loopMode = loopMode;

        if (!string.IsNullOrEmpty(audioDeviceId))
            _player.SetOutputDevice("mmdevice", audioDeviceId);

        using var media = new Media(_libVlc, filePath);
        _player.Media  = media;
        _player.Volume = (int)(volume * 100);
        _player.Play();
    }

    public void Stop()
    {
        _player.Stop();
        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }
    }

    public void Dispose()
    {
        _player.EndReached -= OnEndReached;
        try { _player.Stop(); } catch { }
        _player.Dispose();
        _libVlc.Dispose();
        if (_pin.IsAllocated) _pin.Free();
        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }
    }
}
```

- [ ] **Build**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

If you get "The type or namespace name `LibVLC.VideoFormatCallback` does not exist", the delegate types in LibVLCSharp 3.9.0 may be nested differently. Open the NuGet source or use IDE Go-to-Definition on `_player.SetVideoFormatCallbacks` to find the exact delegate types and update the field declarations and assignments accordingly.

- [ ] **Commit**

```bash
git add Core/VideoLayerPlayer.cs
git commit -m "feat(video): add VideoLayerPlayer with LibVLC software callback decoding"
```

---

## Task 7: `VideoFrameRegistry` and tests

**Files:**
- Create: `Core/VideoFrameRegistry.cs`
- Create: `ShowCast.Tests/Core/VideoFrameRegistryTests.cs`

- [ ] **Write the failing tests first**

Create `ShowCast.Tests/Core/VideoFrameRegistryTests.cs`:

```csharp
using System;
using System.Collections.Generic;
using ShowCast.Core;
using SkiaSharp;
using Xunit;

namespace ShowCast.Tests.Core;

file sealed class FakePlayer : IVideoLayerPlayer
{
    public bool   Started      { get; private set; }
    public bool   Disposed     { get; private set; }
    public string? StartedPath { get; private set; }
    public SKBitmap? CurrentFrame { get; set; }

    public void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId)
    {
        Started     = true;
        StartedPath = filePath;
    }
    public void Stop()    { }
    public void Dispose() => Disposed = true;
}

public class VideoFrameRegistryTests
{
    static Page MakeVideoPage(params (Guid id, string path)[] layers)
    {
        var page = new Page();
        foreach (var (id, path) in layers)
            page.AddLayer(new SlideLayer { Id = id, Type = LayerType.Video, AssetPath = path });
        return page;
    }

    [Fact]
    public void UpdateSlide_StartsPlayerForEachVideoLayer()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        var id = Guid.NewGuid();
        registry.UpdateSlide(MakeVideoPage((id, "clip.mp4")));

        Assert.Single(players);
        Assert.True(players[0].Started);
    }

    [Fact]
    public void UpdateSlide_NullPage_StopsAllPlayers()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        registry.UpdateSlide(MakeVideoPage((Guid.NewGuid(), "clip.mp4")));
        registry.UpdateSlide(null);

        Assert.True(players[0].Disposed);
    }

    [Fact]
    public void UpdateSlide_LayerRemoved_DisposesItsPlayer()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        var keepId   = Guid.NewGuid();
        var removeId = Guid.NewGuid();

        registry.UpdateSlide(MakeVideoPage((keepId, "a.mp4"), (removeId, "b.mp4")));
        registry.UpdateSlide(MakeVideoPage((keepId, "a.mp4")));

        // Two players created; player for removeId is disposed, player for keepId is not
        Assert.Equal(2, players.Count);
        var removedPlayer = players.First(p => p.StartedPath!.EndsWith("b.mp4"));
        var keptPlayer    = players.First(p => p.StartedPath!.EndsWith("a.mp4"));
        Assert.True(removedPlayer.Disposed);
        Assert.False(keptPlayer.Disposed);
    }

    [Fact]
    public void UpdateSlide_SameLayerIdTwice_DoesNotCreateDuplicatePlayer()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        var id = Guid.NewGuid();
        registry.UpdateSlide(MakeVideoPage((id, "clip.mp4")));
        registry.UpdateSlide(MakeVideoPage((id, "clip.mp4")));

        Assert.Single(players);
    }

    [Fact]
    public void TryGetFrame_ReturnsNullWhenNoPlayer()
    {
        var registry = new VideoFrameRegistry(new List<AudioDestination>());
        Assert.Null(registry.TryGetFrame(Guid.NewGuid()));
    }

    [Fact]
    public void Dispose_DisposesAllPlayers()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        registry.UpdateSlide(MakeVideoPage((Guid.NewGuid(), "a.mp4"), (Guid.NewGuid(), "b.mp4")));
        registry.Dispose();

        Assert.All(players, p => Assert.True(p.Disposed));
    }
}
```

- [ ] **Run to confirm they fail (VideoFrameRegistry doesn't exist yet)**

```
dotnet test ShowCast.Tests --filter "VideoFrameRegistryTests" --logger "console;verbosity=detailed"
```

Expected: compile error — `VideoFrameRegistry` not found.

- [ ] **Implement `VideoFrameRegistry`**

Create `Core/VideoFrameRegistry.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Manages one <see cref="IVideoLayerPlayer"/> per active video layer for a single live output.
/// Call <see cref="UpdateSlide"/> when the live page changes; read frames via <see cref="TryGetFrame"/>.
/// </summary>
public sealed class VideoFrameRegistry : IDisposable
{
    readonly IReadOnlyList<AudioDestination> _destinations;
    readonly Func<IVideoLayerPlayer>         _playerFactory;
    readonly Dictionary<Guid, IVideoLayerPlayer> _players = new();

    public VideoFrameRegistry(IReadOnlyList<AudioDestination> destinations,
                              Func<IVideoLayerPlayer>? playerFactory = null)
    {
        _destinations  = destinations;
        _playerFactory = playerFactory ?? (() => new VideoLayerPlayer());
    }

    /// <summary>
    /// Diffs the new page against currently-running players.
    /// Stops players for layers no longer present; starts players for new video layers.
    /// </summary>
    public void UpdateSlide(Page? page)
    {
        var newLayers = page?.Layers
            .Where(l => l.Type == LayerType.Video && !string.IsNullOrEmpty(l.AssetPath))
            .ToList() ?? new List<SlideLayer>();

        var newIds = newLayers.Select(l => l.Id).ToHashSet();

        // Stop and dispose players for removed layers.
        foreach (var id in _players.Keys.Except(newIds).ToList())
        {
            _players[id].Dispose();
            _players.Remove(id);
        }

        // Start players for new layers.
        foreach (var layer in newLayers.Where(l => !_players.ContainsKey(l.Id)))
        {
            var player   = _playerFactory();
            var filePath = Path.Combine(AppFolders.Video, layer.AssetPath);
            var deviceId = layer.VideoAudioDestinationId is { } destId
                ? _destinations.FirstOrDefault(d => d.Id == destId)?.DeviceId
                : null;

            player.Start(filePath, layer.VideoLoopMode, layer.VideoVolume, deviceId);
            _players[layer.Id] = player;
        }
    }

    /// <summary>Returns the most recently decoded frame for a layer, or null if unavailable.</summary>
    public SKBitmap? TryGetFrame(Guid layerId) =>
        _players.TryGetValue(layerId, out var p) ? p.CurrentFrame : null;

    public void Dispose()
    {
        foreach (var p in _players.Values) p.Dispose();
        _players.Clear();
    }
}
```

- [ ] **Run tests**

```
dotnet test ShowCast.Tests --filter "VideoFrameRegistryTests" --logger "console;verbosity=detailed"
```

Expected: all 6 tests PASS.

- [ ] **Commit**

```bash
git add Core/VideoFrameRegistry.cs ShowCast.Tests/Core/VideoFrameRegistryTests.cs
git commit -m "feat(video): add VideoFrameRegistry; add registry diff tests"
```

---

## Task 8: `PageRenderer` — extract `DrawBitmapInRect`, add Video case

**Files:**
- Modify: `Engine/PageRenderer.cs`

The current `DrawImagePlaceholder` has the fit/fill/stretch draw logic inline. This task extracts it into a shared helper so the Video case can reuse it without duplication.

- [ ] **Add `getVideoFrame` parameter to `Render()`**

Change the signature of `Render()` from:

```csharp
public static void Render(SKCanvas canvas, Page page, LayerRole roleFilter,
                          int canvasWidth, int canvasHeight,
                          double elapsedMs     = -1.0,
                          double exitElapsedMs = -1.0,
                          bool useLiveTimers   = true)
```

To:

```csharp
public static void Render(SKCanvas canvas, Page page, LayerRole roleFilter,
                          int canvasWidth, int canvasHeight,
                          double elapsedMs        = -1.0,
                          double exitElapsedMs    = -1.0,
                          bool useLiveTimers      = true,
                          Func<Guid, SKBitmap?>? getVideoFrame = null)
```

- [ ] **Extract `DrawBitmapInRect` from `DrawImagePlaceholder`**

Add this private static method in the `// ── Helpers ──` region (around line 251):

```csharp
static void DrawBitmapInRect(SKCanvas canvas, SKBitmap bmp, SKRect rect, SlideLayer layer)
{
    byte alpha = (byte)(layer.Opacity * 255);
    using var paint = new SKPaint
    {
        IsAntialias = true,
        Color       = SKColors.White.WithAlpha(alpha),
        BlendMode   = ToSkia(layer.BlendMode)
    };

    var src = new SKRect(0, 0, bmp.Width, bmp.Height);

    switch (layer.ImageFit)
    {
        case ImageFit.Stretch:
            canvas.DrawBitmap(bmp, src, rect, paint);
            break;

        case ImageFit.Fill:
            float scaleF = Math.Max(rect.Width / bmp.Width, rect.Height / bmp.Height);
            float cw = rect.Width / scaleF, ch = rect.Height / scaleF;
            src = new SKRect((bmp.Width - cw) / 2, (bmp.Height - ch) / 2,
                             (bmp.Width + cw) / 2, (bmp.Height + ch) / 2);
            canvas.DrawBitmap(bmp, src, rect, paint);
            break;

        default: // Fit — letterbox, preserve aspect
            float scaleL = Math.Min(rect.Width / bmp.Width, rect.Height / bmp.Height);
            float fw = bmp.Width * scaleL, fh = bmp.Height * scaleL;
            var dst = new SKRect(rect.MidX - fw / 2, rect.MidY - fh / 2,
                                 rect.MidX + fw / 2, rect.MidY + fh / 2);
            canvas.DrawBitmap(bmp, src, dst, paint);
            break;
    }
}
```

- [ ] **Simplify `DrawImagePlaceholder` to use `DrawBitmapInRect`**

Replace the entire bitmap-drawing block in `DrawImagePlaceholder` (the `if (bmp is not null) { ... return; }` block) with:

```csharp
static void DrawImagePlaceholder(SKCanvas canvas, SlideLayer layer, int w, int h)
{
    var rect = LayerRect(layer, w, h);
    var bmp  = LoadImage(layer.AssetPath);

    if (bmp is not null)
    {
        DrawBitmapInRect(canvas, bmp, rect, layer);
        return;
    }

    using var bg = new SKPaint { Color = new SKColor(60, 60, 80, (byte)(layer.Opacity * 255)), BlendMode = ToSkia(layer.BlendMode) };
    canvas.DrawRect(rect, bg);
    DrawCenteredLabel(canvas, "[ Image ]", rect, SKColors.Gray);
}
```

- [ ] **Add `case LayerType.Video:` to the switch in `Render()`**

After the existing `case LayerType.Image:` block (line ~88), add:

```csharp
case LayerType.Video:
{
    var frame = getVideoFrame?.Invoke(layer.Id);
    if (frame is not null)
        DrawBitmapInRect(canvas, frame, rect, layer);
    else
    {
        using var bg = new SKPaint { Color = new SKColor(20, 20, 40, (byte)(layer.Opacity * 255)), BlendMode = ToSkia(layer.BlendMode) };
        canvas.DrawRect(rect, bg);
        DrawCenteredLabel(canvas, "[ Video ]", rect, SKColors.Gray);
    }
    break;
}
```

- [ ] **Build and run the existing PageRenderer tests**

```
dotnet build ShowCast.csproj
dotnet test ShowCast.Tests --filter "PageRenderer" --logger "console;verbosity=detailed"
```

Expected: build succeeds; all existing tests pass.

- [ ] **Commit**

```bash
git add Engine/PageRenderer.cs
git commit -m "feat(video): add video frame rendering to PageRenderer; extract DrawBitmapInRect"
```

---

## Task 9: Wire `OutputWindow`

**Files:**
- Modify: `Views/OutputWindow.axaml.cs`
- Modify: `Views/MainWindow.axaml.cs`

- [ ] **Update `OutputWindow` to own a `VideoFrameRegistry`**

In `Views/OutputWindow.axaml.cs`:

1. Add `using ShowCast.Core;` and `using System.Collections.Generic;` at the top (if not present).

2. Add the registry field after `_timer` — nullable because `OutputWindow()` (the parameterless Avalonia designer constructor) never calls the main constructor:

```csharp
VideoFrameRegistry? _videoRegistry;
```

3. Change the constructor signature from `OutputWindow(OutputState output)` to:

```csharp
public OutputWindow(OutputState output, IReadOnlyList<AudioDestination> audioDestinations)
```

4. Add registry initialization at the top of the constructor body (after `_output = output;`):

```csharp
_videoRegistry = new VideoFrameRegistry(audioDestinations);
```

5. In the `LivePage` subscription, call `UpdateSlide` before `OnLivePageChanged`:

```csharp
Page? prev = null;
_subs.Add(output.WhenAnyValue(o => o.LivePage).Subscribe(page =>
{
    _videoRegistry?.UpdateSlide(page);
    OnLivePageChanged(prev, page);
    prev = page;
}));
```

6. Add `getVideoFrame: _videoRegistry?.TryGetFrame` to every `PageRenderer.Render()` call in the file. There are two (leave `RenderTransitionFrame`'s `TransitionCompositor.Composite` unchanged — transitions don't render video):

In `RenderLayerAnimFrame`:
```csharp
void RenderLayerAnimFrame(double elapsed)
{
    int w = _output.Config.Width, h = _output.Config.Height;
    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));
    PageRenderer.Render(surface.Canvas, _output.LivePage!, _output.Roles, w, h, elapsed,
                        getVideoFrame: _videoRegistry?.TryGetFrame);
    RenderImage.Source = ToWriteableBitmap(surface, w, h);
}
```

In `Redraw`:
```csharp
void Redraw()
{
    int w = _output.Config.Width, h = _output.Config.Height;
    using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));
    if (_output.LivePage is not null)
        PageRenderer.Render(surface.Canvas, _output.LivePage, _output.Roles, w, h,
                            getVideoFrame: _videoRegistry?.TryGetFrame);
    else
        surface.Canvas.Clear(SKColors.Black);
    RenderImage.Source = ToWriteableBitmap(surface, w, h);
}
```

7. Dispose the registry in `OnClosed`:

```csharp
protected override void OnClosed(EventArgs e)
{
    _timer.Stop();
    _videoRegistry?.Dispose();
    foreach (var s in _subs) s.Dispose();
    base.OnClosed(e);
}
```

- [ ] **Update the two `OutputWindow` construction sites in `MainWindow.axaml.cs`**

Both occurrences of `new OutputWindow(output)` (lines ~154 and ~185) become:

```csharp
var win = new OutputWindow(output, VM?.ShowFileDestinations ?? new List<AudioDestination>());
```

Add `using System.Collections.Generic;` at the top of `MainWindow.axaml.cs` if not already present.

- [ ] **Build**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

- [ ] **Commit**

```bash
git add Views/OutputWindow.axaml.cs Views/MainWindow.axaml.cs
git commit -m "feat(video): wire VideoFrameRegistry into OutputWindow"
```

---

## Task 10: Wire `NdiSender`

**Files:**
- Modify: `Core/NdiSender.cs`
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Add `VideoFrameRegistry` to `NdiSender`**

In `Core/NdiSender.cs`:

1. Add `using System.Collections.Generic;` and `using SkiaSharp;` at the top if not present.

2. Add field after `_running`:

```csharp
readonly VideoFrameRegistry _videoRegistry;
```

3. Update constructor signature:

```csharp
public NdiSender(OutputState output, IReadOnlyList<AudioDestination> audioDestinations)
```

4. Initialize the registry in the constructor body (after `_pin = GCHandle.Alloc(...)` line):

```csharp
_videoRegistry = new VideoFrameRegistry(audioDestinations);
```

5. In `DetectPageChange()`, call `UpdateSlide` when a page change is detected. After the line `_prevLive = currentLive;`, add:

```csharp
_videoRegistry.UpdateSlide(currentLive);
```

6. Pass `getVideoFrame` in both `PageRenderer.Render()` calls in `RenderFrame()`:

```csharp
void RenderFrame(bool render)
{
    if (!render) { Array.Clear(_buffer); return; }

    var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888);

    if (_fromPage is not null && _output.LivePage is not null)
    {
        double trans = (DateTime.UtcNow - _transStartTime).TotalMilliseconds;
        float  prog  = _output.PendingTransitionDuration > 0
            ? (float)(trans / _output.PendingTransitionDuration) : 1f;

        if (prog < 1f)
        {
            using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
            TransitionCompositor.Composite(surface.Canvas, _fromPage, _output.LivePage,
                _output.Roles, _output.PendingTransitionType,
                prog, _output.PendingTransitionEasing, _w, _h, trans);
            return;
        }
        _fromPage      = null;
        _pageStartTime = DateTime.UtcNow;
    }

    if (_output.LivePage is { } page)
    {
        double elapsed = (DateTime.UtcNow - _pageStartTime).TotalMilliseconds;
        using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
        PageRenderer.Render(surface.Canvas, page, _output.Roles, _w, _h, elapsed,
                            getVideoFrame: _videoRegistry.TryGetFrame);
    }
    else
    {
        Array.Clear(_buffer);
    }
}
```

7. Dispose the registry in `Dispose()`, before `_pin.Free()`:

```csharp
public void Dispose()
{
    _running = false;
    _thread.Join(250);
    _videoRegistry.Dispose();
    if (_sender != IntPtr.Zero)
        NewTek.NDIlib.send_destroy(_sender);
    _pin.Free();
}
```

- [ ] **Update `NdiSender` construction in `MainViewModel.cs`**

In `ViewModels/MainViewModel.cs`, find the line (around line 295):

```csharp
_ndiSenders[o.Config.Id] = new ShowCast.Core.NdiSender(o);
```

Change to:

```csharp
_ndiSenders[o.Config.Id] = new ShowCast.Core.NdiSender(o, _showFile.Settings.AudioDestinations);
```

- [ ] **Build**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

- [ ] **Commit**

```bash
git add Core/NdiSender.cs ViewModels/MainViewModel.cs
git commit -m "feat(video): wire VideoFrameRegistry into NdiSender"
```

---

## Task 11: Inspector VIDEO section

**Files:**
- Modify: `Views/EditorInspectorPanel.axaml`
- Modify: `Views/EditorInspectorPanel.axaml.cs`

- [ ] **Add the VIDEO expander to the AXAML**

In `Views/EditorInspectorPanel.axaml`, add the following after the last existing `Expander` (the ANIMATION section, which is the last section before `</StackPanel>`):

```xml
<!-- ══ VIDEO ══ -->
<Expander x:Name="VideoSection" IsVisible="False"
          Theme="{StaticResource SectionExpander}"
          Header="VIDEO">
    <StackPanel Margin="0,2,0,0">

        <TextBlock Classes="field-label" Text="File"/>
        <Grid ColumnDefinitions="*,6,Auto" Margin="0,0,0,6">
            <TextBox x:Name="VideoPathBox"
                     Grid.Column="0"
                     IsReadOnly="True"
                     Text="(no file)"
                     Foreground="#888888"/>
            <Button Grid.Column="2"
                    x:Name="VideoBrowseBtn"
                    Content="Browse…"
                    Height="28"
                    Padding="8,0"
                    Background="#3a3a3a"
                    Foreground="White"
                    BorderBrush="#555555"
                    BorderThickness="1"
                    CornerRadius="4"
                    Click="OnVideoBrowse"/>
        </Grid>

        <TextBlock Classes="field-label" Text="Loop Mode"/>
        <ComboBox x:Name="VideoLoopModeBox" SelectionChanged="OnVideoLoopModeChanged">
            <ComboBoxItem Content="Loop"/>
            <ComboBoxItem Content="Hold Last Frame"/>
            <ComboBoxItem Content="Go Black"/>
        </ComboBox>

        <Grid ColumnDefinitions="*,8,Auto">
            <StackPanel Grid.Column="0">
                <TextBlock Classes="field-label" Text="Volume"/>
                <Slider x:Name="VideoVolumeSlider" Minimum="0" Maximum="100" Value="100"
                        ValueChanged="OnVideoVolumeChanged" Margin="0,4,0,6"/>
            </StackPanel>
            <TextBlock Grid.Column="2" x:Name="VideoVolumeLabel"
                       Text="100%" Foreground="#888888" FontSize="11"
                       VerticalAlignment="Center" Width="36"/>
        </Grid>

        <TextBlock Classes="field-label" Text="Audio Output"/>
        <ComboBox x:Name="VideoAudioOutputBox"
                  HorizontalAlignment="Stretch"
                  Margin="0,0,0,6"
                  SelectionChanged="OnVideoAudioOutputChanged"/>

    </StackPanel>
</Expander>
```

- [ ] **Add the `VideoAudioOption` record and new fields to the code-behind**

In `Views/EditorInspectorPanel.axaml.cs`, add after the existing `TimerBindingOption` record:

```csharp
record VideoAudioOption(Guid? Id, string Label)
{
    public override string ToString() => Label;
}
```

- [ ] **Add `VideoSection.IsVisible = false` to the `LoadLayer` reset block**

In `LoadLayer`, in the block that sets section visibility (around line 226), add:

```csharp
VideoSection.IsVisible     = false;
```

alongside the other `XxxSection.IsVisible = false;` assignments.

- [ ] **Add `case LayerType.Video:` to the switch in `LoadLayer`**

After the `case LayerType.Background: / case LayerType.Shape:` block, add:

```csharp
case LayerType.Video:
    VideoSection.IsVisible = true;
    VideoPathBox.Text = string.IsNullOrEmpty(layer.AssetPath) ? "(no file)" : layer.AssetPath;
    VideoLoopModeBox.SelectedIndex = (int)layer.VideoLoopMode;
    VideoVolumeSlider.Value  = layer.VideoVolume * 100;
    VideoVolumeLabel.Text    = $"{(int)(layer.VideoVolume * 100)}%";
    var audioItems = new System.Collections.Generic.List<VideoAudioOption>
        { new(null, "Default (OS)") };
    if (VM is not null)
        audioItems.AddRange(VM.ShowFileDestinations.Select(d => new VideoAudioOption(d.Id, d.DisplayName)));
    VideoAudioOutputBox.ItemsSource   = audioItems;
    VideoAudioOutputBox.SelectedIndex = layer.VideoAudioDestinationId is null ? 0
        : audioItems.FindIndex(i => i.Id == layer.VideoAudioDestinationId);
    break;
```

- [ ] **Add `FlushVideoLayerFields()` and call it from `LoadLayer`**

Video controls (slider, combo boxes) apply immediately via their `ValueChanged`/`SelectionChanged` handlers — there is no "typed-but-uncommitted" state to flush. Add a no-op stub so the call site in `LoadLayer` is consistent with other layer types:

```csharp
void FlushVideoLayerFields() { }
```

Add `FlushVideoLayerFields();` at the top of `LoadLayer` alongside the other `Flush*` calls.

- [ ] **Add the three event handlers**

```csharp
async void OnVideoBrowse(object? sender, RoutedEventArgs e)
{
    if (VM?.SelectedLayer is not { Type: LayerType.Video } layer) return;
    var tl = TopLevel.GetTopLevel(this);
    if (tl is null) return;

    var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title          = "Select Video File",
        AllowMultiple  = false,
        FileTypeFilter = new[]
        {
            new FilePickerFileType("Video Files")
            {
                Patterns = new[] { "*.mp4", "*.mov", "*.avi", "*.mkv", "*.wmv", "*.webm", "*.m4v", "*.av1" }
            },
            new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
        }
    });

    var path = files.FirstOrDefault()?.Path.LocalPath;
    if (string.IsNullOrEmpty(path)) return;

    var dest = Path.Combine(AppFolders.Video, Path.GetFileName(path));
    if (!File.Exists(dest))
        File.Copy(path, dest);

    VM.BeginLayerEdit();
    layer.AssetPath  = Path.GetFileName(path);
    VideoPathBox.Text = layer.AssetPath;
    VM.NotifySlideChanged();
}

void OnVideoLoopModeChanged(object? sender, SelectionChangedEventArgs e)
{
    if (_loading || VM?.SelectedLayer is not { Type: LayerType.Video } layer) return;
    layer.VideoLoopMode = (VideoLoopMode)VideoLoopModeBox.SelectedIndex;
    VM.NotifySlideChanged();
}

void OnVideoVolumeChanged(object? sender, Avalonia.Controls.Primitives.RangeBaseValueChangedEventArgs e)
{
    if (_loading || VM?.SelectedLayer is not { Type: LayerType.Video } layer) return;
    layer.VideoVolume    = (float)(VideoVolumeSlider.Value / 100.0);
    VideoVolumeLabel.Text = $"{(int)VideoVolumeSlider.Value}%";
    VM.NotifySlideChanged();
}

void OnVideoAudioOutputChanged(object? sender, SelectionChangedEventArgs e)
{
    if (_loading || VM?.SelectedLayer is not { Type: LayerType.Video } layer) return;
    if (VideoAudioOutputBox.SelectedItem is VideoAudioOption opt)
    {
        layer.VideoAudioDestinationId = opt.Id;
        VM.NotifySlideChanged();
    }
}
```

Add `using System.IO;` and `using ShowCast.Core;` to the top of the file if not already present.

- [ ] **Build**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

- [ ] **Commit**

```bash
git add Views/EditorInspectorPanel.axaml Views/EditorInspectorPanel.axaml.cs
git commit -m "feat(video): add VIDEO inspector section with file picker, loop mode, volume, audio output"
```

---

## Task 12: Editor toolbar button and `AddVideoLayer`

**Files:**
- Modify: `Views/PageEditorOverlay.axaml`
- Modify: `Views/PageEditorOverlay.axaml.cs`
- Modify: `ViewModels/MainViewModel.cs`
- Modify: `Views/EditorLayerPanel.cs`

- [ ] **Update the `TypeBadge` converter in `EditorLayerPanel.cs`**

In `Views/EditorLayerPanel.cs`, change the `TypeBadge` converter to handle `LayerType.Video`:

```csharp
public static readonly FuncValueConverter<LayerType, string> TypeBadge =
    new(t => t switch
    {
        LayerType.Background => "BG",
        LayerType.Text       => "T",
        LayerType.Image      => "IMG",
        LayerType.Shape      => "SHP",
        LayerType.Clock      => "CLK",
        LayerType.Video      => "VID",
        _                    => "?"
    });
```

- [ ] **Add `AddVideoLayer()` to `MainViewModel.cs`**

In `ViewModels/MainViewModel.cs`, after `AddImageLayer()` (around line 1389), add:

```csharp
public void AddVideoLayer()
{
    if (EditingPage is null) return;
    int maxZ = EditingPage.Layers.Count > 0 ? EditingPage.Layers.Max(l => l.ZOrder) : 0;
    var layer = new SlideLayer
    {
        Type   = LayerType.Video, Name = "Video",
        X      = 0f, Y = 0f, Width = 1f, Height = 1f,
        ZOrder = maxZ + 1, Roles = LayerRole.All
    };
    EditingPage.AddLayer(layer);
    RefreshEditorLayers();
    SelectedLayer = layer;
    NotifySlideChanged();
}
```

- [ ] **Add the `+ Video` button to `PageEditorOverlay.axaml`**

In `Views/PageEditorOverlay.axaml`, update the toolbar grid:

1. Change `ColumnDefinitions` to add one more `Auto` column (before the Rulers column). The existing definition has 14 columns; add one more for 15:

From:
```xml
<Grid ColumnDefinitions="Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto">
```
To:
```xml
<Grid ColumnDefinitions="Auto,Auto,Auto,*,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto,Auto">
```

2. Add the `+ Video` button at `Grid.Column="7"` (after the Image button):

```xml
<!-- Add Video -->
<Button Grid.Column="7" Content="+ Video"
        Classes="tool-btn"
        Margin="4,5"
        Click="OnAddVideo"
        ToolTip.Tip="Add video layer (opens file picker)"/>
```

3. Increment every existing `Grid.Column` that was ≥ 7 by 1:
- Rulers: `Grid.Column="7"` → `Grid.Column="8"`
- Grid size: `Grid.Column="8"` → `Grid.Column="9"`
- Snap: `Grid.Column="9"` → `Grid.Column="10"`
- Safe: `Grid.Column="10"` → `Grid.Column="11"`
- Del Layer: `Grid.Column="11"` → `Grid.Column="12"`
- Preview: `Grid.Column="12"` → `Grid.Column="13"`

- [ ] **Add `OnAddVideo` handler to `PageEditorOverlay.axaml.cs`**

```csharp
async void OnAddVideo(object? sender, RoutedEventArgs e)
{
    if (VM is null) return;
    var tl = TopLevel.GetTopLevel(this);
    if (tl is null) return;

    var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title          = "Select Video File",
        AllowMultiple  = false,
        FileTypeFilter = new[]
        {
            new FilePickerFileType("Video Files")
            {
                Patterns = new[] { "*.mp4", "*.mov", "*.avi", "*.mkv", "*.wmv", "*.webm", "*.m4v", "*.av1" }
            },
            new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
        }
    });

    var path = files.FirstOrDefault()?.Path.LocalPath;
    if (string.IsNullOrEmpty(path)) return;

    var dest = System.IO.Path.Combine(ShowCast.Core.AppFolders.Video,
                                       System.IO.Path.GetFileName(path));
    if (!System.IO.File.Exists(dest))
        System.IO.File.Copy(path, dest);

    VM.AddVideoLayer();
    if (VM.SelectedLayer is { Type: ShowCast.Core.LayerType.Video } layer)
    {
        layer.AssetPath = System.IO.Path.GetFileName(path);
        VM.NotifySlideChanged();
    }
}
```

- [ ] **Build**

```
dotnet build ShowCast.csproj
```

Expected: `Build succeeded.`

- [ ] **Run all tests**

```
dotnet test ShowCast.Tests --logger "console;verbosity=detailed"
```

Expected: all tests PASS.

- [ ] **Commit**

```bash
git add Views/EditorLayerPanel.cs ViewModels/MainViewModel.cs Views/PageEditorOverlay.axaml Views/PageEditorOverlay.axaml.cs
git commit -m "feat(video): add Video toolbar button, AddVideoLayer, VID type badge"
```

---

## Task 13: Final build and branch ready for testing

- [ ] **Run the full test suite one more time**

```
dotnet test ShowCast.Tests --logger "console;verbosity=detailed"
```

Expected: all tests PASS.

- [ ] **Build in Release to catch any release-only issues**

```
dotnet build ShowCast.csproj -c Release
```

Expected: `Build succeeded.`

- [ ] **Manual smoke test checklist** (do this in the running app)

1. Open ShowCast, open the slide editor.
2. Click `+ Video` in the toolbar — file picker opens, select an `.mp4`.
3. A "Video" layer is added; inspector shows VIDEO section with the filename, Loop/Hold/Black dropdown, Volume slider, Audio Output dropdown.
4. Return to main view, send the page to live output.
5. Video plays on the output window automatically.
6. Verify loop mode: set to "Go Black", let video end — output goes black. Reload, set to "Loop" — video restarts.
7. Verify the editor canvas shows a dark "[ Video ]" placeholder (not live video).
8. Close and reopen the show file — video layer properties serialize/deserialize correctly.
