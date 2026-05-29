# Multi-Select Layers + Rich Text Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** (1) Add multi-layer selection with Ctrl+Click, Shift+Click, and marquee-drag; batch move, delete, duplicate, and alignment operations on selected layers. (2) Add per-span rich text formatting in text layers — user can highlight a range and change font size, family, bold, italic independently.

**Architecture:** Multi-select uses a new `LayerSelection` class in `Core/` that holds a `HashSet<SlideLayer>` and exposes selection operations. `MainViewModel.SelectedLayer` becomes the *primary* selection (last clicked); a new `MainViewModel.SelectedLayers` collection exposes all selected layers. `EditorCanvas` and `EditorLayerPanel` subscribe to `SelectedLayers` to draw multi-select indicators and dispatch batch ops. Rich text requires changing `SlideLayer.Text` from a plain string to a `List<TextSpan>` with per-span formatting; `PageRenderer.DrawText` is updated to render spans sequentially.

**Tech Stack:** C#/.NET 9, Avalonia, SkiaSharp, ReactiveUI, xUnit

---

> **NOTE — this plan should be executed LAST** as it touches the most files and requires the editor to be fully stable from prior plans. Rich text rendering requires a multi-pass layout engine.

---

### Task 1: LayerSelection — centralized selection manager

**Files:**
- Create: `Core/LayerSelection.cs`
- Modify: `ViewModels/MainViewModel.cs` (add `SelectedLayers`, update `SelectedLayer`)

- [ ] **Step 1: Write failing tests for LayerSelection**

```csharp
// ShowCast.Tests/Core/LayerSelectionTests.cs
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class LayerSelectionTests
{
    [Fact]
    public void Toggle_AddsLayerWhenNotSelected()
    {
        var sel   = new LayerSelection();
        var layer = new SlideLayer();
        sel.Toggle(layer);
        Assert.Contains(layer, sel.Layers);
    }

    [Fact]
    public void Toggle_RemovesLayerWhenAlreadySelected()
    {
        var sel   = new LayerSelection();
        var layer = new SlideLayer();
        sel.Toggle(layer);
        sel.Toggle(layer);
        Assert.DoesNotContain(layer, sel.Layers);
    }

    [Fact]
    public void SetSingle_ClearsPreviousAndSelectsOne()
    {
        var sel   = new LayerSelection();
        var a     = new SlideLayer();
        var b     = new SlideLayer();
        sel.Toggle(a);
        sel.SetSingle(b);
        Assert.DoesNotContain(a, sel.Layers);
        Assert.Contains(b, sel.Layers);
    }

    [Fact]
    public void Clear_EmptiesSelection()
    {
        var sel   = new LayerSelection();
        sel.Toggle(new SlideLayer());
        sel.Clear();
        Assert.Empty(sel.Layers);
    }

    [Fact]
    public void SelectRange_SelectsContiguousBlock()
    {
        var layers = Enumerable.Range(0, 5).Select(_ => new SlideLayer()).ToList();
        var sel    = new LayerSelection();
        sel.SetSingle(layers[1]);
        sel.SelectRange(layers, layers[3]);   // anchor=1, extend to 3
        Assert.Equal(3, sel.Layers.Count);    // layers 1,2,3
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (type not found)**

Run: `dotnet test ShowCast.Tests/ --filter "LayerSelectionTests" -v minimal`
Expected: compile error.

- [ ] **Step 3: Create LayerSelection**

```csharp
// Core/LayerSelection.cs
namespace ShowCast.Core;

public class LayerSelection
{
    readonly HashSet<SlideLayer> _set    = new();
    SlideLayer?                  _anchor;

    public IReadOnlyCollection<SlideLayer> Layers => _set;
    public bool IsSelected(SlideLayer layer) => _set.Contains(layer);
    public event Action? Changed;

    public void SetSingle(SlideLayer layer)
    {
        _set.Clear();
        _set.Add(layer);
        _anchor = layer;
        Changed?.Invoke();
    }

    public void Toggle(SlideLayer layer)
    {
        if (!_set.Remove(layer))
        {
            _set.Add(layer);
            _anchor = layer;
        }
        Changed?.Invoke();
    }

    public void SelectRange(IList<SlideLayer> ordered, SlideLayer extend)
    {
        if (_anchor is null) { SetSingle(extend); return; }
        int anchorIdx = ordered.IndexOf(_anchor);
        int extendIdx = ordered.IndexOf(extend);
        if (anchorIdx < 0 || extendIdx < 0) { SetSingle(extend); return; }
        int lo = Math.Min(anchorIdx, extendIdx);
        int hi = Math.Max(anchorIdx, extendIdx);
        _set.Clear();
        for (int i = lo; i <= hi; i++) _set.Add(ordered[i]);
        Changed?.Invoke();
    }

    public void Clear()
    {
        _set.Clear();
        _anchor = null;
        Changed?.Invoke();
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ShowCast.Tests/ --filter "LayerSelectionTests" -v minimal`
Expected: PASS all 5 tests.

- [ ] **Step 5: Add SelectedLayers to MainViewModel**

In `ViewModels/MainViewModel.cs`, add after `SelectedLayer`:

```csharp
public LayerSelection SelectedLayers { get; } = new();
```

Update `SelectedLayer` setter to also update `SelectedLayers.SetSingle` when a single selection is made from the layer list:

```csharp
private SlideLayer? _selectedLayer;
public SlideLayer? SelectedLayer
{
    get => _selectedLayer;
    set
    {
        this.RaiseAndSetIfChanged(ref _selectedLayer, value);
        if (value is not null) SelectedLayers.SetSingle(value);
        else                   SelectedLayers.Clear();
    }
}
```

- [ ] **Step 6: Commit**

```bash
git add Core/LayerSelection.cs ViewModels/MainViewModel.cs ShowCast.Tests/Core/LayerSelectionTests.cs
git commit -m "feat(editor): add LayerSelection manager for multi-select state"
```

---

### Task 2: Multi-select in EditorLayerPanel (Ctrl+Click, Shift+Click)

**Files:**
- Modify: `Views/EditorLayerPanel.axaml.cs` (intercept pointer events for Ctrl/Shift)
- Modify: `Views/EditorLayerPanel.axaml` (bind selected/multi-selected visual state)

- [ ] **Step 1: Override OnSelectionChanged to route Ctrl+Click to Toggle**

In `Views/EditorLayerPanel.axaml.cs`, replace `OnSelectionChanged`:

```csharp
void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
{
    if (_syncingSelection || VM is null) return;
    if (LayerList.SelectedItem is not SlideLayer layer) return;

    var mods = Avalonia.Input.KeyboardDevice.CurrentModifiers;
    bool ctrl  = mods.HasFlag(Avalonia.Input.KeyModifiers.Control);
    bool shift = mods.HasFlag(Avalonia.Input.KeyModifiers.Shift);

    if (ctrl)
    {
        VM.SelectedLayers.Toggle(layer);
        VM.SelectedLayer = layer;
    }
    else if (shift)
    {
        var ordered = VM.EditingLayers.ToList();
        VM.SelectedLayers.SelectRange(ordered, layer);
        VM.SelectedLayer = layer;
    }
    else
    {
        VM.SelectedLayer = layer;   // SetSingle is called inside SelectedLayer setter
    }
}
```

- [ ] **Step 2: Show multi-select highlight in AXAML**

In `Views/EditorLayerPanel.axaml`, the `DataTemplate` for each layer row needs a multi-select visual state. Add a `FuncValueConverter` in the code-behind:

```csharp
// In EditorLayerPanel.axaml.cs:
public static readonly FuncValueConverter<bool, Avalonia.Media.IBrush> MultiSelectBrush =
    new(selected => selected
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#1a3a6a"))
        : Avalonia.Media.Brushes.Transparent);
```

Then in the AXAML `Grid` inside the `DataTemplate`, add a background binding that reads from a multi-select-aware property. (The exact binding path requires `SlideLayer` to have an `IsMultiSelected` property, or use `RelativeSource` to the VM. The simpler approach is to replace `ListBox` with an `ItemsControl` + custom pointer handling. This step is deferred — implement basic Ctrl/Shift selection first without visual highlight, then add visual as a follow-on.)

- [ ] **Step 3: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Manual test**

1. Open editor with 3 layers
2. Ctrl+Click two layers in the panel — both should be individually selectable
3. Shift+Click to range-select — verify the range is selected

- [ ] **Step 5: Commit**

```bash
git add Views/EditorLayerPanel.axaml.cs
git commit -m "feat(editor): Ctrl+Click and Shift+Click multi-select in layer panel"
```

---

### Task 3: Batch move in EditorCanvas (arrow keys + drag move all selected)

**Files:**
- Modify: `Views/EditorCanvas.cs` (`OnPointerMoved` drag move path)
- Modify: `Views/PageEditorOverlay.axaml.cs` (arrow key movement)

- [ ] **Step 1: Update drag move to move all selected layers**

In `Views/EditorCanvas.cs`, in `OnPointerMoved` `HandleKind.Move` case, replace the single-layer move with:

```csharp
case HandleKind.Move:
    float newX = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
    float newY = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
    float dxModel = newX - _origX;
    float dyModel = newY - _origY;
    layer.X = newX;
    layer.Y = newY;
    // Move all other selected layers by the same delta
    if (_vm?.SelectedLayers is { } sel)
    {
        foreach (var other in sel.Layers)
        {
            if (other == layer) continue;
            other.X = Math.Clamp(other.X + dxModel, 0f, 1f - other.Width);
            other.Y = Math.Clamp(other.Y + dyModel, 0f, 1f - other.Height);
        }
    }
    break;
```

NOTE: `_origX`/`_origY` are captured at drag start for the primary layer. For a full implementation, you need to capture orig positions for ALL selected layers at drag start. Add a `Dictionary<SlideLayer, (float x, float y)> _origPositions` field and populate it in `StartDrag` when `kind == HandleKind.Move`.

Full `StartDrag` with multi-layer position capture:

```csharp
void StartDrag(HandleKind kind, Point pt)
{
    _vm?.BeginLayerEdit();
    _dragging   = true;
    _dragKind   = kind;
    _dragOrigin = pt;
    var l = _vm!.SelectedLayer!;
    _origX = l.X; _origY = l.Y; _origW = l.Width; _origH = l.Height;

    // Capture positions of all selected layers for batch move
    _origPositions.Clear();
    if (kind == HandleKind.Move && _vm.SelectedLayers is { } sel)
    {
        foreach (var other in sel.Layers)
            _origPositions[other] = (other.X, other.Y);
    }

    if (kind == HandleKind.Rotate)
    {
        var ir   = GetImageRect();
        float cx = (float)(ir.X + (_origX + _origW / 2) * ir.Width);
        float cy = (float)(ir.Y + (_origY + _origH / 2) * ir.Height);
        _rotDragAngle0  = Math.Atan2(pt.Y - cy, pt.X - cx) * 180.0 / Math.PI;
        _rotDragOrigDeg = l.RotationDegrees;
    }
}
```

Add the field: `readonly Dictionary<SlideLayer, (float x, float y)> _origPositions = new();`

Update the `Move` case in `OnPointerMoved`:

```csharp
case HandleKind.Move:
    float newX = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
    float newY = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
    float deltaX = newX - _origX;
    float deltaY = newY - _origY;
    layer.X = newX;
    layer.Y = newY;
    foreach (var (other, (ox, oy)) in _origPositions)
    {
        if (other == layer) continue;
        other.X = Math.Clamp(ox + deltaX, 0f, 1f - other.Width);
        other.Y = Math.Clamp(oy + deltaY, 0f, 1f - other.Height);
    }
    break;
```

- [ ] **Step 2: Update arrow key movement to move all selected layers**

In `Views/PageEditorOverlay.axaml.cs`, update the arrow key block to also move selected layers:

```csharp
if (VM?.SelectedLayer is { } layer && !ctrl)
{
    float stepX = VM.SnapToGrid && VM.GridSpacing > 0 ? (float)(VM.GridSpacing/1920.0) : 1f/1920f;
    float stepY = VM.SnapToGrid && VM.GridSpacing > 0 ? (float)(VM.GridSpacing/1080.0) : 1f/1080f;
    float dx = 0, dy = 0;
    switch (e.Key)
    {
        case Key.Left:  dx = -stepX; break;
        case Key.Right: dx =  stepX; break;
        case Key.Up:    dy = -stepY; break;
        case Key.Down:  dy =  stepY; break;
        default: goto skipMove;
    }
    VM.BeginLayerEdit();
    foreach (var l in VM.SelectedLayers.Layers)
    {
        l.X = Math.Clamp(l.X + dx, 0f, 1f - l.Width);
        l.Y = Math.Clamp(l.Y + dy, 0f, 1f - l.Height);
    }
    VM.NotifySlideChanged();
    e.Handled = true;
    return;
    skipMove:;
}
```

- [ ] **Step 3: Run build and tests**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Run: `dotnet test ShowCast.Tests/ -v minimal`
Expected: no errors, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add Views/EditorCanvas.cs Views/PageEditorOverlay.axaml.cs
git commit -m "feat(editor): batch move all selected layers during drag and arrow-key movement"
```

---

### Task 4: Batch delete and duplicate for multi-selection

**Files:**
- Modify: `ViewModels/MainViewModel.cs` (`DeleteLayer`, `DuplicateLayer`)
- Modify: `Views/PageEditorOverlay.axaml.cs` (Delete key handler)

- [ ] **Step 1: Update DeleteLayer to handle multi-selection**

In `ViewModels/MainViewModel.cs`, add a new `DeleteSelectedLayers()` method:

```csharp
public void DeleteSelectedLayers()
{
    if (EditingPage is null) return;
    var toDelete = SelectedLayers.Layers.ToList();
    if (toDelete.Count == 0 && SelectedLayer is not null) toDelete.Add(SelectedLayer);
    BeginLayerEdit();
    foreach (var layer in toDelete)
    {
        if (SelectedLayer == layer) SelectedLayer = null;
        EditingPage.RemoveLayer(layer.Id);
    }
    SelectedLayers.Clear();
    RefreshEditorLayers();
    NotifySlideChanged();
}
```

- [ ] **Step 2: Update DuplicateLayer to handle multi-selection**

In `ViewModels/MainViewModel.cs`, add `DuplicateSelectedLayers()`:

```csharp
public void DuplicateSelectedLayers()
{
    if (EditingPage is null) return;
    var toDuplicate = SelectedLayers.Layers.Count > 0
        ? SelectedLayers.Layers.ToList()
        : (SelectedLayer is not null ? new List<SlideLayer> { SelectedLayer } : new List<SlideLayer>());
    if (toDuplicate.Count == 0) return;
    BeginLayerEdit();
    int maxZ = EditingPage.Layers.Max(l => l.ZOrder);
    SlideLayer? lastCopy = null;
    foreach (var layer in toDuplicate)
    {
        var copy   = layer.Clone(newId: true);
        copy.ZOrder = ++maxZ;
        copy.Name  += " Copy";
        copy.X     = Math.Clamp(copy.X + 0.02f, 0f, 0.98f);
        copy.Y     = Math.Clamp(copy.Y + 0.02f, 0f, 0.98f);
        EditingPage.AddLayer(copy);
        lastCopy = copy;
    }
    RefreshEditorLayers();
    if (lastCopy is not null) SelectedLayer = lastCopy;
    NotifySlideChanged();
}
```

- [ ] **Step 3: Wire Ctrl+D and Delete key to multi-selection methods**

In `Views/PageEditorOverlay.axaml.cs`, replace:

```csharp
if (ctrl && e.Key == Key.D) { VM?.DuplicateLayer(VM?.SelectedLayer); e.Handled = true; return; }
// ...
if (e.Key == Key.Delete || e.Key == Key.Back)
{
    if (VM?.SelectedLayer is { } layer) VM.DeleteLayer(layer);
    e.Handled = true;
}
```

With:

```csharp
if (ctrl && e.Key == Key.D) { VM?.DuplicateSelectedLayers(); e.Handled = true; return; }
// ...
if (e.Key == Key.Delete || e.Key == Key.Back)
{
    VM?.DeleteSelectedLayers();
    e.Handled = true;
}
```

- [ ] **Step 4: Run build and tests**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Run: `dotnet test ShowCast.Tests/ -v minimal`
Expected: no errors, all pass.

- [ ] **Step 5: Commit**

```bash
git add ViewModels/MainViewModel.cs Views/PageEditorOverlay.axaml.cs
git commit -m "feat(editor): batch delete and duplicate operate on all selected layers"
```

---

### Task 5: Rich text — TextSpan model

> **This task is the most complex and changes the serialized data format.** Complete all other plans first. Rich text requires migrating `SlideLayer.Text` from a plain string to `List<TextSpan>` and updating `PageRenderer.DrawText` to do per-span layout.

**Files:**
- Create: `Core/TextSpan.cs`
- Modify: `Core/SlideLayer.cs` (add `Spans`, keep `Text` for legacy compatibility)
- Modify: `Engine/PageRenderer.cs` (`DrawText`)
- Modify: `Core/ShowFileSerializer.cs` (add v2 migration: convert `Text` → first span)
- Modify: `ShowFile.CurrentVersion` (bump to 2)

- [ ] **Step 1: Define TextSpan model**

```csharp
// Core/TextSpan.cs
namespace ShowCast.Core;

public class TextSpan
{
    public string    Text       { get; set; } = "";
    public float?    FontSize   { get; set; }   // null = inherit from layer
    public string?   FontFamily { get; set; }   // null = inherit
    public bool?     Bold       { get; set; }
    public bool?     Italic     { get; set; }
    public SkiaSharp.SKColor? Color { get; set; }  // null = inherit
}
```

- [ ] **Step 2: Add Spans to SlideLayer (with backward-compat Text fallback)**

In `Core/SlideLayer.cs`, add:

```csharp
public List<TextSpan> Spans { get; } = new();

// Computed text for display: concat all spans, or fall back to Text field for legacy layers.
public string EffectiveText => Spans.Count > 0
    ? string.Concat(Spans.Select(s => s.Text))
    : Text;
```

- [ ] **Step 3: Update PageRenderer.DrawText to render spans**

In `Engine/PageRenderer.cs`, `DrawText` needs to render each span with its own font properties:

```csharp
static void DrawText(SKCanvas canvas, SlideLayer layer, int w, int h, bool useLiveTimers = true)
{
    // Timer binding overrides rich text — show live timer value as plain text
    if (useLiveTimers && layer.TimerBinding is { } tid
        && TimerTextCache.Values.TryGetValue(tid, out var tv))
    {
        DrawPlainText(canvas, layer, tv, w, h);
        return;
    }

    if (layer.Spans.Count == 0)
    {
        DrawPlainText(canvas, layer, layer.Text, w, h);
        return;
    }

    // Rich text: draw each span sequentially
    // (Basic horizontal layout — each span continues from where previous ended)
    DrawSpans(canvas, layer, w, h);
}
```

`DrawSpans` is a new helper that handles per-span font properties and line-wrapping. This is a significant layout engine; a minimal implementation positions spans left-to-right, word-wrapping at the layer boundary.

Full implementation of `DrawSpans` requires:
1. Measuring each span's text width with its font
2. Building a list of lines respecting word-wrap
3. Drawing each span's fragment with its specific paint settings

This is ~100 lines of layout code. The detailed implementation is provided in the executing session.

- [ ] **Step 4: Add migration v1→v2 in ShowFileSerializer**

In `Core/ShowFileSerializer.cs`:
- Bump `ShowFile.CurrentVersion` from 1 to 2
- Add migration at index 0 (v1→v2): for every SlideLayer with `Text != ""` and empty `Spans`, create a single `TextSpan` with `Text = layer.Text` and null format overrides

```csharp
static readonly List<Action<ShowFile>> Migrations = new()
{
    // v1 → v2: convert plain Text to first TextSpan
    file =>
    {
        foreach (var show in file.Shows)
            foreach (var pkg in show.Packages)
                foreach (var page in pkg.Pages)
                    foreach (var layer in page.Layers)
                        if (layer.Type == LayerType.Text && layer.Spans.Count == 0
                            && !string.IsNullOrEmpty(layer.Text))
                            layer.Spans.Add(new TextSpan { Text = layer.Text });
    }
};
```

- [ ] **Step 5: Add rich text editing UI in EditorInspectorPanel**

When a text layer is selected and the inline text box (`EditorCanvas.BeginInlineEdit`) is open, the inspector should show per-span formatting controls. The trigger: selecting text in the inline box raises a `SelectionChanged` event; read the selection range, find the spans it covers, update per-span properties.

This requires:
1. `EditorCanvas.BeginInlineEdit` to notify the inspector of the active inline box
2. The inspector listens for text selection changes and offers span-level controls

A full rich text UI is out of scope for this initial implementation. The model and renderer changes above provide the data foundation; the UI can be added iteratively.

- [ ] **Step 6: Run tests and build**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Run: `dotnet test ShowCast.Tests/ -v minimal`
Expected: all pass.

- [ ] **Step 7: Commit**

```bash
git add Core/TextSpan.cs Core/SlideLayer.cs Core/ShowFile.cs Core/ShowFileSerializer.cs Engine/PageRenderer.cs
git commit -m "feat(editor): rich text TextSpan model with per-span font size/family/bold/italic"
```
