# Text Editor Ctrl+Click Move Design

**Date:** 2026-06-11
**Status:** Approved

## Summary

While the inline text editor is active, Ctrl+clicking inside the text layer commits the text edit and initiates a move drag on the layer. This allows repositioning a text layer without first clicking outside to exit text editing mode.

## Scope

- Ctrl+click inside a text layer while its inline editor is active → commit edit, start move drag
- Regular click inside the active text layer → existing behavior (reposition text cursor)
- Ctrl+click outside the text layer while editor is active → existing behavior (commit edit, hit-test falls through to normal canvas logic)
- No change to drag behavior once move starts (OnPointerMoved, OnPointerReleased unchanged)
- After drag ends, text editor is closed; user clicks the layer again to re-enter editing

## Out of Scope

- Keeping the inline editor open during the drag (overlay controls can't follow the layer during a move tick without significant repositioning logic)
- Ctrl+drag without first Ctrl+clicking (drag is initiated from pointer pressed, not moved)
- Any change to non-text layer move behavior

## File Map

| File | Change |
|------|--------|
| `Views/EditorCanvas.cs` | Add Ctrl check in the "text editor active" block of `OnPointerPressed` |

## Design

### `OnPointerPressed` change

The existing "text editor active" block in `OnPointerPressed` (near line 693) currently:

```
if text editor active:
    if click inside text layer → _textEditor.OnPointerPressed(pt)  // cursor placement
    else                       → EndCustomEdit()                   // commit + close
```

Add a Ctrl branch before the cursor-placement path:

```
if text editor active:
    if click inside text layer:
        if Ctrl held → EndCustomEdit(); StartDrag(HandleKind.Move, pt)
        else         → _textEditor.OnPointerPressed(pt)           // existing
    else             → EndCustomEdit()                            // existing
```

`EndCustomEdit()` commits the current text edit and clears `_textEditor`. `StartDrag(HandleKind.Move, pt)` then proceeds exactly as if the user had clicked to move the layer in normal (non-editing) mode.

### Why EndCustomEdit first

`CanvasTextEditor` overlay controls (cursor blink, selection rectangles, IME box) are positioned at fixed canvas coordinates derived from the layer's position at editor-open time. During a move drag, `RebuildSlide()` updates the slide bitmap each tick but the overlay controls stay put, producing a misaligned cursor. Closing the editor before the drag avoids this artifact and keeps the implementation minimal.

### Ctrl modifier detection

```csharp
bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);
```

`KeyModifiers` is in `Avalonia.Input`, already imported in `EditorCanvas.cs`.
