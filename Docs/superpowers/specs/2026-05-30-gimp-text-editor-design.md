# GIMP-Style Text Editor Design

**Date:** 2026-05-30
**Status:** Approved

## Goal

Make ShowCast's inline text editor work exactly like GIMP's text editor:
- WYSIWYG canvas editing — styled text (bold, italic, size, family, color, underline, strikethrough) renders live on the canvas while the user types, using the same SkiaSharp engine as the final output
- Correct cursor positioning within styled/multi-span text
- Click-to-place cursor and drag-to-select
- Full keyboard navigation (arrows, Home/End, Ctrl+arrows, Shift for selection)
- Four new span attributes: underline, strikethrough, baseline shift, kerning
- Inspector works without the 1-second recency window hack — format buttons apply directly to the active editor's selection

## Architecture

Approach 2 — CanvasTextEditor as a separate class, mirroring GIMP's file structure:

| GIMP | ShowCast equivalent |
|------|---------------------|
| `gimptextlayout.c` (Pango layout) | `Engine/SpanLayout.cs` — pure, stateless layout engine |
| `gimptexttool-editor.c` | `Views/CanvasTextEditor.cs` — editor state, input, overlay items |
| `GimpCanvasTextCursor` | `Line` + `DispatcherTimer` inside `CanvasTextEditor` |
| `GimpTextBuffer` (tags) | `TextSpan` + extended `SpanEditor` |

## Section 1: Data Model

### TextSpan — new fields

```csharp
public bool?   Underline      { get; set; }  // null = inherit from layer
public bool?   Strikethrough  { get; set; }  // null = inherit from layer
public float?  Baseline       { get; set; }  // vertical shift in virtual px (+ = up, - = down)
public float?  Kerning        { get; set; }  // extra horizontal spacing after run, virtual px
```

### SlideLayer — new layer-level defaults

```csharp
public bool   Underline     { get; set; } = false;
public bool   Strikethrough { get; set; } = false;
public float  Baseline      { get; set; } = 0f;
public float  Kerning       { get; set; } = 0f;
```

### SpanEditor — updated signatures

`ApplyFormat` accepts four new optional params: `bool? underline`, `bool? strikethrough`, `float? baseline`, `float? kerning`.

`GetFormatAt` returns an 8-tuple: `(bool? bold, bool? italic, float? fontSize, string? fontFamily, bool? underline, bool? strikethrough, float? baseline, float? kerning)`.

`SameFormat`, `Clone`, `HasOverride` include all eight attributes.

### ShowFileSerializer — v4 migration

New fields default to `false`/`0f`/`null` when loading v1–v3 files. No data loss.

### File changes

| File | Change |
|------|--------|
| `Core/TextSpan.cs` | Add 4 new nullable fields |
| `Core/SlideLayer.cs` | Add 4 new layer-level defaults; update `Clone()` |
| `Core/SpanEditor.cs` | All methods updated for 8 attributes |
| `Core/ShowFileSerializer.cs` | v4 schema + migration |
| `ShowCast.Tests/Core/TextSpanTests.cs` | Tests for new fields |
| `ShowCast.Tests/Core/SpanEditorTests.cs` | Tests for new attribute params |

---

## Section 2: SpanLayout

`Engine/SpanLayout.cs` — pure static class, no UI or Avalonia dependencies.

### Coordinate space

`SpanLayout` works entirely in **display pixel space** — the pixel space of the Avalonia overlay canvas, not the SkiaSharp render resolution (1280×720). The layer's normalized geometry (0–1) is scaled by `displayImageRect.Width/Height`, and font sizes (normalized to canvas height) are scaled by `displayImageRect.Height`. Cursor and selection rects produced by the layout are therefore directly usable as `Canvas.SetLeft/Top` values on overlay `Line`/`Rectangle` controls with no further scaling.

### Input

```csharp
public static SpanLayoutResult Compute(SlideLayer layer, Rect displayImageRect)
```

### SpanLayoutResult

```csharp
public class SpanLayoutResult
{
    public IReadOnlyList<LayoutLine> Lines { get; }
    public float TotalHeight { get; }

    public SKRect   GetCharRect(int charIndex);          // cursor position
    public int      HitTest(float x, float y);           // click → char index
    public int      GetLineIndex(int charIndex);         // for up/down arrow
    public int      GetLineStart(int lineIndex);         // Home key
    public int      GetLineEnd(int lineIndex);           // End key
    public int      GetWordStart(int charIndex);         // Ctrl+Left
    public int      GetWordEnd(int charIndex);           // Ctrl+Right
    public IEnumerable<SKRect> GetSelectionRects(int start, int end); // selection highlight
}

public class LayoutLine
{
    public IReadOnlyList<LayoutRun> Runs { get; }
    public float Y { get; }       // baseline Y in canvas pixels
    public float Height { get; }
}

public class LayoutRun
{
    public int      SpanIndex  { get; }  // index in layer.Spans
    public int      CharStart  { get; }  // char index in EffectiveText
    public int      CharEnd    { get; }
    public float    X          { get; }  // left edge in canvas pixels
    public float    Y          { get; }  // baseline in canvas pixels (includes baseline shift)
    public float    Width      { get; }  // includes kerning
    public float    Height     { get; }
    public SKPaint  Paint      { get; }  // pre-built, owned by result (disposed with it)
}
```

### Algorithm

Replicates `PageRenderer.DrawSpans` word-wrap logic exactly, but instead of calling `canvas.DrawText` records `(charIndex, x, y, width, height)` for every character run. Uses `SKPaint.MeasureText` for widths, the same line-height formula (`fontSize * 1.25f`), and applies baseline shift and kerning identically to what the renderer will do. This guarantees cursor position equals rendered glyph position.

`SpanLayoutResult` is `IDisposable` — disposes the `SKPaint` objects it owns.

### File changes

| File | Change |
|------|--------|
| `Engine/SpanLayout.cs` | New — pure layout engine |
| `ShowCast.Tests/Engine/SpanLayoutTests.cs` | Unit tests: cursor rects, hit-test, selection rects, baseline/kerning offsets |

---

## Section 3: CanvasTextEditor

`Views/CanvasTextEditor.cs` — owns all editor state and the Avalonia overlay visual items.

### Constructor

```csharp
public CanvasTextEditor(
    SlideLayer layer,
    Canvas overlay,
    Rect imageRect,           // canvas→screen mapping (updated via UpdateImageRect)
    Action rebuildSlide,
    Action<SpanFormatInfo> spanFormatChanged,
    MainViewModel vm)
```

### Internal state

```csharp
SlideLayer         _layer
string             _editBuffer      // mutable copy of EffectiveText
List<TextSpan>     _spanBuffer      // working copy of spans
int                _cursorIndex     // char position
int                _selStart        // -1 = no selection
int                _selEnd
SpanLayoutResult?  _layout          // recomputed after every edit; disposed before each recompute
Rect               _imageRect       // display pixel rect — passed to SpanLayout.Compute; updated via UpdateImageRect
```

### Overlay items (added to overlay on Open, removed on Close)

```csharp
Line             _cursorLine        // blinking cursor
DispatcherTimer  _blinkTimer        // 530ms, toggles _cursorLine.IsVisible
List<Rectangle>  _selRects          // selection highlight boxes
TextBox          _imeBox            // zero-size, off-screen, receives IME TextInput
```

### Public API

```csharp
void Open()
void Commit()                        // write buffers back to layer, notify VM
void Cancel()                        // discard buffers
void OnPointerPressed(Point pt)      // click to place cursor
void OnPointerMoved(Point pt, bool pointerDown)  // drag to extend selection
void OnKeyDown(KeyEventArgs e)
void ApplyFormat(bool? bold=null, bool? italic=null, float? fontSize=null,
                 string? fontFamily=null, bool? underline=null,
                 bool? strikethrough=null, float? baseline=null, float? kerning=null,
                 SKColor? color=null)
void UpdateImageRect(Rect imageRect) // called by EditorCanvas on resize
SpanFormatInfo GetFormatAtCursor()
```

### Keyboard handling

| Key | Action |
|-----|--------|
| Printable char / IME TextInput | Insert at cursor, `ReconcileSpans`, rebuild layout |
| Backspace | Delete char before cursor |
| Delete | Delete char after cursor |
| Left / Right | Move cursor ±1; with Shift extend selection |
| Ctrl+Left / Right | Move by word (`GetWordStart`/`GetWordEnd`) |
| Up / Down | Move by line (`GetLineIndex`, `GetLineStart`) |
| Home / End | Jump to line start/end |
| Ctrl+A | Select all |
| Ctrl+B | Toggle bold on selection |
| Ctrl+I | Toggle italic on selection |
| Ctrl+U | Toggle underline on selection |
| Enter | Insert `\n` |
| Escape | `Cancel()` |

### Cursor and selection rendering

After every state change, `_layout` is recomputed. Then:
- `_cursorLine` positioned from `_layout.GetCharRect(_cursorIndex)` — a vertical line `(x, y_top)→(x, y_bottom)`
- `_selRects` replaced with rects from `_layout.GetSelectionRects(_selStart, _selEnd)` — semi-transparent blue fill, no border
- Blink timer resets on every keystroke (cursor always visible immediately after input)

### IME

A `TextBox` of size `1×1` positioned at `(-100, -100)` (off-screen) is created alongside the editor. Its `TextInput` event feeds characters into `_editBuffer`. `Dispatcher.UIThread.Post` gives it focus via `imeBox.Focus()` immediately after `Open()`. This replicates GIMP's `GtkIMContext`-on-canvas-widget pattern and preserves system IME for CJK input.

### File changes

| File | Change |
|------|--------|
| `Views/CanvasTextEditor.cs` | New |
| `Views/SpanFormatInfo.cs` | New — simple record: `(bool? Bold, bool? Italic, float? FontSize, string? FontFamily, bool? Underline, bool? Strikethrough, float? Baseline, float? Kerning, SKColor? Color)` |

---

## Section 4: EditorCanvas Integration

`Views/EditorCanvas.cs` — surgical replacement of TextBox inline editor with `CanvasTextEditor`.

### Removed

- `TextBox? _inlineBox`, `_inlineBoxPropHandler`, `_inlineCommitting`
- `BeginInlineEdit`, `CommitInlineEdit`, `CancelInlineEdit`, `RemoveInlineBox`
- `OnInlineKeyDown`, `OnInlineLostFocus`
- `_lastFormatLayer`, `_lastFormatSelStart`, `_lastFormatSelEnd`, `_lastFormatTime`
- `HasRecentSpanSelection`, `ApplySpanSelectionFormat`
- `InlineSpanFormatChanged` event

### Added

```csharp
CanvasTextEditor? _textEditor

public CanvasTextEditor? ActiveTextEditor => _textEditor;

public event Action<SpanFormatInfo>? SpanFormatChanged;
```

- `BeginCustomEdit(SlideLayer layer)` — creates `CanvasTextEditor`, calls `Open()`
- `EndCustomEdit()` — calls `_textEditor.Commit()`, sets `_textEditor = null`
- `CancelCustomEdit()` — calls `_textEditor.Cancel()`, sets `_textEditor = null`

### Pointer handling

- `OnPointerPressed`: if `_textEditor` active and click outside the layer rect → `EndCustomEdit()`; click inside → `_textEditor.OnPointerPressed(pt)`
- `OnPointerMoved`: if `_textEditor` active → `_textEditor.OnPointerMoved(pt, pressed)`
- `OnDoubleTapped`: calls `BeginCustomEdit` instead of `BeginInlineEdit`
- `OnSizeChanged`: calls `_textEditor?.UpdateImageRect(GetImageRect())`

### IsInlineEditing

Returns `_textEditor is not null` (same public contract used by `PageEditorOverlay` to suppress hotkeys).

### Live rendering

`CanvasTextEditor` mutates `_spanBuffer` (a copy of `layer.Spans`) on every keystroke and calls `_rebuildSlide`. On `Commit()`, the buffer is written back to `layer.Spans`. `PageRenderer.DrawSpans` renders the live styled text automatically — this is why WYSIWYG works with no extra rendering code.

---

## Section 5: Inspector Updates

### New controls in `EditorInspectorPanel.axaml` (Text section)

| x:Name | Type | Purpose |
|--------|------|---------|
| `UnderlineBtn` | `ToggleButton` | underline on/off |
| `StrikeBtn` | `ToggleButton` | strikethrough on/off |
| `BaselineBox` | `TextBox` | baseline shift in virtual px |
| `KerningBox` | `TextBox` | kerning in virtual px |
| `SpanColorPicker` | `ColorPickerField` | per-span/selection color |

### Wiring changes in `EditorInspectorPanel.axaml.cs`

- `SetCanvas` stores `EditorCanvas _canvas` and subscribes to `_canvas.SpanFormatChanged`
- `SpanFormatChanged` handler updates all 9 controls (bold, italic, size, family, underline, strike, baseline, kerning, color) — guarded by `_loading`
- `OnStyleClick` handles `UnderlineBtn` and `StrikeBtn` same as `BoldBtn`/`ItalicBtn`
- `OnBaselineLostFocus`, `OnKerningLostFocus` — parse float, call `_canvas.ActiveTextEditor?.ApplyFormat(...)` if editor active, else set `layer.Baseline/Kerning`
- `OnSpanColorChanged` — calls `ApplyFormat(color: ...)` if editor active with selection, else sets `layer.Color`
- All format methods: if `_canvas.ActiveTextEditor is not null` → `editor.ApplyFormat(...)`, else → whole-layer update. No 1-second timer needed.

### Removed

- `HasRecentSpanSelection` call sites
- `ApplySpanSelectionFormat` call sites
- `InlineSpanFormatChanged` subscription (replaced by `SpanFormatChanged`)

---

## Section 6: PageRenderer Updates

### DrawSpans — new rendering passes per run

After drawing each text run's characters, additional decorations are drawn if the resolved attribute is true:

**Underline:**
```
y_line = baseline + strokeWidth
strokeWidth = fontSize * 0.06f
Draw horizontal line from run.X to run.X + run.Width at y_line
Color = run paint color
```

**Strikethrough:**
```
y_line = baseline - fontSize * 0.30f
strokeWidth = fontSize * 0.06f
Draw horizontal line (same color)
```

**Baseline shift:**
```
effectiveBaseline = baseline - (span.Baseline ?? layer.Baseline) * (h / 1080f)
Applied before DrawText and before underline/strikethrough y calculations
```

**Kerning:**
```
x += (span.Kerning ?? layer.Kerning) * (w / 1920f)
Applied after each run, before the next run starts
SpanLayout applies identical offset so hit-testing stays accurate
```

### DrawPlainText

Gains the same underline, strikethrough, baseline, and kerning rendering using `layer.Underline`, `layer.Strikethrough`, `layer.Baseline`, `layer.Kerning`.

---

## File Map (complete)

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `Core/TextSpan.cs` | 4 new nullable fields |
| Modify | `Core/SlideLayer.cs` | 4 new layer-level defaults; Clone update |
| Modify | `Core/SpanEditor.cs` | 8-attribute support throughout |
| Modify | `Core/ShowFileSerializer.cs` | v4 schema + migration |
| Create | `Engine/SpanLayout.cs` | Pure layout engine |
| Modify | `Engine/PageRenderer.cs` | Underline/strike/baseline/kerning rendering |
| Create | `Views/CanvasTextEditor.cs` | Full WYSIWYG editor: cursor, selection, keyboard |
| Create | `Views/SpanFormatInfo.cs` | Format-at-cursor record |
| Modify | `Views/EditorCanvas.cs` | Replace TextBox editor with CanvasTextEditor |
| Modify | `Views/EditorInspectorPanel.axaml` | New controls: underline/strike/baseline/kerning/span-color |
| Modify | `Views/EditorInspectorPanel.axaml.cs` | Wire new controls; use ActiveTextEditor directly |
| Modify | `ShowCast.Tests/Core/SpanEditorTests.cs` | Tests for 4 new attributes |
| Create | `ShowCast.Tests/Engine/SpanLayoutTests.cs` | Layout engine unit tests |

---

## Key Design Decisions

**Why CanvasTextEditor mutates a span buffer copy, not the layer directly:**
Allows `Cancel()` to discard all changes cleanly without undo entries. Only `Commit()` writes to the layer and pushes an undo snapshot.

**Why cursor/selection sit in the Avalonia overlay rather than in the SkiaSharp render:**
The overlay `Line`/`Rectangle` approach avoids re-encoding a full SkiaSharp surface on every blink tick. Only the cursor visibility changes; the slide image stays static between keystrokes.

**Why 530ms blink interval:**
GIMP uses 530ms. It's the most common cursor blink interval across platforms and what users expect.

**Why keep the off-screen IME TextBox:**
Removing it would break CJK input (Chinese, Japanese, Korean). The pattern is standard — VS Code, GIMP, and most Electron apps use a hidden input for IME composition.

**Eliminating the 1-second recency window:**
The old window was a workaround for `LostFocus` closing the TextBox before inspector button clicks fired. With `CanvasTextEditor`, the editor never loses focus to inspector clicks — it captures keyboard input from the overlay canvas, not from a focusable TextBox — so the inspector always has a live `ActiveTextEditor` reference to call `ApplyFormat` on directly.
