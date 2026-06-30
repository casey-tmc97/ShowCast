# Crosshair Guide Snapping Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While dragging a single layer, snap its four edge midpoints (and center) to the frame's center crosshair guides (x=0.5, y=0.5 in normalized coordinates).

**Architecture:** Two `internal static` helpers (`SnapToGuideX`, `SnapToGuideY`) are added to `EditorCanvas` alongside the existing `SnapX`/`SnapY` methods. They are called as a second snap pass after grid snap in the `HandleKind.Move` branch of `OnPointerMoved`, single-layer path only. `InternalsVisibleTo("ShowCast.Tests")` is already configured.

**Tech Stack:** C# 12, Avalonia 11.2.2, xUnit

## Global Constraints

- Snapping is always active — no toggle, no dependency on `ShowSafeBoundaries` or `SnapToGrid`
- Single-layer drag only — the multi-selection `_origPositions` path is not touched
- Snap threshold: 8 overlay pixels, computed as `8.0 / irWidth` (X axis) and `8.0 / irHeight` (Y axis)
- Three snap candidates per axis: leading edge, trailing edge, and center
- Closest candidate within threshold wins; unchanged if none qualify
- No new VM properties, no new UI controls

---

### Task 1: Add `SnapToGuideX`/`SnapToGuideY` helpers, tests, and call site

**Files:**
- Modify: `Views/EditorCanvas.cs` — add two `internal static` helpers near the existing `SnapX`/`SnapY` methods (~line 697); add two lines to the single-layer `HandleKind.Move` branch (~line 878)
- Create: `ShowCast.Tests/Views/EditorCanvasSnapTests.cs`

**Interfaces:**
- Produces:
  - `internal static float EditorCanvas.SnapToGuideX(float x, float w, double irWidth)`
  - `internal static float EditorCanvas.SnapToGuideY(float y, float h, double irHeight)`

- [ ] **Step 1: Create the test file with failing tests**

Create `ShowCast.Tests/Views/EditorCanvasSnapTests.cs`:

```csharp
using ShowCast.Views;
using Xunit;

namespace ShowCast.Tests.Views;

public class EditorCanvasSnapTests
{
    // irWidth=1920 → threshold = 8/1920 ≈ 0.004167
    // irHeight=1080 → threshold = 8/1080 ≈ 0.007407

    // ── SnapToGuideX ─────────────────────────────────────────────────────────

    [Fact]
    public void SnapToGuideX_LeftEdgeWithinThreshold_SnapsLeftEdgeToCenter()
    {
        // left edge at 0.499 → dist 0.001 < threshold → X becomes 0.5
        float result = EditorCanvas.SnapToGuideX(0.499f, 0.3f, 1920.0);
        Assert.Equal(0.5f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_RightEdgeWithinThreshold_SnapsRightEdgeToCenter()
    {
        // x=0.2, w=0.299 → right edge at 0.499 → dist 0.001 < threshold → X = 0.5 - 0.299 = 0.201
        float result = EditorCanvas.SnapToGuideX(0.2f, 0.299f, 1920.0);
        Assert.Equal(0.201f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_CenterWithinThreshold_CentersLayerHorizontally()
    {
        // x=0.301, w=0.4 → center at 0.501 → dist 0.001 < threshold → X = 0.5 - 0.2 = 0.3
        float result = EditorCanvas.SnapToGuideX(0.301f, 0.4f, 1920.0);
        Assert.Equal(0.3f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_NoCandidateWithinThreshold_ReturnsUnchanged()
    {
        // x=0.48, w=0.1 → left=0.48(dist 0.02), center=0.53(dist 0.03), right=0.58(dist 0.08) — all > threshold
        float result = EditorCanvas.SnapToGuideX(0.48f, 0.1f, 1920.0);
        Assert.Equal(0.48f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_MultipleCandidatesWithinThreshold_ClosestWins()
    {
        // x=0.4975, w=0.004 → left=0.4975(dist 0.0025), right=0.5015(dist 0.0015), center=0.4995(dist 0.0005)
        // center is closest → X = 0.5 - 0.002 = 0.498
        float result = EditorCanvas.SnapToGuideX(0.4975f, 0.004f, 1920.0);
        Assert.Equal(0.498f, result, precision: 4);
    }

    // ── SnapToGuideY ─────────────────────────────────────────────────────────

    [Fact]
    public void SnapToGuideY_TopEdgeWithinThreshold_SnapsTopEdgeToCenter()
    {
        // top edge at 0.499 → dist 0.001 < threshold → Y becomes 0.5
        float result = EditorCanvas.SnapToGuideY(0.499f, 0.3f, 1080.0);
        Assert.Equal(0.5f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_BottomEdgeWithinThreshold_SnapsBottomEdgeToCenter()
    {
        // y=0.2, h=0.299 → bottom at 0.499 → dist 0.001 < threshold → Y = 0.5 - 0.299 = 0.201
        float result = EditorCanvas.SnapToGuideY(0.2f, 0.299f, 1080.0);
        Assert.Equal(0.201f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_CenterWithinThreshold_CentersLayerVertically()
    {
        // y=0.301, h=0.4 → center at 0.501 → dist 0.001 < threshold → Y = 0.5 - 0.2 = 0.3
        float result = EditorCanvas.SnapToGuideY(0.301f, 0.4f, 1080.0);
        Assert.Equal(0.3f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_NoCandidateWithinThreshold_ReturnsUnchanged()
    {
        // y=0.46, h=0.05 → top=0.46(dist 0.04), bottom=0.51(dist 0.01), center=0.485(dist 0.015)
        // threshold=0.007407 — bottom dist 0.01 > threshold
        float result = EditorCanvas.SnapToGuideY(0.46f, 0.05f, 1080.0);
        Assert.Equal(0.46f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_MultipleCandidatesWithinThreshold_ClosestWins()
    {
        // y=0.4965, h=0.004 → top=0.4965(dist 0.0035), bottom=0.5005(dist 0.0005), center=0.4985(dist 0.0015)
        // bottom is closest → Y = 0.5 - 0.004 = 0.496
        float result = EditorCanvas.SnapToGuideY(0.4965f, 0.004f, 1080.0);
        Assert.Equal(0.496f, result, precision: 4);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~EditorCanvasSnapTests" -v
```

Expected: 10 failures — `EditorCanvas` has no `SnapToGuideX` or `SnapToGuideY` method yet.

- [ ] **Step 3: Add the two helper methods to `EditorCanvas.cs`**

In `Views/EditorCanvas.cs`, immediately after the `SnapY` method (around line 710), add:

```csharp
internal static float SnapToGuideX(float x, float w, double irWidth)
{
    const float guide = 0.5f;
    float thresh = (float)(8.0 / irWidth);

    float best = float.MaxValue;
    float result = x;

    float d = Math.Abs(x - guide);
    if (d < thresh && d < best) { best = d; result = guide; }

    float rx = x + w;
    d = Math.Abs(rx - guide);
    if (d < thresh && d < best) { best = d; result = guide - w; }

    float cx = x + w / 2f;
    d = Math.Abs(cx - guide);
    if (d < thresh && d < best) { best = d; result = guide - w / 2f; }

    return result;
}

internal static float SnapToGuideY(float y, float h, double irHeight)
{
    const float guide = 0.5f;
    float thresh = (float)(8.0 / irHeight);

    float best = float.MaxValue;
    float result = y;

    float d = Math.Abs(y - guide);
    if (d < thresh && d < best) { best = d; result = guide; }

    float by = y + h;
    d = Math.Abs(by - guide);
    if (d < thresh && d < best) { best = d; result = guide - h; }

    float cy = y + h / 2f;
    d = Math.Abs(cy - guide);
    if (d < thresh && d < best) { best = d; result = guide - h / 2f; }

    return result;
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~EditorCanvasSnapTests" -v
```

Expected: 10 tests pass.

- [ ] **Step 5: Wire up the call site in `OnPointerMoved`**

In `Views/EditorCanvas.cs`, in the `HandleKind.Move` `else` branch (single-layer path, around line 876), change:

```csharp
else
{
    layer.X = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
    layer.Y = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
}
```

to:

```csharp
else
{
    layer.X = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
    layer.Y = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
    layer.X = SnapToGuideX(layer.X, layer.Width,  ir.Width);
    layer.Y = SnapToGuideY(layer.Y, layer.Height, ir.Height);
}
```

`ir` is already declared earlier in `OnPointerMoved` (line 855) — no extra `GetImageRect()` call needed.

- [ ] **Step 6: Build and run the full test suite**

```
dotnet build
dotnet test ShowCast.Tests --no-build
```

Expected: build succeeds, all tests pass (10 new + existing 209).

- [ ] **Step 7: Manual verification**

Run the app. Open a page with at least one layer. Drag the layer toward the frame center.

Expected:
- When any edge midpoint or the layer center approaches x=0.5 or y=0.5, the layer snaps cleanly to that guide
- Snap releases naturally once the layer is dragged away past the threshold
- Multi-layer drag is unaffected (no snapping when multiple layers are selected)
- Behavior is the same regardless of whether the safe area overlay is visible

- [ ] **Step 8: Commit**

```bash
git add Views/EditorCanvas.cs ShowCast.Tests/Views/EditorCanvasSnapTests.cs
git commit -m "feat: snap layer edge midpoints to center crosshair guides while dragging"
```
