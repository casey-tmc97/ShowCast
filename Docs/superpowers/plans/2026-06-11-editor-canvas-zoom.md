# Editor Canvas Zoom Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Ctrl+scroll-wheel zoom to the editor canvas with cursor-anchored zoom that resets when switching pages.

**Architecture:** A `public static ComputeZoomedRect` helper on `EditorCanvas` holds the pure zoom math (testable). Two new fields (`_zoomLevel`, `_zoomOriginNorm`) feed into the existing `GetImageRect()` method; all downstream overlay code (handles, grid, rulers) picks up zoom for free. A new `UpdateSlideLayout()` explicitly positions `_slideImg` since it lives in a Grid and cannot auto-follow `GetImageRect()`. Zoom resets to 1.0 when the editing page changes.

**Tech Stack:** C# / .NET 9, Avalonia 11.2.2, SkiaSharp

---

## File Map

| File | Change |
|------|--------|
| `Views/EditorCanvas.cs` | Add 3 fields, `ComputeZoomedRect`, modify `GetImageRect`, add `UpdateSlideLayout`, update `RebuildSlide`, add `OnWheelZoom`, register handler |
| `ShowCast.Tests/Views/EditorCanvasZoomTests.cs` | New: 3 unit tests for `ComputeZoomedRect` |
| `ShowCast.Tests/ShowCast.Tests.csproj` | Add `Avalonia` package reference for test types |

---

### Task 1: Write failing tests for `ComputeZoomedRect`, then implement it

**Files:**
- Create: `ShowCast.Tests/Views/EditorCanvasZoomTests.cs`
- Modify: `ShowCast.Tests/ShowCast.Tests.csproj`
- Modify: `Views/EditorCanvas.cs` (add method only)

- [ ] **Step 1: Add Avalonia package reference to test project**

Open `ShowCast.Tests/ShowCast.Tests.csproj` and add this line inside the first `<ItemGroup>` that has `PackageReference` entries:

```xml
<PackageReference Include="Avalonia" Version="11.2.2" />
```

- [ ] **Step 2: Write the failing tests**

Create `ShowCast.Tests/Views/EditorCanvasZoomTests.cs`:

```csharp
using Avalonia;
using ShowCast.Views;
using Xunit;

namespace ShowCast.Tests.Views;

public class EditorCanvasZoomTests
{
    [Fact]
    public void ComputeZoomedRect_AtZoomOne_ReturnsUnchangedRect()
    {
        var baseRect = new Rect(100, 50, 640, 360);
        var result = EditorCanvas.ComputeZoomedRect(baseRect, 1.0, new Point(0.5, 0.5));
        Assert.Equal(baseRect.X,      result.X,      precision: 4);
        Assert.Equal(baseRect.Y,      result.Y,      precision: 4);
        Assert.Equal(baseRect.Width,  result.Width,  precision: 4);
        Assert.Equal(baseRect.Height, result.Height, precision: 4);
    }

    [Fact]
    public void ComputeZoomedRect_AtZoomTwo_CenterOrigin_DoublesSizeKeepsCenter()
    {
        // Base rect with no offset: (0, 0, 400, 225)
        // Center origin (0.5, 0.5): center screen pt = (200, 112.5)
        // After 2×: size = (800, 450), origin stays → x = 200 - 0.5*800 = -200
        var baseRect = new Rect(0, 0, 400, 225);
        var result = EditorCanvas.ComputeZoomedRect(baseRect, 2.0, new Point(0.5, 0.5));
        Assert.Equal(800,    result.Width,  precision: 4);
        Assert.Equal(450,    result.Height, precision: 4);
        Assert.Equal(-200,   result.X,      precision: 4);
        Assert.Equal(-112.5, result.Y,      precision: 4);
    }

    [Fact]
    public void ComputeZoomedRect_AtZoomTwo_TopLeftOrigin_TopLeftStaysFixed()
    {
        // Origin (0, 0) = top-left of slide. That screen point = (100, 50).
        // After 2×: x = 100 - 0*800 = 100, y = 50 - 0*450 = 50
        var baseRect = new Rect(100, 50, 400, 225);
        var result = EditorCanvas.ComputeZoomedRect(baseRect, 2.0, new Point(0, 0));
        Assert.Equal(100, result.X,      precision: 4);
        Assert.Equal(50,  result.Y,      precision: 4);
        Assert.Equal(800, result.Width,  precision: 4);
        Assert.Equal(450, result.Height, precision: 4);
    }
}
```

- [ ] **Step 3: Run tests to confirm they fail**

```
dotnet test ShowCast.Tests --filter "EditorCanvasZoomTests" -v normal
```

Expected: build error — `EditorCanvas` has no member `ComputeZoomedRect`.

- [ ] **Step 4: Add `ComputeZoomedRect` to `EditorCanvas`**

Open `Views/EditorCanvas.cs`. Find the `// ── Coordinate helpers` section (near line 585). Add this method immediately before `GetImageRect()`:

```csharp
/// <summary>
/// Scales <paramref name="baseRect"/> by <paramref name="zoomLevel"/> such that
/// the point at <paramref name="originNorm"/> (0–1 normalized within the base rect)
/// remains at the same screen position after zooming.
/// </summary>
public static Rect ComputeZoomedRect(Rect baseRect, double zoomLevel, Point originNorm)
{
    if (zoomLevel == 1.0) return baseRect;
    double zw = baseRect.Width  * zoomLevel;
    double zh = baseRect.Height * zoomLevel;
    double originSX = baseRect.X + originNorm.X * baseRect.Width;
    double originSY = baseRect.Y + originNorm.Y * baseRect.Height;
    return new Rect(
        originSX - originNorm.X * zw,
        originSY - originNorm.Y * zh,
        zw, zh);
}
```

- [ ] **Step 5: Run tests to confirm they pass**

```
dotnet test ShowCast.Tests --filter "EditorCanvasZoomTests" -v normal
```

Expected: 3/3 PASS.

- [ ] **Step 6: Run full test suite to confirm no regressions**

```
dotnet test ShowCast.Tests -v quiet
```

Expected: all tests pass (206+ passing, 0 failing).

- [ ] **Step 7: Commit**

```
git add ShowCast.Tests/ShowCast.Tests.csproj ShowCast.Tests/Views/EditorCanvasZoomTests.cs Views/EditorCanvas.cs
git commit -m "feat: add ComputeZoomedRect to EditorCanvas with tests"
```

---

### Task 2: Wire zoom into `EditorCanvas` — fields, rendering, input

**Files:**
- Modify: `Views/EditorCanvas.cs`

- [ ] **Step 1: Add zoom fields**

In `EditorCanvas.cs`, find the `// ── Drag state` section (near line 115). Add three new fields immediately before it:

```csharp
// ── Zoom state ────────────────────────────────────────────────────────────
double _zoomLevel      = 1.0;
Point  _zoomOriginNorm = new(0.5, 0.5);
Page?  _lastEditingPage;
```

- [ ] **Step 2: Modify `GetImageRect()` to apply zoom**

Find the current `GetImageRect()` method (near line 587). Replace it entirely:

```csharp
Rect GetImageRect()
{
    double cw = _overlay.Bounds.Width, ch = _overlay.Bounds.Height;
    if (cw <= 0 || ch <= 0) return new Rect(0, 0, cw, ch);
    const double aspect = 16.0 / 9.0;
    double iw, ih;
    if (cw / ch > aspect) { ih = ch; iw = ih * aspect; }
    else                  { iw = cw; ih = iw / aspect; }
    var baseRect = new Rect((cw - iw) / 2, (ch - ih) / 2, iw, ih);
    return ComputeZoomedRect(baseRect, _zoomLevel, _zoomOriginNorm);
}
```

- [ ] **Step 3: Add `UpdateSlideLayout()` method**

`_slideImg` is a Grid child with `Stretch.Uniform` — it won't auto-follow `GetImageRect()`. Add this helper immediately after `UpdateSlideImage()` (near line 328):

```csharp
void UpdateSlideLayout()
{
    var ir = GetImageRect();
    _slideImg.Width  = ir.Width;
    _slideImg.Height = ir.Height;
    _slideImg.HorizontalAlignment = HorizontalAlignment.Left;
    _slideImg.VerticalAlignment   = VerticalAlignment.Top;
    _slideImg.Margin  = new Thickness(ir.X, ir.Y, 0, 0);
    _slideImg.Stretch = Stretch.Fill;
}
```

Check existing `using` statements at the top of `EditorCanvas.cs`. `HorizontalAlignment` and `VerticalAlignment` are in `Avalonia.Layout`; `Stretch` is in `Avalonia.Media`. Both should already be imported. If not, add:
```csharp
using Avalonia.Layout;
using Avalonia.Media;
```

- [ ] **Step 4: Update `RebuildSlide()` — add page-change zoom reset and `UpdateSlideLayout()` call**

Find `RebuildSlide()` (near line 298). Replace it entirely:

```csharp
void RebuildSlide()
{
    var slide = _vm?.EditingPage;
    if (slide is null) { _slideImg.Source = null; return; }

    // Reset zoom whenever the editing page changes
    if (slide != _lastEditingPage)
    {
        _zoomLevel      = 1.0;
        _zoomOriginNorm = new Point(0.5, 0.5);
        _lastEditingPage = slide;
    }

    using var surface = SKSurface.Create(new SKImageInfo(RenderW, RenderH, SKColorType.Rgba8888));
    PageRenderer.Render(surface.Canvas, slide, LayerRole.All, RenderW, RenderH, useLiveTimers: false);
    UpdateSlideImage(surface);

    UpdateHandles();
    UpdateSlideLayout();
    RebuildGrid();
    RebuildSafeBoundaries();
}
```

- [ ] **Step 5: Add the `OnWheelZoom` handler**

Add this method in the `// ── Coordinate helpers` section, just before `GetImageRect()`:

```csharp
void OnWheelZoom(object? sender, PointerWheelEventArgs e)
{
    if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

    var cursor = e.GetPosition(_overlay);

    // Compute the base (un-zoomed) letterbox rect to derive a stable origin
    double cw = _overlay.Bounds.Width, ch = _overlay.Bounds.Height;
    const double aspect = 16.0 / 9.0;
    double bw, bh;
    if (cw / ch > aspect) { bh = ch; bw = bh * aspect; }
    else                  { bw = cw; bh = bw / aspect; }
    double bx = (cw - bw) / 2, by = (ch - bh) / 2;

    _zoomOriginNorm = new Point(
        (cursor.X - bx) / bw,
        (cursor.Y - by) / bh);

    double factor = e.Delta.Y > 0 ? 1.1 : 1.0 / 1.1;
    _zoomLevel = Math.Clamp(_zoomLevel * factor, 0.25, 4.0);

    RebuildSlide();
    e.Handled = true;
}
```

- [ ] **Step 6: Register the handler in the constructor**

In `EditorCanvas()` (near line 196), add after the existing `_overlay.DoubleTapped += OnDoubleTapped;` line:

```csharp
_overlay.PointerWheelChanged += OnWheelZoom;
```

- [ ] **Step 7: Build to confirm 0 errors**

```
dotnet build ShowCast.Tests
```

Expected: Build succeeded, 0 Error(s).

- [ ] **Step 8: Run full test suite**

```
dotnet test ShowCast.Tests -v quiet
```

Expected: all tests pass (206+ passing, 0 failing).

- [ ] **Step 9: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat: wire Ctrl+scroll zoom into EditorCanvas"
```
