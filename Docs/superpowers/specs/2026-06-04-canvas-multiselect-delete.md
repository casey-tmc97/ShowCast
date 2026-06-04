# Canvas Multi-Select & Delete Key

**Date:** 2026-06-04  
**Status:** Approved

## Overview

Two related features for the page editor canvas:

1. **Delete key** — pressing Delete/Backspace while a layer is selected deletes it
2. **Rubber-band multi-select** — drag on empty canvas space to select multiple layers; bulk delete, move, and visibility toggle apply to all selected layers

## State Model

`MainViewModel` gets a new `SelectedLayers: HashSet<SlideLayer>` backed by a reactive property.

Sync rules:
- When `SelectedLayer` is set (single click), `SelectedLayers` is replaced with `{ layer }` or cleared if null. All existing single-select code continues to work unchanged.
- When rubber-band selection fires, `SelectedLayers` is set to the intersecting layer set and `SelectedLayer` is set to the topmost (highest ZOrder) layer in that set, so the inspector always shows something useful.
- `SelectedLayer` is always a member of `SelectedLayers`, or both are null/empty.

A new VM method `DeleteSelectedLayers()` deletes all layers in `SelectedLayers` in a single history step (wraps `BeginLayerEdit` + loop of `RemoveLayer` + `NotifySlideChanged`).

## Delete Key

- `EditorCanvas` (`UserControl`) gains `Focusable = true`
- Canvas captures focus on pointer press so keyboard events land on it
- `KeyDown` handler checks for `Key.Delete` or `Key.Back`
- Guard: if `_textEditor is not null`, do nothing — inline text editing takes priority
- Otherwise calls `_vm.DeleteSelectedLayers()`

## Rubber-Band Selection

**Activation:** pointer pressed on canvas when no handle and no layer is hit. Rubber-band only activates once the pointer has moved more than 4px from the press point, preventing accidental marquees on simple clicks.

**During drag:** a `Rectangle` with semi-transparent blue fill (`rgba(59, 130, 246, 0.15)`) and solid blue stroke is drawn on the overlay, updating every pointer-move tick.

**On release:** compute the drag rectangle in normalized slide coords (0–1 range). Select all non-locked layers whose bounding box **intersects** the rectangle (partial overlap counts). Set `SelectedLayers` to the result; set `SelectedLayer` to the topmost intersecting layer. Remove the rubber-band rectangle from the overlay.

**Single click on empty space** still deselects (unchanged behaviour, because the 4px threshold is never crossed).

## Multi-Select Move

When a `HandleKind.Move` drag begins and `SelectedLayers` has more than one layer:

- Snapshot `(X, Y)` for **all** layers in `SelectedLayers` at drag start (stored in a `Dictionary<SlideLayer, (float X, float Y)>`)
- Each pointer-move tick applies the same normalized `(dx, dy)` delta to all snapshotted layers
- Resize and rotate handles operate only on the primary `SelectedLayer` — no group resize

On pointer release, `NotifySlideChanged()` records a single undo step covering all moved layers.

## Visual Treatment

Three visual states on the canvas:

| State | Appearance |
|---|---|
| Unselected | Faint dashed grey outline (existing, unchanged) |
| In selection set, not primary | Solid blue outline, no handles |
| Primary selected (`SelectedLayer`) | Blue border + 8 resize handles + rotation handle (existing, unchanged) |

Rubber-band drag rectangle: `rgba(59, 130, 246, 0.15)` fill, 1px solid blue stroke, drawn above all layer outlines while dragging.

## Bulk Operations

| Operation | Applies to |
|---|---|
| Delete (Delete/Backspace) | All `SelectedLayers`, single undo step |
| Move | All `SelectedLayers`, same delta |
| Visibility toggle (eye button in layer panel) | All `SelectedLayers` |

Copy/paste with multi-select is out of scope.

## Out of Scope

- Group resize / group rotate
- Copy/paste of multi-selection
- Shift-click to add/remove individual layers from selection
