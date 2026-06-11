# Editor Canvas Zoom Design

**Date:** 2026-06-11
**Status:** Approved

## Summary

Add cursor-anchored zoom to the editor canvas. Ctrl+scroll zooms in/out. The slide point under the cursor stays fixed. Zoom resets to 100% when switching pages.

## Scope

- Ctrl+scroll up → zoom in; Ctrl+scroll down → zoom out
- Zoom range: 25%–400% (0.25–4.0)
- Cursor-anchored: the normalized slide coordinate under the cursor stays at the same screen position as zoom changes
- Zoom resets to 100% when a new page is opened in the editor
- Bare scroll (no Ctrl) passes through unchanged — panel scrolls normally

## Out of Scope

- Manual pan / middle-mouse drag
- Zoom indicator / percentage label
- Keyboard shortcuts (Ctrl+Plus / Ctrl+0)
- Persisting zoom level across page switches

## File Map

| File | Change |
|------|--------|
| `Views/EditorCanvas.cs` | Add zoom fields, scroll handler, modify `GetImageRect()`, update slide image positioning |

## Design

### State

Two new private fields in `EditorCanvas`:

```csharp
double _zoomLevel      = 1.0;          // clamped to [0.25, 4.0]
Point  _zoomOriginNorm = new(0.5, 0.5); // normalized (0-1) slide coord kept fixed during zoom
```

Reset both to defaults at the start of the existing private `RebuildSlide()` method (already called on page open and page switch):

```csharp
void RebuildSlide()
{
    _zoomLevel      = 1.0;
    _zoomOriginNorm = new Point(0.5, 0.5);
    // ... rest of existing method unchanged
```

### Input Handler

Register on `_overlay` in the constructor:

```csharp
_overlay.PointerWheelChanged += OnWheelZoom;
```

Handler:

```csharp
void OnWheelZoom(object? sender, PointerWheelEventArgs e)
{
    if (!e.KeyModifiers.HasFlag(KeyModifiers.Control)) return;

    var cursor = e.GetPosition(_overlay);

    // Compute base (un-zoomed) letterbox rect to derive the normalized origin
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

`_zoomOriginNorm` is computed against the **base** letterbox rect (not the zoomed rect), so successive scrolls always anchor correctly to the same content point regardless of current zoom level.

### Modified `GetImageRect()`

```csharp
Rect GetImageRect()
{
    double cw = _overlay.Bounds.Width, ch = _overlay.Bounds.Height;
    if (cw <= 0 || ch <= 0) return new Rect(0, 0, cw, ch);
    const double aspect = 16.0 / 9.0;
    double iw, ih;
    if (cw / ch > aspect) { ih = ch; iw = ih * aspect; }
    else                  { iw = cw; ih = iw / aspect; }
    double baseX = (cw - iw) / 2;
    double baseY = (ch - ih) / 2;

    if (_zoomLevel == 1.0) return new Rect(baseX, baseY, iw, ih);

    double zw = iw * _zoomLevel;
    double zh = ih * _zoomLevel;

    // Screen position of the origin in the base (un-zoomed) rect
    double originSX = baseX + _zoomOriginNorm.X * iw;
    double originSY = baseY + _zoomOriginNorm.Y * ih;

    // Shift so the origin stays at the same screen position after scaling
    double x = originSX - _zoomOriginNorm.X * zw;
    double y = originSY - _zoomOriginNorm.Y * zh;

    return new Rect(x, y, zw, zh);
}
```

### Slide Image Positioning

`_slideImg` is declared with `Stretch = Stretch.Uniform` and lives in a `Grid` cell alongside `_overlay`, `_gridCanvas`, and `_safeCanvas`. At zoom=1 it letterboxes naturally. At other zoom levels it must be positioned explicitly.

Add a helper called at the end of `RebuildSlide()` (after `UpdateHandles()`):

```csharp
void UpdateSlideLayout()
{
    var ir = GetImageRect();
    _slideImg.Width  = ir.Width;
    _slideImg.Height = ir.Height;
    _slideImg.HorizontalAlignment = HorizontalAlignment.Left;
    _slideImg.VerticalAlignment   = VerticalAlignment.Top;
    _slideImg.Margin  = new Thickness(ir.X, ir.Y, 0, 0);
    _slideImg.Stretch = Stretch.Fill; // size is now controlled manually
}
```

`ir.X` and `ir.Y` are relative to the Grid cell's top-left corner, which is the same coordinate space as `_overlay` (all siblings in the same `cell` Grid). The overlays (`_overlay` children — handles, selection border, etc.) already use `GetImageRect()` coordinates, so they align automatically.

`RebuildGrid()` and `RebuildSafeBoundaries()` also use `GetImageRect()` internally and need no changes.

### Zoom Reset

`_zoomLevel = 1.0` at the start of `RebuildSlide()` (see State section). Since `RebuildSlide()` is already called by `SwitchEditingPage` and `OpenEditor`, zoom resets correctly on every page change.
