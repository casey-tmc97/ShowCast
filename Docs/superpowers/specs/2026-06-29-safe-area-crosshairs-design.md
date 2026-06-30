# Safe Area Crosshair Guides — Design Spec

**Date:** 2026-06-29

## Summary

Add centered horizontal and vertical crosshair lines to the safe area overlay in the page editor. The crosshairs span the full image frame and toggle on/off with the existing `ShowSafeBoundaries` control.

## Goal

Give operators a center reference point while composing graphics, without adding any new UI controls or toggles.

## Scope

Single file change: `Views/EditorCanvas.cs`, inside `RebuildSafeBoundaries()`.

## Design

### What changes

After the two `AddSafeRect` calls in `RebuildSafeBoundaries()`, append two `Line` elements to `_safeCanvas.Children`:

**Horizontal center line:**
- `StartPoint`: `(ir.X, ir.Y + ir.Height / 2)`
- `EndPoint`: `(ir.X + ir.Width, ir.Y + ir.Height / 2)`

**Vertical center line:**
- `StartPoint`: `(ir.X + ir.Width / 2, ir.Y)`
- `EndPoint`: `(ir.X + ir.Width / 2, ir.Y + ir.Height)`

### Visual style

- Stroke: white at ~60% opacity — `Color.FromArgb(150, 255, 255, 255)`
- StrokeThickness: `0.75`
- StrokeDashArray: `{ 8, 4 }` (dashed)
- `IsHitTestVisible = false`

Dashed white keeps the lines readable against any background without overpowering slide content, and visually distinguishes them from the solid safe-area rectangles.

### Behavior

- Shown and hidden by the same `ShowSafeBoundaries` toggle that controls the safe rectangles — no new VM property, no new button.
- Cleared automatically when `_safeCanvas.Children.Clear()` runs at the top of `RebuildSafeBoundaries()`.
- Recomputed whenever the canvas resizes (same trigger path as the safe rects via `RebuildSafeBoundaries`).

## What does NOT change

- `EditorCanvasViewModel` — no new properties.
- Toolbar/UI — no new controls.
- `AddSafeRect` helper — untouched.
- NDI path (`NdiSender`) — safe area overlay is an editor-only visual; NDI output is unaffected.
