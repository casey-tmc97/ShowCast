# Editor Window Features Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Implement six editor improvements: (1) green/red visibility toggle buttons, (2) 12.5 grid size option, (3) auto-disable snap when grid is None, (4) arrow key layer movement with visual update, (5) always-visible layer bounding boxes, (6) duplicate layers feature (verify existing implementation is wired correctly).

**Architecture:** Changes are confined to `EditorLayerPanel.axaml/.cs`, `PageEditorOverlay.axaml.cs`, `EditorCanvas.cs`, `MainViewModel.cs`, and `AppSettings.cs`. No new files are required. GridSpacing changes from `int` to `double` throughout.

**Tech Stack:** C#/.NET 9, Avalonia, SkiaSharp, ReactiveUI

---

### Task 1: Visibility toggle — green (visible) / red (hidden)

**Files:**
- Modify: `Views/EditorLayerPanel.axaml.cs` (add `VisibilityBrush` converter)
- Modify: `Views/EditorLayerPanel.axaml:80` (bind Foreground to converter)

- [ ] **Step 1: Add the FuncValueConverter for visibility color**

In `Views/EditorLayerPanel.axaml.cs`, add after the existing `LockIcon` converter:

```csharp
public static readonly FuncValueConverter<bool, Avalonia.Media.IBrush> VisibilityBrush =
    new(visible => visible
        ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22cc66"))
        : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#cc3333")));
```

- [ ] **Step 2: Bind the eye button Foreground in AXAML**

In `Views/EditorLayerPanel.axaml`, replace the eye button:

```xml
<Button Grid.Column="0" Classes="layer-icon"
        Content="●"
        Click="OnEyeClick"
        Tag="{Binding .}"
        ToolTip.Tip="Toggle visibility"/>
```

With:

```xml
<Button Grid.Column="0" Classes="layer-icon"
        Content="●"
        Foreground="{Binding Visible,
            Converter={x:Static local:EditorLayerPanel.VisibilityBrush}}"
        Click="OnEyeClick"
        Tag="{Binding .}"
        ToolTip.Tip="Toggle visibility"/>
```

Also remove the hardcoded `Foreground` value from the `layer-icon` style so the binding takes effect:

In the `<Style Selector="Button.layer-icon">` block, remove or change the `Foreground` setter:

```xml
<!-- Remove this line from the layer-icon style: -->
<!-- <Setter Property="Foreground" Value="#888888"/> -->
```

If removing it breaks other buttons, add it back only for non-eye buttons by adding a separate class or leaving it as a lower-priority default.

- [ ] **Step 3: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Manual test**

1. Open a package in the editor
2. Verify the visibility dot (●) is green for visible layers
3. Click the dot — verify it turns red and the layer disappears from the canvas
4. Click again — verify it turns green and the layer reappears

- [ ] **Step 5: Commit**

```bash
git add Views/EditorLayerPanel.axaml Views/EditorLayerPanel.axaml.cs
git commit -m "feat(editor): visibility toggle shows green/red color indicator"
```

---

### Task 2: Add 12.5 grid size option

**Files:**
- Modify: `Core/AppSettings.cs:7` (`GridSpacing` type: int → double)
- Modify: `ViewModels/MainViewModel.cs:1090` (`GridSpacing` type: int → double)
- Modify: `Views/EditorCanvas.cs` (snap helpers + grid/ruler rebuilds)
- Modify: `Views/PageEditorOverlay.axaml.cs` (OnDataContextChanged + OnGridSizeChanged)
- Modify: `Views/PageEditorOverlay.axaml` (add 12.5 ComboBoxItem)

- [ ] **Step 1: Change GridSpacing from int to double**

In `Core/AppSettings.cs`, change:

```csharp
public int GridSpacing { get; set; } = 100;
```

To:

```csharp
public double GridSpacing { get; set; } = 100;
```

In `ViewModels/MainViewModel.cs`, change:

```csharp
private int _gridSpacing = 100;
public int GridSpacing
{
    get => _gridSpacing;
    set => this.RaiseAndSetIfChanged(ref _gridSpacing, value);
}
```

To:

```csharp
private double _gridSpacing = 100;
public double GridSpacing
{
    get => _gridSpacing;
    set => this.RaiseAndSetIfChanged(ref _gridSpacing, value);
}
```

Also update `SaveSessionAsync` where it reads `s.GridSpacing` (no change needed — auto-cast).
And `RestoreSettings` where it writes `GridSpacing = s.GridSpacing` (no change needed).

- [ ] **Step 2: Fix EditorCanvas to use double spacing**

In `Views/EditorCanvas.cs`, in `RebuildGrid`:

```csharp
// Change:
int spacing = _vm?.GridSpacing ?? 100;
// To:
double spacing = _vm?.GridSpacing ?? 100;
```

In `RebuildGrid`, change the loop conditions:

```csharp
// Change:
for (int vx = spacing; vx < 1920; vx += spacing)
// To:
for (double vx = spacing; vx < 1920; vx += spacing)

// Change:
for (int vy = spacing; vy < 1080; vy += spacing)
// To:
for (double vy = spacing; vy < 1080; vy += spacing)
```

In `RebuildHRuler`:

```csharp
// Change:
int spacing = _vm?.GridSpacing ?? 100;
// To:
double spacing = _vm?.GridSpacing ?? 100;

// Change:
for (int vx = 0; vx <= 1920; vx += spacing / 2)
// To:
for (double vx = 0; vx <= 1920; vx += spacing / 2)
    
// Change:
bool major = vx % spacing == 0;
// To:
bool major = Math.Abs(vx % spacing) < 0.001;
```

In `RebuildVRuler`:

```csharp
// Change:
int spacing = _vm?.GridSpacing ?? 100;
// To:
double spacing = _vm?.GridSpacing ?? 100;

// Change:
for (int vy = 0; vy <= 1080; vy += spacing / 2)
// To:
for (double vy = 0; vy <= 1080; vy += spacing / 2)

// Change:
bool major = vy % spacing == 0;
// To:
bool major = Math.Abs(vy % spacing) < 0.001;
```

In `SnapX` and `SnapY`:

```csharp
float SnapX(float v)
{
    if (_vm?.SnapToGrid != true || _vm.GridSpacing <= 0) return v;
    float step = (float)(_vm.GridSpacing / 1920.0);
    return (float)Math.Round(v / step) * step;
}

float SnapY(float v)
{
    if (_vm?.SnapToGrid != true || _vm.GridSpacing <= 0) return v;
    float step = (float)(_vm.GridSpacing / 1080.0);
    return (float)Math.Round(v / step) * step;
}
```

- [ ] **Step 3: Add 12.5 to the ComboBox in PageEditorOverlay**

In `Views/PageEditorOverlay.axaml`, find the `GridSizeBox` ComboBox and add `12.5` as an option. (Read the AXAML first to find the exact location, then add `<ComboBoxItem>12.5</ComboBoxItem>` after the other options.)

In `Views/PageEditorOverlay.axaml.cs`, update `OnDataContextChanged`:

```csharp
GridSizeBox.SelectedIndex = !VM.ShowGrid ? 0 : VM.GridSpacing switch
{
    12.5 => 1,
    25   => 2,
    50   => 3,
    75   => 4,
    100  => 5,
    _    => 0
};
```

Update `OnGridSizeChanged`:

```csharp
void OnGridSizeChanged(object? sender, SelectionChangedEventArgs e)
{
    if (VM is null) return;
    switch (GridSizeBox.SelectedIndex)
    {
        case 0:  VM.ShowGrid = false; VM.SnapToGrid = false; break;
        case 1:  VM.ShowGrid = true; VM.GridSpacing = 12.5; break;
        case 2:  VM.ShowGrid = true; VM.GridSpacing =  25;  break;
        case 3:  VM.ShowGrid = true; VM.GridSpacing =  50;  break;
        case 4:  VM.ShowGrid = true; VM.GridSpacing =  75;  break;
        default: VM.ShowGrid = true; VM.GridSpacing = 100;  break;
    }
}
```

Note: `VM.SnapToGrid = false` when selecting None is the snap auto-disable fix (Task 3 combined here).

- [ ] **Step 4: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Run tests**

Run: `dotnet test ShowCast.Tests/ -v minimal`
Expected: All tests pass.

- [ ] **Step 6: Manual test**

1. Open editor → Grid dropdown → select "12.5"
2. Verify fine grid lines appear at 12.5px intervals
3. Enable Snap to Grid — drag a layer — verify it snaps to 12.5 intervals
4. Select "None" — verify Snap to Grid checkbox unchecks automatically

- [ ] **Step 7: Commit**

```bash
git add Core/AppSettings.cs ViewModels/MainViewModel.cs Views/EditorCanvas.cs Views/PageEditorOverlay.axaml Views/PageEditorOverlay.axaml.cs
git commit -m "feat(editor): add 12.5 grid size option; auto-disable snap when grid is None"
```

---

### Task 3: Arrow key movement — visual update

**Context:** Arrow keys should nudge the selected layer by 1 virtual pixel (1/1920 or 1/1080 of the canvas). The fix adds a `KeyDown` handler in `PageEditorOverlay.axaml.cs` that updates layer X/Y and calls `NotifySlideChanged()`.

**Files:**
- Modify: `Views/PageEditorOverlay.axaml.cs:78` (`OnKeyDown`)

- [ ] **Step 1: Add arrow key handling to OnKeyDown**

In `Views/PageEditorOverlay.axaml.cs`, in `OnKeyDown`, add after the `ctrl && e.Key == Key.D` block:

```csharp
if (VM?.SelectedLayer is { } layer && !ctrl)
{
    const float step = 1f / 1920f;
    switch (e.Key)
    {
        case Key.Left:
            VM.BeginLayerEdit();
            layer.X = Math.Clamp(layer.X - step, 0f, 1f - layer.Width);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
        case Key.Right:
            VM.BeginLayerEdit();
            layer.X = Math.Clamp(layer.X + step, 0f, 1f - layer.Width);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
        case Key.Up:
            VM.BeginLayerEdit();
            layer.Y = Math.Clamp(layer.Y - (1f / 1080f), 0f, 1f - layer.Height);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
        case Key.Down:
            VM.BeginLayerEdit();
            layer.Y = Math.Clamp(layer.Y + (1f / 1080f), 0f, 1f - layer.Height);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
    }
}
```

When Snap is active, multiply the step by the grid spacing so arrows snap to grid:

```csharp
if (VM?.SelectedLayer is { } layer && !ctrl)
{
    float stepX = VM.SnapToGrid && VM.GridSpacing > 0
        ? (float)(VM.GridSpacing / 1920.0)
        : 1f / 1920f;
    float stepY = VM.SnapToGrid && VM.GridSpacing > 0
        ? (float)(VM.GridSpacing / 1080.0)
        : 1f / 1080f;
    switch (e.Key)
    {
        case Key.Left:
            VM.BeginLayerEdit();
            layer.X = Math.Clamp(layer.X - stepX, 0f, 1f - layer.Width);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
        case Key.Right:
            VM.BeginLayerEdit();
            layer.X = Math.Clamp(layer.X + stepX, 0f, 1f - layer.Width);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
        case Key.Up:
            VM.BeginLayerEdit();
            layer.Y = Math.Clamp(layer.Y - stepY, 0f, 1f - layer.Height);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
        case Key.Down:
            VM.BeginLayerEdit();
            layer.Y = Math.Clamp(layer.Y + stepY, 0f, 1f - layer.Height);
            VM.NotifySlideChanged();
            e.Handled = true;
            return;
    }
}
```

- [ ] **Step 2: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Manual test**

1. Open a package in the editor; select a text layer
2. Press Right arrow — layer moves 1px right on canvas
3. Press Left arrow — layer moves 1px left
4. Press Up/Down — layer moves 1px up/down
5. Enable Snap + Grid=100 — pressing arrow moves layer by one grid unit
6. Verify inspector X/Y values also update (they'll update on next focus since they use LostFocus binding)

- [ ] **Step 4: Commit**

```bash
git add Views/PageEditorOverlay.axaml.cs
git commit -m "feat(editor): arrow keys move selected layer with visual update"
```

---

### Task 4: Always-visible layer bounding boxes

**Context:** Currently `UpdateHandles()` only draws the selection border for the selected layer. The requirement is to show a thin bounding box for ALL layers at all times, with resize handles only for the selected layer.

**Files:**
- Modify: `Views/EditorCanvas.cs` (add bounding box overlay list; update `UpdateHandles`)

- [ ] **Step 1: Add a bounding box rectangle list field**

In `Views/EditorCanvas.cs`, add after `readonly Rectangle[] _handles`:

```csharp
readonly List<Rectangle> _layerBounds = new();
```

- [ ] **Step 2: Update UpdateHandles to draw all-layer bounding boxes**

Replace the `UpdateHandles()` method:

```csharp
void UpdateHandles()
{
    // Remove previous per-layer bounding boxes
    foreach (var r in _layerBounds) _overlay.Children.Remove(r);
    _layerBounds.Clear();

    var slide = _vm?.EditingPage;
    var sel   = _vm?.SelectedLayer;

    if (slide is not null && _overlay.Bounds.Width > 0)
    {
        var ir = GetImageRect();
        foreach (var layer in slide.Layers)
        {
            if (layer == sel) continue;  // selected layer handled separately below
            double x = ir.X + layer.X * ir.Width;
            double y = ir.Y + layer.Y * ir.Height;
            double w = layer.Width  * ir.Width;
            double h = layer.Height * ir.Height;
            var box = new Rectangle
            {
                Stroke          = new SolidColorBrush(Color.FromArgb(100, 120, 120, 160)),
                StrokeThickness = 0.75,
                StrokeDashArray = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                Fill            = Brushes.Transparent,
                IsHitTestVisible = false
            };
            Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
            box.Width = w; box.Height = h;
            _overlay.Children.Insert(0, box);  // insert at back so handles render on top
            _layerBounds.Add(box);
        }
    }

    if (sel is null || _overlay.Bounds.Width <= 0)
    {
        _selBorder.IsVisible = false;
        _rotHandle.IsVisible = false; _rotHandleLine.IsVisible = false;
        foreach (var hnd in _handles) hnd.IsVisible = false;
        return;
    }

    var selIr = GetImageRect();
    double sx  = selIr.X + sel.X * selIr.Width;
    double sy  = selIr.Y + sel.Y * selIr.Height;
    double sw  = sel.Width  * selIr.Width;
    double sh  = sel.Height * selIr.Height;

    Canvas.SetLeft(_selBorder, sx); Canvas.SetTop(_selBorder, sy);
    _selBorder.Width = sw; _selBorder.Height = sh; _selBorder.IsVisible = true;

    // Resize handles: NW N NE W E SW S SE
    double[] hx = { sx - HandleHalf, sx + sw/2 - HandleHalf, sx + sw - HandleHalf,
                     sx - HandleHalf,                          sx + sw - HandleHalf,
                     sx - HandleHalf, sx + sw/2 - HandleHalf, sx + sw - HandleHalf };
    double[] hy = { sy - HandleHalf, sy - HandleHalf,          sy - HandleHalf,
                     sy + sh/2 - HandleHalf,                   sy + sh/2 - HandleHalf,
                     sy + sh - HandleHalf, sy + sh - HandleHalf, sy + sh - HandleHalf };
    for (int i = 0; i < 8; i++)
    {
        Canvas.SetLeft(_handles[i], hx[i]);
        Canvas.SetTop (_handles[i], hy[i]);
        _handles[i].IsVisible = true;
    }

    // Rotation handle
    double rhx = sx + sw/2 - RotHandHalf;
    double rhy = sy - RotHandDist - RotHandHalf;
    Canvas.SetLeft(_rotHandle, rhx); Canvas.SetTop(_rotHandle, rhy);
    _rotHandle.IsVisible = true;
    _rotHandleLine.StartPoint = new Point(sx + sw/2, sy);
    _rotHandleLine.EndPoint   = new Point(sx + sw/2, rhy + RotHandHalf);
    _rotHandleLine.IsVisible  = true;
}
```

- [ ] **Step 3: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Manual test**

1. Open a package in the editor with multiple layers
2. Without selecting any layer — verify all layers show a faint dashed bounding box
3. Click a layer — verify that layer shows a solid blue bounding box + resize handles; other layers keep the faint box
4. Click away (deselect) — handles disappear, all layers keep their faint boxes

- [ ] **Step 5: Commit**

```bash
git add Views/EditorCanvas.cs
git commit -m "feat(editor): always show layer bounding boxes; handles only appear for selected layer"
```

---

### Task 5: Verify duplicate layer wiring

**Context:** `MainViewModel.DuplicateLayer()` exists; `EditorLayerPanel.OnDuplicate` calls `VM?.DuplicateLayer(VM.SelectedLayer)`. Ctrl+D in `PageEditorOverlay.OnKeyDown` also calls it. This task verifies the feature is fully wired.

**Files:**
- No changes expected unless a gap is found.

- [ ] **Step 1: Write a test for DuplicateLayer**

Add to `ShowCast.Tests/ViewModels/`:

```csharp
// ShowCast.Tests/ViewModels/DuplicateLayerTests.cs
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class DuplicateLayerTests
{
    [Fact]
    public void DuplicateLayer_AddsLayerWithOffsetAndNewId()
    {
        var vm  = new MainViewModel();
        var show = vm.AddShow("S");
        var pkg  = show.AddPackage("P");
        var page = new Page();
        var layer = new SlideLayer { Type = LayerType.Text, Name = "Original", X = 0.1f, Y = 0.2f };
        page.AddLayer(layer);
        pkg.AddPage(page);

        vm.OpenEditor(new PageViewModel(page, pkg));
        vm.SelectedLayer = layer;

        int countBefore = vm.EditingPage!.Layers.Count;
        vm.DuplicateLayer(layer);

        Assert.Equal(countBefore + 1, vm.EditingPage.Layers.Count);
        Assert.NotEqual(layer.Id, vm.SelectedLayer!.Id);
        Assert.Equal("Original Copy", vm.SelectedLayer.Name);
        Assert.True(vm.SelectedLayer.X > layer.X);  // offset applied
    }
}
```

- [ ] **Step 2: Run test**

Run: `dotnet test ShowCast.Tests/ --filter "DuplicateLayerTests" -v minimal`
Expected: PASS.

- [ ] **Step 3: Manual test**

1. Open editor, select a layer
2. Click the ⧉ button in the layer panel footer
3. Verify a copy appears with " Copy" suffix, selected, offset by 2% on canvas
4. Press Ctrl+D — verify another copy is created

- [ ] **Step 4: Commit**

```bash
git add ShowCast.Tests/ViewModels/DuplicateLayerTests.cs
git commit -m "test(editor): add DuplicateLayer test to verify existing feature wiring"
```
