# Video Playback in Output Preview Viewers — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show live video frames in the right-side output preview thumbnail cards when the corresponding OutputWindow is open and playing video.

**Architecture:** Add a nullable `VideoFrameRegistry?` property to `OutputState` (the shared data model). `OutputWindow` sets it on open and clears it before dispose. `WebView2PreviewControl` subscribes to that property, keeps its `DispatcherTimer` alive while video layers are present, and passes `registry.TryGetFrame` to `PageRenderer.Render()` — sharing decoded frames at zero extra decode cost.

**Tech Stack:** C# / .NET 9, ReactiveUI (`WhenAnyValue`, `RaiseAndSetIfChanged`), SkiaSharp, xUnit

---

### Task 1: Add `VideoRegistry` property to `OutputState`

**Files:**
- Modify: `Core/OutputState.cs`
- Create: `ShowCast.Tests/Core/OutputStateVideoRegistryTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `ShowCast.Tests/Core/OutputStateVideoRegistryTests.cs`:

```csharp
using System.Collections.Generic;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class OutputStateVideoRegistryTests
{
    static OutputState MakeState() => new(new OutputConfig());

    [Fact]
    public void VideoRegistry_DefaultsToNull()
    {
        var state = MakeState();
        Assert.Null(state.VideoRegistry);
    }

    [Fact]
    public void VideoRegistry_CanBeSetAndRead()
    {
        var state    = MakeState();
        var registry = new VideoFrameRegistry(new List<AudioDestination>());

        state.VideoRegistry = registry;

        Assert.Same(registry, state.VideoRegistry);
        registry.Dispose();
    }

    [Fact]
    public void VideoRegistry_RaisesPropertyChanged()
    {
        var state    = MakeState();
        var registry = new VideoFrameRegistry(new List<AudioDestination>());
        string? changedProp = null;
        state.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        state.VideoRegistry = registry;

        Assert.Equal(nameof(OutputState.VideoRegistry), changedProp);
        registry.Dispose();
    }

    [Fact]
    public void VideoRegistry_CanBeClearedToNull()
    {
        var state    = MakeState();
        var registry = new VideoFrameRegistry(new List<AudioDestination>());
        state.VideoRegistry = registry;

        state.VideoRegistry = null;

        Assert.Null(state.VideoRegistry);
        registry.Dispose();
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~OutputStateVideoRegistryTests" -v minimal
```

Expected: compilation error or 4 failures — `VideoRegistry` does not exist yet.

- [ ] **Step 3: Add the property to `OutputState`**

Open `Core/OutputState.cs`. After the `IsOutputWindowOpen` property block (around line 58), add:

```csharp
private VideoFrameRegistry? _videoRegistry;
public VideoFrameRegistry? VideoRegistry
{
    get => _videoRegistry;
    set => this.RaiseAndSetIfChanged(ref _videoRegistry, value);
}
```

The full file after the change should have this block between `IsOutputWindowOpen` and `GoLive`:

```csharp
    private bool _isOutputWindowOpen;
    public bool IsOutputWindowOpen
    {
        get => _isOutputWindowOpen;
        set => this.RaiseAndSetIfChanged(ref _isOutputWindowOpen, value);
    }

    private VideoFrameRegistry? _videoRegistry;
    public VideoFrameRegistry? VideoRegistry
    {
        get => _videoRegistry;
        set => this.RaiseAndSetIfChanged(ref _videoRegistry, value);
    }

    public void GoLive(Page page, int index,
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~OutputStateVideoRegistryTests" -v minimal
```

Expected: 4 tests pass.

- [ ] **Step 5: Run the full test suite to check for regressions**

```
dotnet test ShowCast.Tests -v minimal
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```
git add Core/OutputState.cs ShowCast.Tests/Core/OutputStateVideoRegistryTests.cs
git commit -m "feat(core): add VideoRegistry property to OutputState"
```

---

### Task 2: Wire `OutputWindow` to publish/clear `VideoRegistry`

**Files:**
- Modify: `Views/OutputWindow.axaml.cs`

No new unit tests — `OutputWindow` is a UI class. Manual verification is in Task 4.

- [ ] **Step 1: Set `VideoRegistry` in the constructor**

Open `Views/OutputWindow.axaml.cs`. Around line 34, after the `_videoRegistry` creation line, add the publish call:

```csharp
_videoRegistry = new ShowCast.Core.VideoFrameRegistry(audioDestinations, ndiLookup: ndiLookup);
output.VideoRegistry = _videoRegistry;   // publish to shared state
```

The constructor block should now look like:

```csharp
public OutputWindow(OutputState output, IReadOnlyList<ShowCast.Core.AudioDestination> audioDestinations,
                    Func<string, ShowCast.Core.NdiSender?>? ndiLookup = null)
{
    InitializeComponent();
    _output = output;
    _videoRegistry = new ShowCast.Core.VideoFrameRegistry(audioDestinations, ndiLookup: ndiLookup);
    output.VideoRegistry = _videoRegistry;
    Title   = $"ShowCast — {output.Name}";
    // ...rest unchanged
```

- [ ] **Step 2: Clear `VideoRegistry` in `OnClosed` before dispose**

In `Views/OutputWindow.axaml.cs`, find `OnClosed` (around line 197). Add the clear call *before* `_videoRegistry?.Dispose()`:

```csharp
protected override void OnClosed(EventArgs e)
{
    _timer.Stop();
    _output.VideoRegistry = null;      // clear before dispose — prevents use-after-free in preview
    _videoRegistry?.Dispose();
    foreach (var s in _subs) s.Dispose();
    base.OnClosed(e);
}
```

- [ ] **Step 3: Build to check for errors**

```
dotnet build ShowCast -v minimal
```

Expected: build succeeds with no errors.

- [ ] **Step 4: Commit**

```
git add Views/OutputWindow.axaml.cs
git commit -m "feat(output): publish VideoRegistry on OutputState when output window opens/closes"
```

---

### Task 3: Update `WebView2PreviewControl` to render video frames

**Files:**
- Modify: `Views/WebView2PreviewControl.cs`

No new unit tests — this is a UI render class. Manual verification is in Task 4.

- [ ] **Step 1: Add `HasVideoLayers` helper**

Open `Views/WebView2PreviewControl.cs`. In the `// ── Render helpers` section (after line 195), add the static helper:

```csharp
static bool HasVideoLayers(Page? page) =>
    page?.Layers.Any(l => l.Type == LayerType.Video && !string.IsNullOrEmpty(l.AssetPath)) == true;
```

- [ ] **Step 2: Subscribe to `VideoRegistry` changes**

In `OnPropertyChanged`, inside the `if (_currentOutput is not null)` block (around line 81), add a second subscription after the existing `LivePage` subscription:

```csharp
if (_currentOutput is not null)
{
    Page? prev = null;
    _subs.Add(_currentOutput.WhenAnyValue(o => o.LivePage).Subscribe(page =>
    {
        OnLivePageChanged(_currentOutput, prev, page);
        prev = page;
    }));
    _subs.Add(_currentOutput.WhenAnyValue(o => o.VideoRegistry).Subscribe(_ =>
        StartTimerIfNeeded(_currentPage)));
}
```

When `VideoRegistry` goes from null → non-null (output window opens), `StartTimerIfNeeded` will start the timer if the current page has video layers. When it goes non-null → null (output window closes), the timer will stop naturally on the next tick that finds neither animations nor video.

- [ ] **Step 3: Update `StartTimerIfNeeded` to keep timer alive for video**

Replace the existing `StartTimerIfNeeded` method (around line 140) with:

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

- [ ] **Step 4: Update `OnTick` to keep timer alive while video is playing**

In `OnTick` (around line 154), find the section at the end of the method that stops the timer when animations finish:

```csharp
        if (animating)
            { RenderAnimFrame(elapsed); return; }

        _timer.Stop();
        RenderAnimFrame(elapsed);
```

Replace it with:

```csharp
        bool hasVideo = _currentOutput?.VideoRegistry is not null && HasVideoLayers(_currentPage);

        if (animating || hasVideo)
            { RenderAnimFrame(elapsed); return; }

        _timer.Stop();
        RenderAnimFrame(elapsed);
```

- [ ] **Step 5: Pass `getVideoFrame` to `RenderAnimFrame`**

Replace the `RenderAnimFrame` method (around line 206):

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
```

- [ ] **Step 6: Pass `getVideoFrame` to `RenderStatic`**

Replace the `RenderStatic` method (around line 216):

```csharp
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

- [ ] **Step 7: Build to check for errors**

```
dotnet build ShowCast -v minimal
```

Expected: build succeeds with no errors.

- [ ] **Step 8: Run full test suite**

```
dotnet test ShowCast.Tests -v minimal
```

Expected: all tests pass.

- [ ] **Step 9: Commit**

```
git add Views/WebView2PreviewControl.cs
git commit -m "feat(preview): render live video frames in output preview viewers"
```

---

### Task 4: Manual smoke test

- [ ] **Step 1: Build and launch the application**

```
dotnet run --project ShowCast
```

- [ ] **Step 2: Verify video plays in preview**

1. Open a show that has a page with a video layer
2. Send that page live to an output
3. Open the output window for that output
4. Confirm the right-side preview thumbnail now shows the live video playing (not `[ Video ]`)

- [ ] **Step 3: Verify placeholder when output window is closed**

1. Close the output window
2. Confirm the preview thumbnail reverts to showing `[ Video ]` placeholder

- [ ] **Step 4: Verify no regressions**

1. Send a non-video page live — confirm preview renders correctly
2. Trigger a page transition — confirm transition animates in the preview
3. Open multiple outputs — confirm each preview card independently shows its own video or placeholder

- [ ] **Step 5: Commit (if any fixups were needed during smoke test)**

```
git add -p
git commit -m "fix(preview): <describe fixup>"
```
