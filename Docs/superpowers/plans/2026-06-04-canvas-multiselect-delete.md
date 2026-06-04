# Canvas Multi-Select & Delete Key Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Delete-key layer deletion and rubber-band multi-select with bulk move/delete/visibility-toggle to the page editor canvas.

**Architecture:** `MainViewModel` gains a `SelectedLayers: HashSet<SlideLayer>` reactive property kept in sync with `SelectedLayer` (single-select setter replaces the set; multi-select method sets the whole set and picks the primary). `EditorCanvas` handles keyboard Delete, rubber-band drag, multi-layer move delta, and secondary-selection visuals. `EditorLayerPanel` eye button is extended to toggle all selected layers when more than one is selected.

**Tech Stack:** C# 12 / .NET 9, Avalonia UI, ReactiveUI (`RaiseAndSetIfChanged`, `WhenAnyValue`), SkiaSharp (canvas rendering), xUnit (tests).

---

## File Map

| File | Change |
|---|---|
| `ViewModels/MainViewModel.cs` | Add `SelectedLayers`, `SetMultiSelection`, `DeleteSelectedLayers`, `ToggleVisibilityForSelected`; update `SelectedLayer` setter |
| `Views/EditorCanvas.cs` | Focusable + KeyDown; rubber-band state/draw/finalize; multi-layer move snapshot; secondary selection highlights; subscribe to `SelectedLayers` |
| `Views/EditorLayerPanel.axaml.cs` | Update `OnEyeClick` to use `ToggleVisibilityForSelected` when multi-selected |
| `ShowCast.Tests/ViewModels/MultiSelectTests.cs` | New — tests for VM multi-select behaviour |

---

## Task 1: Add multi-select state and bulk operations to MainViewModel

**Files:**
- Modify: `ViewModels/MainViewModel.cs:1116-1121` (SelectedLayer property)
- Create: `ShowCast.Tests/ViewModels/MultiSelectTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ShowCast.Tests/ViewModels/MultiSelectTests.cs`:

```csharp
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MultiSelectTests
{
    static MainViewModel MakeVmWithTwoLayers(out SlideLayer a, out SlideLayer b)
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg  = show.Packages.Last();
        var page = new Page();
        a = new SlideLayer { Type = LayerType.Text, Name = "A", ZOrder = 1 };
        b = new SlideLayer { Type = LayerType.Text, Name = "B", ZOrder = 2 };
        page.AddLayer(a);
        page.AddLayer(b);
        pkg.AddPage(page);
        vm.OpenEditor(new PageViewModel(page, pkg));
        return vm;
    }

    [Fact]
    public void SelectedLayer_set_SyncsSelectedLayersToSingleton()
    {
        var vm = MakeVmWithTwoLayers(out var a, out _);
        vm.SelectedLayer = a;
        Assert.Single(vm.SelectedLayers);
        Assert.Contains(a, vm.SelectedLayers);
    }

    [Fact]
    public void SelectedLayer_setNull_ClearsSelectedLayers()
    {
        var vm = MakeVmWithTwoLayers(out var a, out _);
        vm.SelectedLayer = a;
        vm.SelectedLayer = null;
        Assert.Empty(vm.SelectedLayers);
    }

    [Fact]
    public void SetMultiSelection_SetsCollectionAndPicksTopmostAsPrimary()
    {
        var vm = MakeVmWithTwoLayers(out var a, out var b);
        // b has ZOrder=2 (higher), a has ZOrder=1
        vm.SetMultiSelection(new[] { a, b });
        Assert.Equal(2, vm.SelectedLayers.Count);
        Assert.Contains(a, vm.SelectedLayers);
        Assert.Contains(b, vm.SelectedLayers);
        Assert.Equal(b, vm.SelectedLayer); // topmost by ZOrder
    }

    [Fact]
    public void DeleteSelectedLayers_RemovesAllFromPage()
    {
        var vm = MakeVmWithTwoLayers(out var a, out var b);
        vm.SetMultiSelection(new[] { a, b });
        vm.DeleteSelectedLayers();
        Assert.Empty(vm.EditingPage!.Layers);
        Assert.Null(vm.SelectedLayer);
        Assert.Empty(vm.SelectedLayers);
    }

    [Fact]
    public void ToggleVisibilityForSelected_TogglesAllSelectedLayers()
    {
        var vm = MakeVmWithTwoLayers(out var a, out var b);
        a.Visible = true; b.Visible = true;
        vm.SetMultiSelection(new[] { a, b });
        vm.ToggleVisibilityForSelected();
        Assert.False(a.Visible);
        Assert.False(b.Visible);
        // Selection should be preserved
        Assert.Equal(2, vm.SelectedLayers.Count);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test ShowCast.Tests --filter "FullyQualifiedName~MultiSelectTests" --no-build
```

Expected: compile error — `SelectedLayers`, `SetMultiSelection`, `DeleteSelectedLayers`, `ToggleVisibilityForSelected` not defined yet.

- [ ] **Step 3: Add `SelectedLayers` property and update `SelectedLayer` setter**

In `ViewModels/MainViewModel.cs`, replace the existing `SelectedLayer` property (lines 1116–1121) with:

```csharp
private HashSet<SlideLayer> _selectedLayers = new();
public HashSet<SlideLayer> SelectedLayers
{
    get => _selectedLayers;
    private set => this.RaiseAndSetIfChanged(ref _selectedLayers, value);
}

private SlideLayer? _selectedLayer;
public SlideLayer? SelectedLayer
{
    get => _selectedLayer;
    set
    {
        this.RaiseAndSetIfChanged(ref _selectedLayer, value);
        SelectedLayers = value is null
            ? new HashSet<SlideLayer>()
            : new HashSet<SlideLayer> { value };
    }
}
```

- [ ] **Step 4: Add `SetMultiSelection`, `DeleteSelectedLayers`, `ToggleVisibilityForSelected`**

Directly after the `SelectedLayer` property block, add:

```csharp
public void SetMultiSelection(IEnumerable<SlideLayer> layers)
{
    var set = new HashSet<SlideLayer>(layers);
    SelectedLayers = set;
    // Set primary without re-triggering the SelectedLayer setter (which would clear the set)
    _selectedLayer = set.OrderByDescending(l => l.ZOrder).FirstOrDefault();
    this.RaisePropertyChanged(nameof(SelectedLayer));
}

public void DeleteSelectedLayers()
{
    if (EditingPage is null || SelectedLayers.Count == 0) return;
    foreach (var layer in SelectedLayers.ToList())
        EditingPage.RemoveLayer(layer.Id);
    _selectedLayer = null;
    this.RaisePropertyChanged(nameof(SelectedLayer));
    SelectedLayers = new HashSet<SlideLayer>();
    RefreshEditorLayers();
    NotifySlideChanged();
}

public void ToggleVisibilityForSelected()
{
    if (SelectedLayers.Count == 0) return;
    var savedSet     = new HashSet<SlideLayer>(SelectedLayers);
    var savedPrimary = _selectedLayer;
    foreach (var layer in savedSet)
        layer.Visible = !layer.Visible;
    RefreshEditorLayers();
    // Restore multi-selection bypassing the SelectedLayer setter
    SelectedLayers  = savedSet;
    _selectedLayer  = savedPrimary;
    this.RaisePropertyChanged(nameof(SelectedLayer));
    NotifySlideChanged();
}
```

- [ ] **Step 5: Build and run tests**

```
dotnet build ShowCast.sln
dotnet test ShowCast.Tests --filter "FullyQualifiedName~MultiSelectTests"
```

Expected: all 5 tests pass.

- [ ] **Step 6: Commit**

```
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MultiSelectTests.cs
git commit -m "feat(editor): add SelectedLayers, SetMultiSelection, DeleteSelectedLayers, ToggleVisibilityForSelected"
```

---

## Task 2: Delete key in EditorCanvas

**Files:**
- Modify: `Views/EditorCanvas.cs` (constructor, `OnPointerPressed`, new `OnKeyDown`)

- [ ] **Step 1: Make the canvas focusable and grab focus on click**

In `EditorCanvas.cs` constructor, after `_animTimer.Tick += OnAnimTick;` add:

```csharp
this.Focusable = true;
this.KeyDown += OnKeyDown;
```

At the very top of `OnPointerPressed`, before any other logic, add:

```csharp
this.Focus();
```

- [ ] **Step 2: Add the `OnKeyDown` handler**

After the `OnPointerReleased` method, add:

```csharp
void OnKeyDown(object? sender, KeyEventArgs e)
{
    if (_textEditor is not null) return;
    if (e.Key is Key.Delete or Key.Back && _vm?.SelectedLayers.Count > 0)
    {
        _vm.DeleteSelectedLayers();
        e.Handled = true;
    }
}
```

- [ ] **Step 3: Build**

```
dotnet build ShowCast.sln
```

Expected: no errors.

- [ ] **Step 4: Manual smoke test**

Run the app, open the editor, click a layer, press Delete — layer should disappear.

- [ ] **Step 5: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat(editor): delete key removes selected layer"
```

---

## Task 3: Rubber-band selection — state and drawing

**Files:**
- Modify: `Views/EditorCanvas.cs` (fields, constructor, `OnPointerPressed`, `OnPointerMoved`, `OnPointerReleased`)

- [ ] **Step 1: Add rubber-band fields and rectangle**

In `EditorCanvas.cs`, in the `// ── Drag state ─` section add:

```csharp
bool  _rubberPotential;
bool  _rubberBanding;
Point _rubberOrigin;
readonly Rectangle _rubberRect = new()
{
    Fill             = new SolidColorBrush(Color.FromArgb(38, 59, 130, 246)),
    Stroke           = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
    StrokeThickness  = 1,
    IsHitTestVisible = false,
    IsVisible        = false
};
```

- [ ] **Step 2: Add rubber rect to overlay in constructor**

After `_overlay.Children.Add(_crossV);` in the constructor add:

```csharp
_overlay.Children.Add(_rubberRect);
```

- [ ] **Step 3: Replace "click to select layer" block in `OnPointerPressed`**

Find and replace the `// 4. Click to select layer` block (currently starting at line ~662):

Replace this:
```csharp
        // 4. Click to select layer
        var (nx, ny) = ToNorm(pt);
        SlideLayer? hit = null;
        if (_vm.EditingSlide is { } slide)
        {
            foreach (var l in slide.Layers.OrderByDescending(l => l.ZOrder))
            {
                if (!l.Locked && nx >= l.X && nx <= l.X + l.Width && ny >= l.Y && ny <= l.Y + l.Height)
                { hit = l; break; }
            }
        }
        _vm.SelectedLayer = hit;
        e.Handled = true;
```

With this:
```csharp
        // 4. Click to select layer, or start potential rubber-band on empty space
        var (nx, ny) = ToNorm(pt);
        SlideLayer? hit = null;
        if (_vm.EditingSlide is { } slide)
        {
            foreach (var l in slide.Layers.OrderByDescending(l => l.ZOrder))
            {
                if (!l.Locked && nx >= l.X && nx <= l.X + l.Width && ny >= l.Y && ny <= l.Y + l.Height)
                { hit = l; break; }
            }
        }
        if (hit is not null)
        {
            _vm.SelectedLayer = hit;
        }
        else
        {
            _rubberPotential = true;
            _rubberOrigin    = pt;
            e.Pointer.Capture(_overlay);
        }
        e.Handled = true;
```

- [ ] **Step 4: Add rubber-band drawing in `OnPointerMoved`**

At the start of `OnPointerMoved`, after `UpdateRulerPointers(pt);` and before the `if (!_dragging ...)` guard, insert:

```csharp
        if (_rubberPotential)
        {
            var delta = pt - _rubberOrigin;
            if (Math.Abs(delta.X) > 4 || Math.Abs(delta.Y) > 4)
            {
                _rubberPotential = false;
                _rubberBanding   = true;
            }
        }

        if (_rubberBanding)
        {
            double rx = Math.Min(pt.X, _rubberOrigin.X);
            double ry = Math.Min(pt.Y, _rubberOrigin.Y);
            Canvas.SetLeft(_rubberRect, rx);
            Canvas.SetTop (_rubberRect, ry);
            _rubberRect.Width    = Math.Abs(pt.X - _rubberOrigin.X);
            _rubberRect.Height   = Math.Abs(pt.Y - _rubberOrigin.Y);
            _rubberRect.IsVisible = true;
            e.Handled = true;
            return;
        }
```

- [ ] **Step 5: Finalize rubber-band in `OnPointerReleased`**

At the start of `OnPointerReleased`, before the `_textEditor?.OnPointerReleased();` line, insert:

```csharp
        if (_rubberPotential)
        {
            _rubberPotential = false;
            if (_vm is not null) _vm.SelectedLayer = null;
            e.Pointer.Capture(null);
            e.Handled = true;
            return;
        }

        if (_rubberBanding)
        {
            _rubberBanding        = false;
            _rubberRect.IsVisible = false;
            e.Pointer.Capture(null);

            if (_vm?.EditingSlide is { } slide)
            {
                double rx = Canvas.GetLeft(_rubberRect);
                double ry = Canvas.GetTop (_rubberRect);
                var (nx1, ny1) = ToNorm(new Point(rx,                  ry));
                var (nx2, ny2) = ToNorm(new Point(rx + _rubberRect.Width, ry + _rubberRect.Height));
                float minX = Math.Min(nx1, nx2), maxX = Math.Max(nx1, nx2);
                float minY = Math.Min(ny1, ny2), maxY = Math.Max(ny1, ny2);

                var hits = slide.Layers
                    .Where(l => !l.Locked)
                    .Where(l => l.X < maxX && l.X + l.Width  > minX
                             && l.Y < maxY && l.Y + l.Height > minY)
                    .ToList();

                _vm.SetMultiSelection(hits);
            }

            UpdateHandles();
            e.Handled = true;
            return;
        }
```

- [ ] **Step 6: Build**

```
dotnet build ShowCast.sln
```

Expected: no errors.

- [ ] **Step 7: Manual smoke test**

Run the app, open a page with 2+ layers, drag on empty canvas space — should draw a blue selection rectangle. On release the layers it crossed should highlight.

- [ ] **Step 8: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat(editor): rubber-band multi-select on canvas"
```

---

## Task 4: Multi-layer move

**Files:**
- Modify: `Views/EditorCanvas.cs` (`// ── Drag state` fields, `StartDrag`, `OnPointerMoved`, `OnPointerReleased`)

- [ ] **Step 1: Add `_origPositions` field**

In the `// ── Drag state ─` section, after `float _rotDragOrigDeg;` add:

```csharp
Dictionary<SlideLayer, (float X, float Y)>? _origPositions;
```

- [ ] **Step 2: Populate `_origPositions` in `StartDrag`**

At the end of `StartDrag`, after `_rotDragOrigDeg = l.RotationDegrees;` closing brace, add:

```csharp
        if (kind == HandleKind.Move && (_vm.SelectedLayers.Count > 1))
            _origPositions = _vm.SelectedLayers.ToDictionary(sl => sl, sl => (sl.X, sl.Y));
        else
            _origPositions = null;
```

- [ ] **Step 3: Apply delta to all selected layers in `OnPointerMoved`**

In the `switch (_dragKind)` block, replace the `case HandleKind.Move:` arm:

Replace:
```csharp
            case HandleKind.Move:
                layer.X = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
                layer.Y = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
                break;
```

With:
```csharp
            case HandleKind.Move:
                if (_origPositions is { Count: > 1 })
                {
                    foreach (var (sl, orig) in _origPositions)
                    {
                        sl.X = Math.Clamp(SnapX(orig.X + dx), 0f, Math.Max(0f, 1f - sl.Width));
                        sl.Y = Math.Clamp(SnapY(orig.Y + dy), 0f, Math.Max(0f, 1f - sl.Height));
                    }
                }
                else
                {
                    layer.X = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
                    layer.Y = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
                }
                break;
```

- [ ] **Step 4: Clear `_origPositions` on release**

In `OnPointerReleased`, inside `if (_dragging)` block, after `_dragging = false;` add:

```csharp
            _origPositions = null;
```

- [ ] **Step 5: Build**

```
dotnet build ShowCast.sln
```

Expected: no errors.

- [ ] **Step 6: Manual smoke test**

Rubber-band select two layers, then drag one — both should move together.

- [ ] **Step 7: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat(editor): move all selected layers together during drag"
```

---

## Task 5: Secondary selection highlights and SelectedLayers subscription

**Files:**
- Modify: `Views/EditorCanvas.cs` (`OnDataContextChanged`, `UpdateHandles`)

- [ ] **Step 1: Subscribe to `SelectedLayers` in `OnDataContextChanged`**

In `OnDataContextChanged`, after the existing `_subs.Add(_vm.WhenAnyValue(x => x.SelectedLayer).Subscribe(_ => UpdateHandles()));` line, add:

```csharp
        _subs.Add(_vm.WhenAnyValue(x => x.SelectedLayers).Subscribe(_ => UpdateHandles()));
```

- [ ] **Step 2: Update bounding-box loop in `UpdateHandles` to distinguish secondary selections**

In `UpdateHandles`, replace the inner loop that creates `_layerBounds` boxes. Replace:

```csharp
            foreach (var layer in slide.Layers)
            {
                if (layer == sel) continue;
                double x = ir.X + layer.X * ir.Width;
                double y = ir.Y + layer.Y * ir.Height;
                double w = layer.Width  * ir.Width;
                double h = layer.Height * ir.Height;
                var box = new Rectangle
                {
                    Stroke           = s_boundsBrush,
                    StrokeThickness  = 0.75,
                    StrokeDashArray  = s_boundsDash,
                    Fill             = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
                box.Width = w; box.Height = h;
                _overlay.Children.Insert(0, box);
                _layerBounds.Add(box);
            }
```

With:

```csharp
            foreach (var layer in slide.Layers)
            {
                if (layer == sel) continue;
                bool inSel = _vm?.SelectedLayers.Contains(layer) == true;
                double x = ir.X + layer.X * ir.Width;
                double y = ir.Y + layer.Y * ir.Height;
                double w = layer.Width  * ir.Width;
                double h = layer.Height * ir.Height;
                var box = new Rectangle
                {
                    Stroke          = inSel
                        ? new SolidColorBrush(Color.FromRgb(59, 130, 246))
                        : s_boundsBrush,
                    StrokeThickness = inSel ? 1.5 : 0.75,
                    StrokeDashArray = inSel ? null : s_boundsDash,
                    Fill            = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
                box.Width = w; box.Height = h;
                _overlay.Children.Insert(0, box);
                _layerBounds.Add(box);
            }
```

- [ ] **Step 3: Build**

```
dotnet build ShowCast.sln
```

Expected: no errors.

- [ ] **Step 4: Manual smoke test**

Rubber-band select two layers — all selected-but-not-primary layers should get a solid blue outline; primary gets the handles as usual; unselected layers keep the faint grey dashes.

- [ ] **Step 5: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat(editor): solid blue outline for secondary selected layers"
```

---

## Task 6: Layer panel eye button applies to all selected layers

**Files:**
- Modify: `Views/EditorLayerPanel.axaml.cs` (`OnEyeClick`)

- [ ] **Step 1: Update `OnEyeClick`**

In `EditorLayerPanel.axaml.cs`, replace the current `OnEyeClick` method:

Replace:
```csharp
    void OnEyeClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SlideLayer layer)
            VM?.ToggleLayerVisibility(layer);
    }
```

With:
```csharp
    void OnEyeClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SlideLayer layer || VM is null) return;
        if (VM.SelectedLayers.Count > 1 && VM.SelectedLayers.Contains(layer))
            VM.ToggleVisibilityForSelected();
        else
            VM.ToggleLayerVisibility(layer);
    }
```

- [ ] **Step 2: Run full test suite**

```
dotnet test ShowCast.Tests
```

Expected: all tests pass.

- [ ] **Step 3: Manual smoke test**

Multi-select two layers, click the eye icon on one of them — both should toggle visibility.

- [ ] **Step 4: Commit**

```
git add Views/EditorLayerPanel.axaml.cs
git commit -m "feat(editor): eye button toggles visibility for all selected layers"
```
