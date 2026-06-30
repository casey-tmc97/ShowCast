# Safe Area Crosshair Guides Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add centered horizontal and vertical dashed crosshair lines to the safe area overlay in the page editor.

**Architecture:** Two `Line` elements are appended to `_safeCanvas` at the end of `RebuildSafeBoundaries()`. They are cleared and rebuilt alongside the existing safe-area rectangles, sharing the `ShowSafeBoundaries` toggle with zero new state.

**Tech Stack:** C#, Avalonia 11.2.2 (`Avalonia.Controls.Shapes.Line`)

## Global Constraints

- Avalonia 11.2.2 — do not introduce newer APIs
- `IsHitTestVisible = false` on every added shape
- No new VM properties, no new UI controls, no new canvases

---

### Task 1: Add crosshair lines to `RebuildSafeBoundaries`

**Files:**
- Modify: `Views/EditorCanvas.cs` — `RebuildSafeBoundaries()` method (lines ~455–466)

**Interfaces:**
- Consumes: `GetImageRect()` → `Rect ir` (already used in the method)
- Produces: nothing new externally; `_safeCanvas.Children` gains two `Line` elements

- [ ] **Step 1: Open `Views/EditorCanvas.cs` and locate `RebuildSafeBoundaries`**

The method currently ends after two `AddSafeRect` calls:

```csharp
void RebuildSafeBoundaries()
{
    _safeCanvas.Children.Clear();
    if (_vm?.ShowSafeBoundaries != true) return;
    var ir = GetImageRect();
    if (ir.Width <= 0) return;

    AddSafeRect(ir, 0.05, Color.FromArgb(200, 255, 165, 0), "Action");
    AddSafeRect(ir, 0.10, Color.FromArgb(200, 220, 60, 60), "Title");
}
```

- [ ] **Step 2: Add the crosshair lines**

Replace the method body with:

```csharp
void RebuildSafeBoundaries()
{
    _safeCanvas.Children.Clear();
    if (_vm?.ShowSafeBoundaries != true) return;
    var ir = GetImageRect();
    if (ir.Width <= 0) return;

    AddSafeRect(ir, 0.05, Color.FromArgb(200, 255, 165, 0), "Action");
    AddSafeRect(ir, 0.10, Color.FromArgb(200, 220, 60, 60), "Title");

    var dash   = new Avalonia.Collections.AvaloniaList<double> { 8, 4 };
    var stroke = new SolidColorBrush(Color.FromArgb(150, 255, 255, 255));

    _safeCanvas.Children.Add(new Line
    {
        StartPoint       = new Point(ir.X, ir.Y + ir.Height / 2),
        EndPoint         = new Point(ir.X + ir.Width, ir.Y + ir.Height / 2),
        Stroke           = stroke,
        StrokeThickness  = 0.75,
        StrokeDashArray  = dash,
        IsHitTestVisible = false
    });
    _safeCanvas.Children.Add(new Line
    {
        StartPoint       = new Point(ir.X + ir.Width / 2, ir.Y),
        EndPoint         = new Point(ir.X + ir.Width / 2, ir.Y + ir.Height),
        Stroke           = stroke,
        StrokeThickness  = 0.75,
        StrokeDashArray  = dash,
        IsHitTestVisible = false
    });
}
```

- [ ] **Step 3: Build**

```
dotnet build
```

Expected: build succeeds with zero errors (pre-existing AVLN3001 warnings are fine).

- [ ] **Step 4: Manual verification**

Run the app. Open a show with at least one page. In the page editor, toggle the safe boundaries overlay on.

Expected:
- Horizontal dashed white line bisects the frame at the vertical midpoint
- Vertical dashed white line bisects the frame at the horizontal midpoint
- Both lines span the full image area (edge to edge)
- Toggling the overlay off hides all lines and rectangles
- Resizing the editor window repositions the crosshairs correctly

- [ ] **Step 5: Commit**

```bash
git add Views/EditorCanvas.cs
git commit -m "feat: add center crosshair guides to safe area overlay"
```
