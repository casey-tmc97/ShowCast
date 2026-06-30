# Crosshair Guide Snapping — Design Spec

**Date:** 2026-06-29

## Summary

When dragging a single layer in the page editor, the four perimeter center points (edge midpoints) of the layer's bounding box snap to the center crosshair guides (x=0.5 and y=0.5 in normalized coordinates). Snapping is always active — no toggle.

## Snap Targets

The center crosshair guides (added in the safe area overlay) define two snap lines:
- **Horizontal guide:** y = 0.5 (normalized)
- **Vertical guide:** x = 0.5 (normalized)

## Snap Points on the Layer

Four edge midpoints in normalized layer coordinates:
- **Top center:** `(X + W/2, Y)`
- **Bottom center:** `(X + W/2, Y + H)`
- **Left center:** `(X, Y + H/2)`
- **Right center:** `(X + W, Y + H/2)`

## Snap Logic

**Threshold:** 8 overlay pixels, converted to normalized at drag time:  
`threshX = 8.0 / ir.Width`, `threshY = 8.0 / ir.Height`

**`SnapToGuideX(float x, float w, double irWidth)`** — checks three candidates against x=0.5:
- Left edge: `x` (left edge at center)
- Right edge: `x + w` (right edge at center)
- Horizontal midpoint: `x + w/2` (layer horizontally centered)

Returns the adjusted `x` for whichever candidate is closest to 0.5 and within threshold. Returns `x` unchanged if none qualify.

**`SnapToGuideY(float y, float h, double irHeight)`** — checks three candidates against y=0.5:
- Top edge: `y` (top edge at center)
- Bottom edge: `y + h` (bottom edge at center)
- Vertical midpoint: `y + h/2` (layer vertically centered)

Returns the adjusted `y` for whichever candidate is closest to 0.5 and within threshold. Returns `y` unchanged if none qualify.

## Integration Point

In `EditorCanvas.cs`, `OnPointerMoved`, `HandleKind.Move` branch — **single layer only** (not the multi-selection `_origPositions` path). Applied after `SnapX`/`SnapY` as a second pass:

```csharp
case HandleKind.Move:
    // ... existing single-layer move logic ...
    layer.X = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
    layer.Y = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
    // new: guide snap pass
    var ir2 = GetImageRect();
    layer.X = SnapToGuideX(layer.X, layer.Width,  ir2.Width);
    layer.Y = SnapToGuideY(layer.Y, layer.Height, ir2.Height);
    break;
```

## What Does NOT Change

- `SnapX` / `SnapY` — untouched
- Multi-layer selection drag — excluded (YAGNI)
- `ShowSafeBoundaries` VM property — snapping is independent of crosshair visibility
- `SnapToGrid` VM property — guide snap runs regardless of grid snap setting
- NDI / OutputWindow paths — editor-only feature, no effect on output
