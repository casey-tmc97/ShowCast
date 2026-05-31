# GIMP-Style Text Editor Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the Avalonia TextBox inline editor with a fully custom SkiaSharp-driven WYSIWYG text editor that mirrors GIMP's architecture — live styled text on canvas, correct cursor positioning within spans, click/drag selection, and four new span attributes (underline, strikethrough, baseline, kerning).

**Architecture:** `Engine/SpanLayout.cs` computes character-level pixel positions in display space (equivalent to GIMP's PangoLayout). `Views/CanvasTextEditor.cs` owns cursor/selection state, overlay visuals, and keyboard/pointer input (equivalent to GIMP's `gimptexttool-editor.c`). `EditorCanvas.cs` is surgically updated to create/destroy `CanvasTextEditor` on double-tap instead of spawning a TextBox.

**Tech Stack:** C#/.NET 9, Avalonia 11, SkiaSharp, ReactiveUI, xUnit

---

## File Map

| Action | File | Responsibility |
|--------|------|----------------|
| Modify | `Core/TextSpan.cs` | Add Underline, Strikethrough, Baseline, Kerning fields |
| Modify | `Core/SlideLayer.cs` | Add layer-level defaults; update Clone() |
| Modify | `Core/SpanEditor.cs` | 9-attribute support throughout; add color to ApplyFormat |
| Modify | `Core/ShowFile.cs` | CurrentVersion = 4 |
| Modify | `Core/ShowFileSerializer.cs` | v3→v4 migration entry |
| Create | `Engine/SpanLayout.cs` | Pure layout engine: char rects, hit-test, selection rects |
| Modify | `Engine/PageRenderer.cs` | Render underline/strikethrough/baseline/kerning |
| Create | `Views/SpanFormatInfo.cs` | Record: format state at cursor |
| Create | `Views/CanvasTextEditor.cs` | WYSIWYG editor: cursor, selection, keyboard, IME |
| Modify | `Views/EditorCanvas.cs` | Replace TextBox editor with CanvasTextEditor |
| Modify | `Views/EditorInspectorPanel.axaml` | Add Underline/Strike/Baseline/Kerning/SpanColor controls |
| Modify | `Views/EditorInspectorPanel.axaml.cs` | Wire new controls; use ActiveTextEditor directly |
| Modify | `ShowCast.Tests/Core/SpanEditorTests.cs` | Update for 9-attribute tuple |
| Create | `ShowCast.Tests/Engine/SpanLayoutTests.cs` | Layout engine unit tests |

---

## Task 1: Data Model Extensions

**Files:**
- Modify: `Core/TextSpan.cs`
- Modify: `Core/SlideLayer.cs`
- Modify: `Core/SpanEditor.cs`
- Modify: `Core/ShowFile.cs`
- Modify: `Core/ShowFileSerializer.cs`
- Modify: `ShowCast.Tests/Core/SpanEditorTests.cs`

---

- [ ] **Step 1: Add four fields to TextSpan**

Open `Core/TextSpan.cs`. Replace the entire file content with:

```csharp
using SkiaSharp;

namespace ShowCast.Core;

public class TextSpan
{
    public string   Text          { get; set; } = "";
    public float?   FontSize      { get; set; }   // null = inherit from layer
    public string?  FontFamily    { get; set; }   // null = inherit
    public bool?    Bold          { get; set; }   // null = inherit
    public bool?    Italic        { get; set; }   // null = inherit
    public SKColor? Color         { get; set; }   // null = inherit
    public bool?    Underline     { get; set; }   // null = inherit
    public bool?    Strikethrough { get; set; }   // null = inherit
    public float?   Baseline      { get; set; }   // vertical shift in virtual px (+ = up)
    public float?   Kerning       { get; set; }   // extra spacing after run in virtual px
}
```

---

- [ ] **Step 2: Add four fields to SlideLayer and update Clone()**

Open `Core/SlideLayer.cs`.

After the line `public bool Italic { get; set; } = false;` (in the `// ── Content ──` section), add:

```csharp
    public bool  Underline     { get; set; } = false;
    public bool  Strikethrough { get; set; } = false;
    public float Baseline      { get; set; } = 0f;
    public float Kerning       { get; set; } = 0f;
```

In the `Clone()` method, after the line `Italic = Italic,` add:

```csharp
        Underline       = Underline,
        Strikethrough   = Strikethrough,
        Baseline        = Baseline,
        Kerning         = Kerning,
```

In the `Clone()` method's Spans projection (the `Spans = Spans.Select(s => new TextSpan { ... })` block), after `Color = s.Color,` add:

```csharp
            Underline     = s.Underline,
            Strikethrough = s.Strikethrough,
            Baseline      = s.Baseline,
            Kerning       = s.Kerning,
```

---

- [ ] **Step 3: Update SpanEditor — expand GetFormatAt return tuple**

Open `Core/SpanEditor.cs`. Replace the `GetFormatAt` method:

```csharp
    public static (bool? bold, bool? italic, float? fontSize, string? fontFamily,
                   bool? underline, bool? strikethrough, float? baseline, float? kerning,
                   SKColor? color)
        GetFormatAt(SlideLayer layer, int pos)
    {
        if (layer.Spans.Count == 0) return default;
        int ci = 0;
        foreach (var span in layer.Spans)
        {
            ci += span.Text.Length;
            if (pos < ci) return (span.Bold, span.Italic, span.FontSize, span.FontFamily,
                                  span.Underline, span.Strikethrough, span.Baseline, span.Kerning,
                                  span.Color);
        }
        var last = layer.Spans[^1];
        return (last.Bold, last.Italic, last.FontSize, last.FontFamily,
                last.Underline, last.Strikethrough, last.Baseline, last.Kerning,
                last.Color);
    }
```

---

- [ ] **Step 4: Update SpanEditor — expand ApplyFormat parameters**

Replace the `ApplyFormat` method signature and inner loop:

```csharp
    public static void ApplyFormat(SlideLayer layer, int selStart, int selEnd,
                                   bool? bold = null, bool? italic = null,
                                   float? fontSize = null, string? fontFamily = null,
                                   bool? underline = null, bool? strikethrough = null,
                                   float? baseline = null, float? kerning = null,
                                   SKColor? color = null)
    {
        if (selStart >= selEnd) return;

        if (layer.Spans.Count == 0)
        {
            string seed = layer.EffectiveText;
            if (seed.Length == 0) return;
            layer.Spans.Add(new TextSpan { Text = seed });
        }

        int total = layer.Spans.Sum(s => s.Text.Length);
        selStart = Math.Max(0, selStart);
        selEnd   = Math.Min(selEnd, total);
        if (selStart >= selEnd) return;

        SplitAt(layer.Spans, selStart);
        SplitAt(layer.Spans, selEnd);

        int ci = 0;
        foreach (var span in layer.Spans)
        {
            int spanEnd = ci + span.Text.Length;
            if (ci >= selStart && spanEnd <= selEnd)
            {
                if (bold.HasValue)          span.Bold          = bold;
                if (italic.HasValue)        span.Italic        = italic;
                if (fontSize.HasValue)      span.FontSize      = fontSize;
                if (fontFamily is not null) span.FontFamily    = fontFamily;
                if (underline.HasValue)     span.Underline     = underline;
                if (strikethrough.HasValue) span.Strikethrough = strikethrough;
                if (baseline.HasValue)      span.Baseline      = baseline;
                if (kerning.HasValue)       span.Kerning       = kerning;
                if (color.HasValue)         span.Color         = color;
            }
            ci = spanEnd;
        }

        Merge(layer.Spans);
    }
```

---

- [ ] **Step 5: Update SpanEditor — Clone, HasOverride, SameFormat**

Replace the three private helpers at the bottom of `Core/SpanEditor.cs`:

```csharp
    static TextSpan Clone(TextSpan src, string text) => new()
    {
        Text = text, Bold = src.Bold, Italic = src.Italic,
        FontSize = src.FontSize, FontFamily = src.FontFamily, Color = src.Color,
        Underline = src.Underline, Strikethrough = src.Strikethrough,
        Baseline = src.Baseline, Kerning = src.Kerning
    };

    static bool HasOverride(TextSpan s) =>
        s.Bold.HasValue || s.Italic.HasValue || s.FontSize.HasValue
        || s.FontFamily is not null || s.Color.HasValue
        || s.Underline.HasValue || s.Strikethrough.HasValue
        || s.Baseline.HasValue || s.Kerning.HasValue;

    static bool SameFormat(TextSpan a, TextSpan b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.FontSize == b.FontSize
        && a.FontFamily == b.FontFamily && a.Color == b.Color
        && a.Underline == b.Underline && a.Strikethrough == b.Strikethrough
        && a.Baseline == b.Baseline && a.Kerning == b.Kerning;
```

---

- [ ] **Step 6: Bump ShowFile version to 4**

Open `Core/ShowFile.cs`. Change:

```csharp
    public const int CurrentVersion = 3;
```

to:

```csharp
    public const int CurrentVersion = 4;
```

---

- [ ] **Step 7: Add v3→v4 migration to ShowFileSerializer**

Open `Core/ShowFileSerializer.cs`. In the `Migrations` list, add a third entry after the existing `// index 1: v2 → v3` block:

```csharp
        // index 2: v3 → v4 — added Underline, Strikethrough, Baseline, Kerning to
        // TextSpan and SlideLayer. New nullable/defaulted fields; JSON populates them
        // automatically from absence, so no data migration is required.
        _ => { },
```

---

- [ ] **Step 8: Update SpanEditorTests to use new 9-element tuple**

Open `ShowCast.Tests/Core/SpanEditorTests.cs`. Replace every deconstruct of `GetFormatAt` with named-field access. The two test methods that call it are `GetFormatAt_ReturnsFormatOfCorrectSpan` and `GetFormatAt_ReturnsLastSpan_WhenPosAtEnd`.

Replace:

```csharp
    [Fact]
    public void GetFormatAt_ReturnsFormatOfCorrectSpan()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, true));

        var (bold, italic, _, _) = SpanEditor.GetFormatAt(layer, 0);  // in "Hello"
        Assert.True(bold);
        Assert.Null(italic);

        (bold, italic, _, _) = SpanEditor.GetFormatAt(layer, 7);      // in " World"
        Assert.Null(bold);
        Assert.True(italic);
    }

    [Fact]
    public void GetFormatAt_ReturnsLastSpan_WhenPosAtEnd()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, true));
        var (bold, italic, _, _) = SpanEditor.GetFormatAt(layer, 11); // past end
        Assert.Null(bold);
        Assert.True(italic);
    }
```

With:

```csharp
    [Fact]
    public void GetFormatAt_ReturnsFormatOfCorrectSpan()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, true));

        var fmt0 = SpanEditor.GetFormatAt(layer, 0);  // in "Hello"
        Assert.True(fmt0.bold);
        Assert.Null(fmt0.italic);

        var fmt7 = SpanEditor.GetFormatAt(layer, 7);  // in " World"
        Assert.Null(fmt7.bold);
        Assert.True(fmt7.italic);
    }

    [Fact]
    public void GetFormatAt_ReturnsLastSpan_WhenPosAtEnd()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, true));
        var fmt = SpanEditor.GetFormatAt(layer, 11); // past end
        Assert.Null(fmt.bold);
        Assert.True(fmt.italic);
    }
```

Also fix the `OnInlineKeyDown` in `Views/EditorCanvas.cs` which uses the old 4-element deconstruct (lines ~901-903):

```csharp
            var (curBold, curItalic, _, _) = SpanEditor.GetFormatAt(_inlineLayer, selStart);
```

Change to:

```csharp
            var fmt = SpanEditor.GetFormatAt(_inlineLayer, selStart);
            bool? curBold = fmt.bold; bool? curItalic = fmt.italic;
```

And fix `BeginInlineEdit` (line ~879):

```csharp
            var (b, i, fs, ff) = SpanEditor.GetFormatAt(_inlineLayer, start);
            InlineSpanFormatChanged?.Invoke(b, i, fs, ff);
```

Change to:

```csharp
            var fmt = SpanEditor.GetFormatAt(_inlineLayer, start);
            InlineSpanFormatChanged?.Invoke(fmt.bold, fmt.italic, fmt.fontSize, fmt.fontFamily);
```

---

- [ ] **Step 9: Build and verify**

```
dotnet build ShowCast.csproj -c Debug -v minimal 2>&1 | tail -5
```

Expected: 0 errors.

---

- [ ] **Step 10: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | tail -10
```

Expected: all pass.

---

- [ ] **Step 11: Commit**

```
git add Core/TextSpan.cs Core/SlideLayer.cs Core/SpanEditor.cs Core/ShowFile.cs Core/ShowFileSerializer.cs ShowCast.Tests/Core/SpanEditorTests.cs Views/EditorCanvas.cs
git commit -m "feat(rich-text): add underline, strikethrough, baseline, kerning to span model; bump file version to 4"
```

---

## Task 2: SpanLayout Engine

**Files:**
- Create: `Engine/SpanLayout.cs`
- Create: `ShowCast.Tests/Engine/SpanLayoutTests.cs`

SpanLayout computes character-level pixel positions in display space from a `SlideLayer` + Avalonia display rect. It's pure (no UI dependencies) and fully unit-testable.

---

- [ ] **Step 1: Write failing tests**

Create `ShowCast.Tests/Engine/SpanLayoutTests.cs`:

```csharp
using Avalonia;
using ShowCast.Core;
using ShowCast.Engine;
using Xunit;

namespace ShowCast.Tests.Engine;

public class SpanLayoutTests
{
    static Rect TestRect => new Rect(0, 0, 1280, 720);

    static SlideLayer SingleSpanLayer(string text, float fontSize = 0.07f) => new SlideLayer
    {
        Type = LayerType.Text, Text = text, FontSize = fontSize,
        FontFamily = "Arial", X = 0f, Y = 0f, Width = 1f, Height = 1f,
        TextHAlign = TextHAlign.Left, TextVAlign = TextVAlign.Top
    };

    static SlideLayer MultiSpanLayer(params (string text, bool? bold)[] spans)
    {
        var layer = new SlideLayer
        {
            Type = LayerType.Text, FontSize = 0.05f,
            FontFamily = "Arial", X = 0f, Y = 0f, Width = 1f, Height = 1f,
            TextHAlign = TextHAlign.Left, TextVAlign = TextVAlign.Top
        };
        foreach (var (text, bold) in spans)
            layer.Spans.Add(new TextSpan { Text = text, Bold = bold });
        return layer;
    }

    [Fact]
    public void EmptyText_ReturnsSingleCursorPositionAtLayerOrigin()
    {
        var layer = SingleSpanLayer("");
        var result = SpanLayout.Compute(layer, TestRect);

        // One cursor position (index 0) at top-left of layer
        var r = result.GetCharRect(0);
        Assert.True(r.Left >= 0);
        Assert.True(r.Top  >= 0);
        Assert.Equal(0, result.Lines.Count);
    }

    [Fact]
    public void SingleChar_HasTwoCursorPositions()
    {
        var layer = SingleSpanLayer("A");
        var result = SpanLayout.Compute(layer, TestRect);

        var r0 = result.GetCharRect(0);  // before 'A'
        var r1 = result.GetCharRect(1);  // after 'A'
        Assert.True(r1.Left > r0.Left, "cursor after 'A' should be to the right of cursor before 'A'");
    }

    [Fact]
    public void MultiChar_CursorPositionsAreMonotonicallyIncreasing()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

        float prevX = float.MinValue;
        for (int i = 0; i <= 5; i++)
        {
            float x = result.GetCharRect(i).Left;
            Assert.True(x >= prevX, $"cursor position {i} should be >= position {i-1}");
            prevX = x;
        }
    }

    [Fact]
    public void HitTest_ClickBeforeFirstChar_ReturnsZero()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

        // Click far to the left of text — should return char 0
        int hit = result.HitTest(-100f, result.GetCharRect(0).Top + 5f);
        Assert.Equal(0, hit);
    }

    [Fact]
    public void HitTest_ClickAfterLastChar_ReturnsTextLength()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

        // Click far to the right of text — should return 5 (length)
        int hit = result.HitTest(5000f, result.GetCharRect(0).Top + 5f);
        Assert.Equal(5, hit);
    }

    [Fact]
    public void GetSelectionRects_SingleLine_ReturnsOneRect()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

        var rects = result.GetSelectionRects(1, 3).ToList();
        Assert.Single(rects);
        Assert.True(rects[0].Width > 0);
    }

    [Fact]
    public void GetSelectionRects_EmptyRange_ReturnsNone()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

        var rects = result.GetSelectionRects(2, 2).ToList();
        Assert.Empty(rects);
    }

    [Fact]
    public void GetLineIndex_SingleLine_ReturnsZeroForAllChars()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

        for (int i = 0; i <= 5; i++)
            Assert.Equal(0, result.GetLineIndex(i));
    }

    [Fact]
    public void MultiSpan_CursorPositionsAreMonotonicallyIncreasing()
    {
        var layer = MultiSpanLayer(("Hello ", true), ("World", null));
        var result = SpanLayout.Compute(layer, TestRect);

        float prevX = float.MinValue;
        for (int i = 0; i <= 11; i++)
        {
            float x = result.GetCharRect(i).Left;
            Assert.True(x >= prevX, $"cursor position {i} (x={x}) should be >= position {i-1} (x={prevX})");
            prevX = x;
        }
    }

    [Fact]
    public void GetWordStart_SkipsToWordBeginning()
    {
        var layer = SingleSpanLayer("Hello World");
        var result = SpanLayout.Compute(layer, TestRect);

        Assert.Equal(6, result.GetWordStart(10));  // from inside "World" → start of "World"
        Assert.Equal(0, result.GetWordStart(4));   // from inside "Hello" → start of "Hello"
    }

    [Fact]
    public void GetWordEnd_SkipsToWordEnd()
    {
        var layer = SingleSpanLayer("Hello World");
        var result = SpanLayout.Compute(layer, TestRect);

        Assert.Equal(5, result.GetWordEnd(1));    // from inside "Hello" → end of "Hello"
        Assert.Equal(11, result.GetWordEnd(7));   // from inside "World" → end of "World"
    }
}
```

---

- [ ] **Step 2: Run to verify tests fail**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "SpanLayoutTests" -v minimal 2>&1 | tail -5
```

Expected: compile error — `SpanLayout` does not exist yet.

---

- [ ] **Step 3: Create Engine/SpanLayout.cs**

Create `Engine/SpanLayout.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using ShowCast.Core;
using SkiaSharp;

namespace ShowCast.Engine;

public sealed class SpanLayoutResult : IDisposable
{
    readonly List<LayoutLine> _lines;
    readonly SKRect[]         _charRects;
    readonly int[]            _charLineIndex;
    readonly string           _text;

    internal SpanLayoutResult(List<LayoutLine> lines, SKRect[] charRects,
                               int[] charLineIndex, string text)
    {
        _lines         = lines;
        _charRects     = charRects;
        _charLineIndex = charLineIndex;
        _text          = text;
    }

    // Paints are disposed inside SpanLayout.Compute before the result is returned.
    // IDisposable is implemented for call-site consistency (CanvasTextEditor calls Dispose
    // before recomputing on each keystroke) and to allow future resource ownership.
    public void Dispose() { }

    public IReadOnlyList<LayoutLine> Lines => _lines;

    public SKRect GetCharRect(int i)
    {
        i = Math.Clamp(i, 0, _text.Length);
        return _charRects[i];
    }

    public int HitTest(float x, float y)
    {
        if (_text.Length == 0) return 0;

        // Find nearest line by Y centre
        int bestLine = 0;
        float bestDist = float.MaxValue;
        for (int li = 0; li < _lines.Count; li++)
        {
            float mid  = _lines[li].Top + _lines[li].Height / 2f;
            float dist = Math.Abs(y - mid);
            if (dist < bestDist) { bestDist = dist; bestLine = li; }
        }

        // Within line, find nearest cursor position by X
        var line = _lines[bestLine];
        int best = line.CharStart;
        float bestX = float.MaxValue;
        for (int ci = line.CharStart; ci <= line.CharEnd; ci++)
        {
            if (ci > _text.Length) break;
            float dist = Math.Abs(x - _charRects[ci].Left);
            if (dist < bestX) { bestX = dist; best = ci; }
        }
        return best;
    }

    public int GetLineIndex(int charIndex)
    {
        charIndex = Math.Clamp(charIndex, 0, _text.Length);
        if (_charLineIndex.Length == 0) return 0;
        return _charLineIndex[Math.Min(charIndex, _charLineIndex.Length - 1)];
    }

    public int GetLineStart(int lineIndex)
    {
        if (_lines.Count == 0) return 0;
        lineIndex = Math.Clamp(lineIndex, 0, _lines.Count - 1);
        return _lines[lineIndex].CharStart;
    }

    public int GetLineEnd(int lineIndex)
    {
        if (_lines.Count == 0) return 0;
        lineIndex = Math.Clamp(lineIndex, 0, _lines.Count - 1);
        return _lines[lineIndex].CharEnd;
    }

    public int GetWordStart(int charIndex)
    {
        if (charIndex <= 0) return 0;
        int i = Math.Min(charIndex, _text.Length) - 1;
        while (i > 0 && char.IsWhiteSpace(_text[i])) i--;
        while (i > 0 && !char.IsWhiteSpace(_text[i - 1])) i--;
        return i;
    }

    public int GetWordEnd(int charIndex)
    {
        int i = Math.Min(charIndex, _text.Length);
        while (i < _text.Length && char.IsWhiteSpace(_text[i])) i++;
        while (i < _text.Length && !char.IsWhiteSpace(_text[i])) i++;
        return i;
    }

    public IEnumerable<SKRect> GetSelectionRects(int start, int end)
    {
        if (start >= end || _text.Length == 0) yield break;
        start = Math.Max(0, start);
        end   = Math.Min(_text.Length, end);

        int startLine = GetLineIndex(start);
        int endLine   = GetLineIndex(end == _text.Length ? end - 1 : end);
        if (end == _text.Length) endLine = _lines.Count - 1;

        for (int li = startLine; li <= endLine && li < _lines.Count; li++)
        {
            var line   = _lines[li];
            int selStart = li == startLine ? start : line.CharStart;
            int selEnd   = li == endLine   ? end   : line.CharEnd;
            if (selStart >= selEnd) continue;

            float x1 = _charRects[selStart].Left;
            float x2 = selEnd <= _text.Length ? _charRects[selEnd].Left : _charRects[_text.Length].Left;
            if (x2 <= x1) x2 = x1 + 4f;
            yield return new SKRect(x1, line.Top, x2, line.Top + line.Height);
        }
    }
}

public sealed class LayoutLine
{
    public IReadOnlyList<LayoutRun> Runs      { get; init; } = Array.Empty<LayoutRun>();
    public float                    Top       { get; init; }
    public float                    Height    { get; init; }
    public int                      CharStart { get; init; }
    public int                      CharEnd   { get; init; }  // exclusive
}

public sealed class LayoutRun
{
    public int   SpanIndex { get; init; }
    public int   CharStart { get; init; }
    public int   CharEnd   { get; init; }  // exclusive
    public float X         { get; init; }
    public float Baseline  { get; init; }  // baseline Y in display pixels (includes shift)
    public float Width     { get; init; }
    public float Height    { get; init; }
}

public static class SpanLayout
{
    public static SpanLayoutResult Compute(SlideLayer layer, Rect displayImageRect)
    {
        float bx = (float)(displayImageRect.X + layer.X * displayImageRect.Width);
        float by = (float)(displayImageRect.Y + layer.Y * displayImageRect.Height);
        float bw = (float)(layer.Width  * displayImageRect.Width);
        float bh = (float)(layer.Height * displayImageRect.Height);

        string text        = layer.EffectiveText;
        float  defaultLineH = layer.FontSize * (float)displayImageRect.Height * 1.25f;

        // ── Empty text ────────────────────────────────────────────────────────
        if (text.Length == 0)
        {
            float cursorY = layer.TextVAlign switch
            {
                TextVAlign.Bottom => by + bh - defaultLineH,
                TextVAlign.Middle => by + (bh - defaultLineH) / 2f,
                _                 => by
            };
            float cursorX = layer.TextHAlign switch
            {
                TextHAlign.Right  => bx + bw,
                TextHAlign.Center => bx + bw / 2f,
                _                 => bx
            };
            return new SpanLayoutResult(
                new List<LayoutLine>(),
                new[] { new SKRect(cursorX, cursorY, cursorX, cursorY + defaultLineH) },
                new[] { 0 },
                text);
        }

        // ── Build effective spans ──────────────────────────────────────────────
        IReadOnlyList<TextSpan> spans = layer.Spans.Count > 0
            ? layer.Spans
            : (IReadOnlyList<TextSpan>)new[] { new TextSpan { Text = layer.Text } };

        // Build per-span paint + metrics (disposed before return)
        var paints  = new List<(SKPaint p, SKTypeface tf, float lineH, float bshift, float kern)>();
        foreach (var span in spans)
        {
            float  fs     = (span.FontSize   ?? layer.FontSize)   * (float)displayImageRect.Height;
            string ff     = span.FontFamily  ?? layer.FontFamily;
            bool   bold   = span.Bold        ?? layer.Bold;
            bool   italic = span.Italic      ?? layer.Italic;
            float  bshift = (span.Baseline   ?? layer.Baseline)   * (float)displayImageRect.Height / 1080f;
            float  kern   = (span.Kerning    ?? layer.Kerning)    * (float)displayImageRect.Width  / 1920f;

            var style = (bold, italic) switch
            {
                (true,  true)  => SKFontStyle.BoldItalic,
                (true,  false) => SKFontStyle.Bold,
                (false, true)  => SKFontStyle.Italic,
                _              => SKFontStyle.Normal
            };
            var tf = SKTypeface.FromFamilyName(ff, style) ?? SKTypeface.Default;
            var p  = new SKPaint { TextSize = fs, IsAntialias = true, Typeface = tf };
            paints.Add((p, tf, fs * 1.25f, bshift, kern));
        }

        // ── Tokenise ──────────────────────────────────────────────────────────
        // Each token: (text, spanIndex, globalCharStart, measuredWidth, lineH, bshift, kern)
        var tokens = new List<(string txt, int si, int cs, float tw, float lh, float bs, float kn)>();
        int gi = 0;
        for (int si = 0; si < spans.Count; si++)
        {
            var (p, _, lh, bs, kn) = paints[si];
            var parts = spans[si].Text.Split('\n');
            for (int pi = 0; pi < parts.Length; pi++)
            {
                var words = parts[pi].Split(' ');
                for (int wi = 0; wi < words.Length; wi++)
                {
                    bool addSp = wi < words.Length - 1;
                    string tok = addSp ? words[wi] + " " : words[wi];
                    if (tok.Length > 0)
                    {
                        tokens.Add((tok, si, gi, p.MeasureText(tok), lh, bs, kn));
                        gi += tok.Length;
                    }
                }
                if (pi < parts.Length - 1)
                {
                    // Newline — forced wrap, width > bw
                    tokens.Add(("\n", si, gi, bw + 1f, lh, bs, kn));
                    gi++;
                }
            }
        }

        // ── Word-wrap ─────────────────────────────────────────────────────────
        var lineGroups = new List<List<(string txt, int si, int cs, float tw, float lh, float bs, float kn)>>();
        var cur = new List<(string txt, int si, int cs, float tw, float lh, float bs, float kn)>();
        float curW = 0f;
        foreach (var tok in tokens)
        {
            bool isNl = tok.txt == "\n";
            if (isNl || (cur.Count > 0 && curW + tok.tw > bw))
            {
                lineGroups.Add(cur);
                cur  = new();
                curW = 0f;
                if (isNl) continue;
            }
            cur.Add(tok);
            curW += tok.tw;
        }
        if (cur.Count > 0) lineGroups.Add(cur);
        if (lineGroups.Count == 0) lineGroups.Add(new());

        // ── Vertical layout ───────────────────────────────────────────────────
        float maxLH   = paints.Count > 0 ? paints.Max(p => p.lineH) : defaultLineH;
        float totalH  = lineGroups.Count * maxLH;
        float startY  = layer.TextVAlign switch
        {
            TextVAlign.Bottom => by + bh - totalH,
            TextVAlign.Middle => by + (bh - totalH) / 2f,
            _                 => by
        };

        // ── Build char rects ──────────────────────────────────────────────────
        var charRects     = new SKRect[text.Length + 1];
        var charLineIdx   = new int[text.Length + 1];
        var lines         = new List<LayoutLine>();
        float lineY = startY;

        for (int li = 0; li < lineGroups.Count; li++)
        {
            var ltoks  = lineGroups[li];
            float lineW = ltoks.Sum(t => t.tw);
            float lineX = layer.TextHAlign switch
            {
                TextHAlign.Right  => bx + bw - lineW,
                TextHAlign.Center => bx + (bw - lineW) / 2f,
                _                 => bx
            };
            float lineH    = ltoks.Count > 0 ? ltoks.Max(t => t.lh) : maxLH;
            float lineTop  = lineY;
            float baseline = lineTop + lineH * 0.8f;

            int lineCharStart = ltoks.Count > 0 ? ltoks[0].cs : (li > 0 ? lines[li-1].CharEnd : 0);
            int lineCharEnd   = lineCharStart;

            var runs = new List<LayoutRun>();
            float rx = lineX;

            foreach (var tok in ltoks)
            {
                var (p, _, _, bs, kn) = paints[tok.si];
                float runX    = rx;
                float runBase = baseline - bs;

                // Record each char position within this token (0..length inclusive)
                for (int ci = 0; ci <= tok.txt.Length; ci++)
                {
                    int gci = tok.cs + ci;
                    if (gci > text.Length) break;
                    float cx = runX + (ci > 0 ? p.MeasureText(tok.txt[..ci]) : 0f);
                    float cw = ci < tok.txt.Length ? p.MeasureText(tok.txt[ci..(ci+1)]) : 0f;
                    charRects[gci]   = new SKRect(cx, lineTop, cx + cw, lineTop + lineH);
                    charLineIdx[gci] = li;
                }

                runs.Add(new LayoutRun
                {
                    SpanIndex = tok.si,
                    CharStart = tok.cs,
                    CharEnd   = tok.cs + tok.txt.Length,
                    X         = runX,
                    Baseline  = runBase,
                    Width     = tok.tw + kn,
                    Height    = lineH,
                });

                rx += tok.tw + kn;
                lineCharEnd = tok.cs + tok.txt.Length;
            }

            // Cursor after last char on this line
            if (lineCharEnd <= text.Length)
            {
                charRects[lineCharEnd]   = new SKRect(rx, lineTop, rx, lineTop + lineH);
                charLineIdx[lineCharEnd] = li;
            }

            lines.Add(new LayoutLine
            {
                Runs      = runs,
                Top       = lineTop,
                Height    = lineH,
                CharStart = lineCharStart,
                CharEnd   = lineCharEnd,
            });
            lineY += lineH;
        }

        // Dispose paints
        foreach (var (p, tf, _, _, _) in paints) { p.Dispose(); tf.Dispose(); }

        return new SpanLayoutResult(lines, charRects, charLineIdx, text);
    }
}
```

---

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "SpanLayoutTests" -v minimal 2>&1 | tail -15
```

Expected: all SpanLayoutTests pass.

---

- [ ] **Step 5: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | tail -10
```

Expected: all pass.

---

- [ ] **Step 6: Commit**

```
git add Engine/SpanLayout.cs ShowCast.Tests/Engine/SpanLayoutTests.cs
git commit -m "feat(rich-text): SpanLayout engine — char rects, hit-test, selection rects, word navigation"
```

---

## Task 3: PageRenderer — Underline, Strikethrough, Baseline, Kerning

**Files:**
- Modify: `Engine/PageRenderer.cs`

No new unit tests (renderer requires a canvas). Build verification is the gate.

---

- [ ] **Step 1: Update DrawSpans to handle new attributes**

Open `Engine/PageRenderer.cs`. In `DrawSpans`, find the `foreach (var span in layer.Spans)` loop that builds `spanInfos`. After building each entry, extend the tuple to include the new attributes. Find and replace the `spanInfos` variable declaration and its build loop:

```csharp
        var spanInfos = new List<(string text, SKTypeface tf, SKPaint paint, float lineH,
                                  bool underline, bool strikethrough, float baselineShift, float kerning)>();
        foreach (var span in layer.Spans)
        {
            if (string.IsNullOrEmpty(span.Text)) continue;

            float  fs     = (span.FontSize   ?? layer.FontSize) * h;
            string ff     = span.FontFamily  ?? layer.FontFamily;
            bool   bold   = span.Bold        ?? layer.Bold;
            bool   italic = span.Italic      ?? layer.Italic;
            var    color  = span.Color       ?? layer.Color;
            bool   ul     = span.Underline     ?? layer.Underline;
            bool   st     = span.Strikethrough ?? layer.Strikethrough;
            float  bshift = (span.Baseline ?? layer.Baseline) * h / 1080f;
            float  kern   = (span.Kerning  ?? layer.Kerning)  * w / 1920f;

            var style = (bold, italic) switch
            {
                (true,  true)  => SKFontStyle.BoldItalic,
                (true,  false) => SKFontStyle.Bold,
                (false, true)  => SKFontStyle.Italic,
                _              => SKFontStyle.Normal,
            };
            var tf    = SKTypeface.FromFamilyName(ff, style) ?? SKTypeface.Default;
            var paint = new SKPaint
            {
                Color       = color,
                TextSize    = fs,
                IsAntialias = true,
                Typeface    = tf,
            };
            spanInfos.Add((span.Text, tf, paint, fs * 1.25f, ul, st, bshift, kern));
        }
```

---

- [ ] **Step 2: Update DrawSpans tokeniser to propagate new attributes**

In `DrawSpans`, find the token list build loop. The tokens need to carry `underline`, `strikethrough`, `baselineShift`, and `kerning`. Replace the tokens list and its build loop:

```csharp
        var tokens = new List<(string txt, SKPaint p, float tw, float lh,
                               bool ul, bool st, float bs, float kn)>();
        foreach (var (spanText, _, paint, lh, ul, st, bs, kn) in spanInfos)
        {
            var parts = spanText.Split('\n');
            for (int pi = 0; pi < parts.Length; pi++)
            {
                var words = parts[pi].Split(' ');
                for (int wi = 0; wi < words.Length; wi++)
                {
                    bool   addSpace = wi < words.Length - 1;
                    string tok      = addSpace ? words[wi] + " " : words[wi];
                    if (tok.Length == 0) continue;
                    float tw = paint.MeasureText(tok);
                    tokens.Add((tok, paint, tw, lh, ul, st, bs, kn));
                }
                if (pi < parts.Length - 1)
                    tokens.Add(("\n", paint, bw + 1, lh, ul, st, bs, kn));
            }
        }
```

---

- [ ] **Step 3: Update DrawSpans word-wrap to propagate new attributes**

Replace the `lines` list type and the wrap loop:

```csharp
        var lines = new List<List<(string txt, SKPaint p, float tw, float lh,
                                   bool ul, bool st, float bs, float kn)>>();
        var cur   = new List<(string txt, SKPaint p, float tw, float lh,
                              bool ul, bool st, float bs, float kn)>();
        float curW = 0;
        foreach (var tok in tokens)
        {
            bool isNewline = tok.txt == "\n";
            if (isNewline || (cur.Count > 0 && curW + tok.tw > bw))
            {
                lines.Add(cur);
                cur  = new();
                curW = 0;
                if (isNewline) continue;
            }
            cur.Add(tok);
            curW += tok.tw;
        }
        if (cur.Count > 0) lines.Add(cur);
```

---

- [ ] **Step 4: Update DrawSpans render loop to apply new attributes**

Replace **only** the inner `foreach (var (txt, paint, tw, _) in line)` block and the `y += lineH;` line — leave the `float baseline = y + lineH * 0.8f;` line that is already declared above it untouched:

```csharp
            foreach (var (txt, paint, tw, _, ul, st, bs, kn) in line)
            {
                float effectiveBaseline = baseline - bs;
                canvas.DrawText(txt, x, effectiveBaseline, paint);

                // Underline
                if (ul)
                {
                    float strokeW = paint.TextSize * 0.06f;
                    using var ulPaint = new SKPaint
                    {
                        Color       = paint.Color,
                        StrokeWidth = strokeW,
                        IsAntialias = true,
                    };
                    canvas.DrawLine(x, effectiveBaseline + strokeW, x + tw, effectiveBaseline + strokeW, ulPaint);
                }

                // Strikethrough
                if (st)
                {
                    float strokeW  = paint.TextSize * 0.06f;
                    float strikeY  = effectiveBaseline - paint.TextSize * 0.30f;
                    using var stPaint = new SKPaint
                    {
                        Color       = paint.Color,
                        StrokeWidth = strokeW,
                        IsAntialias = true,
                    };
                    canvas.DrawLine(x, strikeY, x + tw, strikeY, stPaint);
                }

                x += tw + kn;
            }
            y += lineH;
```

Also remove the old `float baseline = y + lineH * 0.8f;` line that existed before the foreach, since we now define it inline above.

---

- [ ] **Step 5: Update DisposeSpanInfos for new tuple shape**

Replace the `DisposeSpanInfos` method:

```csharp
    static void DisposeSpanInfos(
        List<(string text, SKTypeface tf, SKPaint paint, float lineH,
              bool ul, bool st, float baselineShift, float kerning)> infos)
    {
        foreach (var (_, tf, p, _, _, _, _, _) in infos) { tf.Dispose(); p.Dispose(); }
    }
```

---

- [ ] **Step 6: Update DrawPlainText for layer-level new attributes**

In `DrawPlainText`, find the render loop (the `for (int i = 0; i < lines.Count; i++)` block). Replace it with:

```csharp
        float baselineShift = layer.Baseline * h / 1080f;
        float kerning       = layer.Kerning  * w / 1920f;

        // Draw stroke under fill if set
        if (layer.StrokeWidth > 0 && layer.StrokeColor.Alpha > 0)
        {
            using var sp = new SKPaint
            {
                Color       = layer.StrokeColor.WithAlpha((byte)(layer.Opacity * 255)),
                TextSize    = fontSize,
                IsAntialias = true,
                Typeface    = tf,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = layer.StrokeWidth * h / 1080f,
                TextAlign   = skAlign
            };
            for (int i = 0; i < lines.Count; i++)
                canvas.DrawText(lines[i], textX, startY + i * lh - baselineShift, sp);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            float lineBaseline = startY + i * lh - baselineShift;
            canvas.DrawText(lines[i], textX, lineBaseline, paint);

            // Underline
            if (layer.Underline)
            {
                float sw = fontSize * 0.06f;
                using var ulp = new SKPaint { Color = paint.Color, StrokeWidth = sw, IsAntialias = true };
                float x1 = layer.TextHAlign == TextHAlign.Left  ? textX :
                           layer.TextHAlign == TextHAlign.Right ? textX - paint.MeasureText(lines[i]) :
                                                                   textX - paint.MeasureText(lines[i]) / 2f;
                canvas.DrawLine(x1, lineBaseline + sw, x1 + paint.MeasureText(lines[i]), lineBaseline + sw, ulp);
            }

            // Strikethrough
            if (layer.Strikethrough)
            {
                float sw     = fontSize * 0.06f;
                float strikeY = lineBaseline - fontSize * 0.30f;
                using var stp = new SKPaint { Color = paint.Color, StrokeWidth = sw, IsAntialias = true };
                float x1 = layer.TextHAlign == TextHAlign.Left  ? textX :
                           layer.TextHAlign == TextHAlign.Right ? textX - paint.MeasureText(lines[i]) :
                                                                   textX - paint.MeasureText(lines[i]) / 2f;
                canvas.DrawLine(x1, strikeY, x1 + paint.MeasureText(lines[i]), strikeY, stp);
            }
        }
```

Note: `kerning` is a per-run concept and doesn't apply to the plain-text single-block renderer. Ignore it in DrawPlainText.

---

- [ ] **Step 7: Build and verify**

```
dotnet build ShowCast.csproj -c Debug -v minimal 2>&1 | tail -5
```

Expected: 0 errors.

---

- [ ] **Step 8: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | tail -5
```

Expected: all pass.

---

- [ ] **Step 9: Commit**

```
git add Engine/PageRenderer.cs
git commit -m "feat(rich-text): PageRenderer renders underline, strikethrough, baseline shift, kerning"
```

---

## Task 4: SpanFormatInfo + CanvasTextEditor

**Files:**
- Create: `Views/SpanFormatInfo.cs`
- Create: `Views/CanvasTextEditor.cs`

No unit tests (UI-dependent). Build verification is the gate.

---

- [ ] **Step 1: Create Views/SpanFormatInfo.cs**

```csharp
using SkiaSharp;

namespace ShowCast.Views;

public sealed record SpanFormatInfo(
    bool?    Bold,
    bool?    Italic,
    float?   FontSize,
    string?  FontFamily,
    bool?    Underline,
    bool?    Strikethrough,
    float?   Baseline,
    float?   Kerning,
    SKColor? Color);
```

---

- [ ] **Step 2: Create Views/CanvasTextEditor.cs — skeleton with fields and constructor**

Create `Views/CanvasTextEditor.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using ShowCast.Core;
using ShowCast.Engine;
using ShowCast.ViewModels;
using SkiaSharp;

namespace ShowCast.Views;

public sealed class CanvasTextEditor
{
    // ── Dependencies ──────────────────────────────────────────────────────────
    readonly SlideLayer              _layer;
    readonly Canvas                  _overlay;
    readonly Action                  _rebuildSlide;
    readonly Action<SpanFormatInfo>  _spanFormatChanged;
    readonly MainViewModel           _vm;

    // ── Saved originals for Cancel() ─────────────────────────────────────────
    readonly List<TextSpan> _origSpans;
    readonly string         _origText;

    // ── Editor state ─────────────────────────────────────────────────────────
    Rect  _imageRect;
    int   _cursorIndex;
    int   _selStart  = -1;
    int   _selEnd    = -1;
    bool  _pointerDown;

    // ── Layout ───────────────────────────────────────────────────────────────
    SpanLayoutResult? _layout;

    // ── Overlay visuals ───────────────────────────────────────────────────────
    readonly Line            _cursorLine;
    readonly DispatcherTimer _blinkTimer = new() { Interval = TimeSpan.FromMilliseconds(530) };
    readonly List<Rectangle> _selRects   = new();
    bool                     _blinkOn    = true;

    // ── IME input ─────────────────────────────────────────────────────────────
    TextBox? _imeBox;

    public CanvasTextEditor(
        SlideLayer              layer,
        Canvas                  overlay,
        Rect                    imageRect,
        Action                  rebuildSlide,
        Action<SpanFormatInfo>  spanFormatChanged,
        MainViewModel           vm)
    {
        _layer             = layer;
        _overlay           = overlay;
        _imageRect         = imageRect;
        _rebuildSlide      = rebuildSlide;
        _spanFormatChanged = spanFormatChanged;
        _vm                = vm;

        _origText  = layer.Text;
        _origSpans = layer.Spans.Select(s => new TextSpan
        {
            Text          = s.Text,          Bold          = s.Bold,
            Italic        = s.Italic,        FontSize      = s.FontSize,
            FontFamily    = s.FontFamily,    Color         = s.Color,
            Underline     = s.Underline,     Strikethrough = s.Strikethrough,
            Baseline      = s.Baseline,      Kerning       = s.Kerning,
        }).ToList();

        _cursorLine = new Line
        {
            Stroke          = new SolidColorBrush(Colors.White),
            StrokeThickness = 2,
            IsHitTestVisible = false,
            IsVisible        = false,
        };
        _blinkTimer.Tick += (_, _) =>
        {
            _blinkOn = !_blinkOn;
            _cursorLine.IsVisible = _blinkOn;
        };
    }
```

---

- [ ] **Step 3: Add Open(), Commit(), Cancel(), Cleanup(), UpdateImageRect()**

Continuing in `CanvasTextEditor.cs`, add after the constructor:

```csharp
    public void Open(Point? clickPosition = null)
    {
        _vm.BeginLayerEdit();
        _overlay.Children.Add(_cursorLine);

        _layout      = SpanLayout.Compute(_layer, _imageRect);
        _cursorIndex = clickPosition.HasValue
            ? _layout.HitTest((float)clickPosition.Value.X, (float)clickPosition.Value.Y)
            : _layer.EffectiveText.Length;
        _selStart = _selEnd = -1;

        // IME box: off-screen TextBox purely for system IME composition
        _imeBox = new TextBox
        {
            Width = 1, Height = 1, Opacity = 0.01,
            IsHitTestVisible = false,
            Background       = Brushes.Transparent,
            BorderThickness  = new Thickness(0),
            Padding          = new Thickness(0),
        };
        Canvas.SetLeft(_imeBox, -200);
        Canvas.SetTop (_imeBox, -200);
        _imeBox.AddHandler(InputElement.KeyDownEvent,   OnImeKeyDown,   RoutingStrategies.Tunnel);
        _imeBox.AddHandler(InputElement.TextInputEvent, OnImeTextInput, RoutingStrategies.Tunnel);
        _overlay.Children.Add(_imeBox);
        Dispatcher.UIThread.Post(() => _imeBox?.Focus());

        _blinkOn = true;
        _blinkTimer.Start();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    public void Commit()
    {
        _vm.NotifySlideChanged();
        Cleanup();
    }

    public void Cancel()
    {
        _layer.Text = _origText;
        _layer.Spans.Clear();
        foreach (var s in _origSpans) _layer.Spans.Add(s);
        Cleanup();
        _rebuildSlide();
    }

    void Cleanup()
    {
        _blinkTimer.Stop();
        _layout?.Dispose();
        _layout = null;

        _overlay.Children.Remove(_cursorLine);
        foreach (var r in _selRects) _overlay.Children.Remove(r);
        _selRects.Clear();

        if (_imeBox is not null)
        {
            _imeBox.RemoveHandler(InputElement.KeyDownEvent,   OnImeKeyDown);
            _imeBox.RemoveHandler(InputElement.TextInputEvent, OnImeTextInput);
            _overlay.Children.Remove(_imeBox);
            _imeBox = null;
        }
    }

    public void UpdateImageRect(Rect imageRect)
    {
        _imageRect = imageRect;
        _layout?.Dispose();
        _layout = null;
        UpdateOverlayVisuals();
    }

    public SpanFormatInfo GetFormatAtCursor()
    {
        var f = SpanEditor.GetFormatAt(_layer, _cursorIndex);
        return new SpanFormatInfo(f.bold, f.italic, f.fontSize, f.fontFamily,
                                  f.underline, f.strikethrough, f.baseline, f.kerning, f.color);
    }
```

---

- [ ] **Step 4: Add text mutation methods**

Add after the previous block:

```csharp
    // ── Text mutation ─────────────────────────────────────────────────────────

    public void ApplyFormat(
        bool? bold = null, bool? italic = null, float? fontSize = null,
        string? fontFamily = null, bool? underline = null, bool? strikethrough = null,
        float? baseline = null, float? kerning = null, SKColor? color = null)
    {
        if (!HasSelection()) return;
        int start = Math.Min(_selStart, _selEnd);
        int end   = Math.Max(_selStart, _selEnd);
        SpanEditor.ApplyFormat(_layer, start, end, bold, italic, fontSize, fontFamily,
                               underline, strikethrough, baseline, kerning, color);
        RefreshAfterChange();
    }

    void InsertText(string text)
    {
        if (HasSelection()) DeleteSelection(fireRefresh: false);
        string oldText = _layer.EffectiveText;
        string newText = oldText.Insert(_cursorIndex, text);
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        _cursorIndex += text.Length;
        ClearSelection();
        RefreshAfterChange();
    }

    void DeleteBackward()
    {
        if (HasSelection()) { DeleteSelection(); return; }
        if (_cursorIndex == 0) return;
        string oldText = _layer.EffectiveText;
        string newText = oldText.Remove(_cursorIndex - 1, 1);
        _cursorIndex--;
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        RefreshAfterChange();
    }

    void DeleteForward()
    {
        if (HasSelection()) { DeleteSelection(); return; }
        string oldText = _layer.EffectiveText;
        if (_cursorIndex >= oldText.Length) return;
        string newText = oldText.Remove(_cursorIndex, 1);
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        RefreshAfterChange();
    }

    void DeleteSelection(bool fireRefresh = true)
    {
        if (!HasSelection()) return;
        int start = Math.Min(_selStart, _selEnd);
        int end   = Math.Max(_selStart, _selEnd);
        string oldText = _layer.EffectiveText;
        string newText = oldText.Remove(start, end - start);
        SpanEditor.ReconcileSpans(_layer, oldText, newText);
        _cursorIndex = start;
        ClearSelection();
        if (fireRefresh) RefreshAfterChange();
    }
```

---

- [ ] **Step 5: Add cursor movement methods**

Add after the mutation methods:

```csharp
    // ── Cursor movement ───────────────────────────────────────────────────────

    void MoveCursor(int newIndex, bool extending)
    {
        newIndex = Math.Clamp(newIndex, 0, _layer.EffectiveText.Length);
        if (extending)
        {
            if (!HasSelection()) _selStart = _cursorIndex;
            _selEnd = newIndex;
        }
        else
        {
            ClearSelection();
        }
        _cursorIndex = newIndex;
        ResetBlink();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    void MoveByLine(int direction, bool extending)
    {
        EnsureLayout();
        if (_layout!.Lines.Count == 0) return;
        int curLine    = _layout.GetLineIndex(_cursorIndex);
        int targetLine = curLine + direction;
        if (targetLine < 0)
        {
            MoveCursor(0, extending);
            return;
        }
        if (targetLine >= _layout.Lines.Count)
        {
            MoveCursor(_layer.EffectiveText.Length, extending);
            return;
        }
        var r   = _layout.GetCharRect(_cursorIndex);
        int idx = _layout.HitTest(r.Left,
            (float)(_layout.Lines[targetLine].Top + _layout.Lines[targetLine].Height / 2f));
        MoveCursor(idx, extending);
    }

    void SelectAll()
    {
        _selStart    = 0;
        _selEnd      = _layer.EffectiveText.Length;
        _cursorIndex = _selEnd;
        ResetBlink();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    void ToggleBold()
    {
        if (!HasSelection()) return;
        var f = SpanEditor.GetFormatAt(_layer, Math.Min(_selStart, _selEnd));
        ApplyFormat(bold: f.bold == true ? (bool?)false : true);
    }

    void ToggleItalic()
    {
        if (!HasSelection()) return;
        var f = SpanEditor.GetFormatAt(_layer, Math.Min(_selStart, _selEnd));
        ApplyFormat(italic: f.italic == true ? (bool?)false : true);
    }

    void ToggleUnderline()
    {
        if (!HasSelection()) return;
        var f = SpanEditor.GetFormatAt(_layer, Math.Min(_selStart, _selEnd));
        ApplyFormat(underline: f.underline == true ? (bool?)false : true);
    }
```

---

- [ ] **Step 6: Add pointer event handlers**

Add after the movement methods:

```csharp
    // ── Pointer events ────────────────────────────────────────────────────────

    public void OnPointerPressed(Point pt)
    {
        EnsureLayout();
        int idx = _layout!.HitTest((float)pt.X, (float)pt.Y);
        _cursorIndex = idx;
        _selStart    = idx;
        _selEnd      = idx;
        _pointerDown = true;
        ResetBlink();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    public void OnPointerMoved(Point pt, bool isDown)
    {
        if (!isDown || !_pointerDown) return;
        EnsureLayout();
        int idx = _layout!.HitTest((float)pt.X, (float)pt.Y);
        _selEnd      = idx;
        _cursorIndex = idx;
        UpdateOverlayVisuals();
    }

    public void OnPointerReleased()
    {
        _pointerDown = false;
        if (_selStart == _selEnd) ClearSelection();
    }
```

---

- [ ] **Step 7: Add keyboard handler**

Add after pointer events:

```csharp
    // ── Keyboard ──────────────────────────────────────────────────────────────

    void OnImeKeyDown(object? sender, KeyEventArgs e)   => OnKeyDown(e);

    void OnImeTextInput(object? sender, TextInputEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text)) return;
        InsertText(e.Text);
        e.Handled = true;
    }

    public void OnKeyDown(KeyEventArgs e)
    {
        bool ctrl  = e.KeyModifiers.HasFlag(KeyModifiers.Control);
        bool shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);

        switch (e.Key)
        {
            case Key.Escape:
                Cancel();
                e.Handled = true;
                return;
            case Key.Back:
                DeleteBackward();
                e.Handled = true;
                return;
            case Key.Delete:
                DeleteForward();
                e.Handled = true;
                return;
            case Key.Enter:
                InsertText("\n");
                e.Handled = true;
                return;
            case Key.Left:
                EnsureLayout();
                MoveCursor(ctrl ? _layout!.GetWordStart(_cursorIndex) : _cursorIndex - 1, shift);
                e.Handled = true;
                return;
            case Key.Right:
                EnsureLayout();
                MoveCursor(ctrl ? _layout!.GetWordEnd(_cursorIndex) : _cursorIndex + 1, shift);
                e.Handled = true;
                return;
            case Key.Up:
                MoveByLine(-1, shift);
                e.Handled = true;
                return;
            case Key.Down:
                MoveByLine(+1, shift);
                e.Handled = true;
                return;
            case Key.Home:
                EnsureLayout();
                MoveCursor(ctrl ? 0 : _layout!.GetLineStart(_layout.GetLineIndex(_cursorIndex)), shift);
                e.Handled = true;
                return;
            case Key.End:
                EnsureLayout();
                MoveCursor(ctrl ? _layer.EffectiveText.Length
                               : _layout!.GetLineEnd(_layout.GetLineIndex(_cursorIndex)), shift);
                e.Handled = true;
                return;
            case Key.A when ctrl:
                SelectAll();
                e.Handled = true;
                return;
            case Key.B when ctrl:
                ToggleBold();
                e.Handled = true;
                return;
            case Key.I when ctrl:
                ToggleItalic();
                e.Handled = true;
                return;
            case Key.U when ctrl:
                ToggleUnderline();
                e.Handled = true;
                return;
        }
    }
```

---

- [ ] **Step 8: Add overlay visual methods and helpers — close the class**

Add the final section to complete the class:

```csharp
    // ── Overlay visuals ───────────────────────────────────────────────────────

    void EnsureLayout()
    {
        _layout ??= SpanLayout.Compute(_layer, _imageRect);
    }

    void RefreshAfterChange()
    {
        _layout?.Dispose();
        _layout = null;
        _rebuildSlide();
        UpdateOverlayVisuals();
        FireSpanFormatChanged();
    }

    void UpdateOverlayVisuals()
    {
        EnsureLayout();
        UpdateCursorVisual();
        UpdateSelectionVisuals();
    }

    void UpdateCursorVisual()
    {
        if (_layout == null) { _cursorLine.IsVisible = false; return; }
        var r = _layout.GetCharRect(_cursorIndex);
        _cursorLine.StartPoint = new Point(r.Left, r.Top);
        _cursorLine.EndPoint   = new Point(r.Left, r.Bottom);
        _cursorLine.IsVisible  = _blinkOn;
    }

    void UpdateSelectionVisuals()
    {
        foreach (var r in _selRects) _overlay.Children.Remove(r);
        _selRects.Clear();
        if (!HasSelection() || _layout == null) return;

        int start = Math.Min(_selStart, _selEnd);
        int end   = Math.Max(_selStart, _selEnd);

        foreach (var rect in _layout.GetSelectionRects(start, end))
        {
            var vis = new Rectangle
            {
                Width            = Math.Max(4f, rect.Width),
                Height           = rect.Height,
                Fill             = new SolidColorBrush(Color.FromArgb(80, 59, 130, 246)),
                IsHitTestVisible = false,
            };
            Canvas.SetLeft(vis, rect.Left);
            Canvas.SetTop (vis, rect.Top);
            _overlay.Children.Insert(0, vis);
            _selRects.Add(vis);
        }
    }

    void ResetBlink()
    {
        _blinkTimer.Stop();
        _blinkOn              = true;
        _cursorLine.IsVisible = true;
        _blinkTimer.Start();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    bool HasSelection() => _selStart >= 0 && _selEnd >= 0 && _selStart != _selEnd;
    void ClearSelection() { _selStart = _selEnd = -1; }

    void FireSpanFormatChanged() => _spanFormatChanged(GetFormatAtCursor());
}
```

---

- [ ] **Step 9: Build and verify**

```
dotnet build ShowCast.csproj -c Debug -v minimal 2>&1 | tail -5
```

Expected: 0 errors.

---

- [ ] **Step 10: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | tail -5
```

Expected: all pass.

---

- [ ] **Step 11: Commit**

```
git add Views/SpanFormatInfo.cs Views/CanvasTextEditor.cs
git commit -m "feat(rich-text): CanvasTextEditor — WYSIWYG cursor/selection, keyboard, IME, ApplyFormat"
```

---

## Task 5: EditorCanvas Integration

**Files:**
- Modify: `Views/EditorCanvas.cs`

Replace the Avalonia TextBox inline editor with `CanvasTextEditor`. Surgical edit — only the inline-editing section changes.

---

- [ ] **Step 1: Remove old TextBox editor fields and add new fields**

Open `Views/EditorCanvas.cs`. Find the `// ── Inline text editing ───` section (around line 127). Replace the entire block from that comment through `public event Action<bool?, bool?, float?, string?>? InlineSpanFormatChanged;` with:

```csharp
    // ── Inline text editing ───────────────────────────────────────────────────
    CanvasTextEditor? _textEditor;

    public CanvasTextEditor? ActiveTextEditor => _textEditor;

    public event Action<SpanFormatInfo>? SpanFormatChanged;
```

---

- [ ] **Step 2: Replace BeginInlineEdit with BeginCustomEdit**

Find and delete the entire `BeginInlineEdit`, `OnInlineKeyDown`, `OnInlineLostFocus`, `CommitInlineEdit`, `CancelInlineEdit`, `RemoveInlineBox`, `HasRecentSpanSelection`, and `ApplySpanSelectionFormat` methods.

Add in their place (after the `IsInlineEditing` property):

```csharp
    public bool IsInlineEditing => _textEditor is not null;

    void BeginCustomEdit(SlideLayer layer, Point? clickPosition = null)
    {
        EndCustomEdit();
        _textEditor = new CanvasTextEditor(
            layer, _overlay, GetImageRect(),
            RebuildSlide,
            info => SpanFormatChanged?.Invoke(info),
            _vm!);
        _textEditor.Open(clickPosition);
    }

    void EndCustomEdit()
    {
        if (_textEditor is null) return;
        var ed = _textEditor;
        _textEditor = null;
        ed.Commit();
    }

    void CancelCustomEdit()
    {
        if (_textEditor is null) return;
        var ed = _textEditor;
        _textEditor = null;
        ed.Cancel();
    }
```

---

- [ ] **Step 3: Update OnDoubleTapped to use BeginCustomEdit**

Find `OnDoubleTapped`. Replace the line:

```csharp
        BeginInlineEdit(hit);
```

With:

```csharp
        BeginCustomEdit(hit, e.GetPosition(_overlay));
```

---

- [ ] **Step 4: Update OnPointerPressed to forward to CanvasTextEditor**

Find `OnPointerPressed`. Replace the first two lines of the method body:

```csharp
        if (_vm is null) return;
        if (_inlineBox is not null) { CommitInlineEdit(); return; }
```

With:

```csharp
        if (_vm is null) return;

        // Forward click into active text editor (for cursor placement / outside-click commit)
        if (_textEditor is not null)
        {
            var pt = e.GetPosition(_overlay);
            var ir = GetImageRect();
            var (nx, ny) = ToNorm(pt);
            bool insideLayer = _vm.SelectedLayer is { } sel2
                && nx >= sel2.X && nx <= sel2.X + sel2.Width
                && ny >= sel2.Y && ny <= sel2.Y + sel2.Height;
            if (insideLayer)
            {
                _textEditor.OnPointerPressed(pt);
                e.Handled = true;
                return;
            }
            EndCustomEdit();
            return;
        }
```

---

- [ ] **Step 5: Update OnPointerMoved and OnPointerReleased**

Find `OnPointerMoved`. At the top of the method body, after `var pt = e.GetPosition(_overlay);`, add:

```csharp
        if (_textEditor is not null)
        {
            _textEditor.OnPointerMoved(pt, e.GetCurrentPoint(_overlay).Properties.IsLeftButtonPressed);
            return;
        }
```

Find `OnPointerReleased`. At the top of the method body, add:

```csharp
        _textEditor?.OnPointerReleased();
```

---

- [ ] **Step 6: Update OnSizeChanged to forward image rect**

Find `OnSizeChanged`. After all existing calls (RebuildRulers, RebuildGrid, etc.), add:

```csharp
        _textEditor?.UpdateImageRect(GetImageRect());
```

---

- [ ] **Step 7: Update Dispose to clean up text editor**

Find the `Dispose()` method. Before the existing `_animTimer.Stop(); CommitInlineEdit();` line, replace `CommitInlineEdit()` with:

```csharp
        _animTimer.Stop();
        EndCustomEdit();
```

---

- [ ] **Step 8: Build and verify**

```
dotnet build ShowCast.csproj -c Debug -v minimal 2>&1 | tail -5
```

Expected: 0 errors. If there are errors about `_inlineBox` or old method references, delete those remaining references.

---

- [ ] **Step 9: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | tail -5
```

Expected: all pass.

---

- [ ] **Step 10: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat(rich-text): replace TextBox inline editor with CanvasTextEditor in EditorCanvas"
```

---

## Task 6: Inspector Updates

**Files:**
- Modify: `Views/EditorInspectorPanel.axaml`
- Modify: `Views/EditorInspectorPanel.axaml.cs`

---

- [ ] **Step 1: Add new controls to EditorInspectorPanel.axaml**

Open `Views/EditorInspectorPanel.axaml`. Find the existing Style row (the `<StackPanel Orientation="Horizontal"` block containing `BoldBtn` and `ItalicBtn`). After its closing `</StackPanel>` and before `<TextBlock Classes="field-label" Text="H Alignment"/>`, insert:

```xml
                    <TextBlock Classes="field-label" Text="Decoration"/>
                    <StackPanel Orientation="Horizontal" Spacing="4" Margin="0,0,0,6">
                        <ToggleButton Classes="option-toggle" x:Name="UnderlineBtn"
                                      Content="U" TextDecorations="Underline"
                                      Click="OnStyleClick"/>
                        <ToggleButton Classes="option-toggle" x:Name="StrikeBtn"
                                      Content="S" TextDecorations="Strikethrough"
                                      Click="OnStyleClick"/>
                    </StackPanel>

                    <TextBlock Classes="field-label" Text="Span Color"/>
                    <local:ColorPickerField x:Name="SpanColorPicker" Margin="0,0,0,6"/>

                    <Grid ColumnDefinitions="*,6,*">
                        <StackPanel Grid.Column="0">
                            <TextBlock Classes="field-label" Text="Baseline (px)"/>
                            <TextBox x:Name="BaselineBox"
                                     LostFocus="OnBaselineLostFocus"
                                     KeyDown="OnSingleLineKeyDown"/>
                        </StackPanel>
                        <StackPanel Grid.Column="2">
                            <TextBlock Classes="field-label" Text="Kerning (px)"/>
                            <TextBox x:Name="KerningBox"
                                     LostFocus="OnKerningLostFocus"
                                     KeyDown="OnSingleLineKeyDown"/>
                        </StackPanel>
                    </Grid>
```

---

- [ ] **Step 2: Update EditorInspectorPanel.axaml.cs — replace canvas wiring**

Open `Views/EditorInspectorPanel.axaml.cs`.

Replace the fields near the top of the class:

```csharp
    readonly List<IDisposable> _subs = new();
    bool _loading;
    EditorCanvas? _canvas;
```

Replace `SetCanvas` and `OnInlineSpanFormatChanged` with:

```csharp
    public void SetCanvas(EditorCanvas canvas)
    {
        if (_canvas is not null)
            _canvas.SpanFormatChanged -= OnSpanFormatChanged;
        _canvas = canvas;
        _canvas.SpanFormatChanged += OnSpanFormatChanged;
    }

    void OnSpanFormatChanged(SpanFormatInfo info)
    {
        _loading = true;
        try
        {
            BoldBtn.IsChecked   = info.Bold;
            ItalicBtn.IsChecked = info.Italic;
            UnderlineBtn.IsChecked  = info.Underline;
            StrikeBtn.IsChecked     = info.Strikethrough;
            FontSizeBox.Text = info.FontSize.HasValue
                ? ((int)(info.FontSize.Value * VH)).ToString()
                : VM?.SelectedLayer is { } l ? (l.FontSize * VH).ToString("F0") : "";
            if (info.FontFamily is not null)
                FontFamilyBox.SelectedItem = info.FontFamily;
            BaselineBox.Text = info.Baseline.HasValue
                ? info.Baseline.Value.ToString("F1")
                : VM?.SelectedLayer is { } l2 ? l2.Baseline.ToString("F1") : "0";
            KerningBox.Text = info.Kerning.HasValue
                ? info.Kerning.Value.ToString("F1")
                : VM?.SelectedLayer is { } l3 ? l3.Kerning.ToString("F1") : "0";
            if (info.Color.HasValue)
                SpanColorPicker.Value = info.Color.Value;
        }
        finally { _loading = false; }
    }
```

---

- [ ] **Step 3: Update LoadLayer for new fields and new controls**

In `LoadLayer`, in the `case LayerType.Text:` block, after the existing property assignments (after `TextStrokeWidthBox.Text = layer.StrokeWidth.ToString("F1");`), add:

```csharp
                    UnderlineBtn.IsChecked  = layer.Underline;
                    StrikeBtn.IsChecked     = layer.Strikethrough;
                    BaselineBox.Text        = layer.Baseline.ToString("F1");
                    KerningBox.Text         = layer.Kerning.ToString("F1");
                    SpanColorPicker.Value   = layer.Color;
```

---

- [ ] **Step 4: Update OnStyleClick to handle new buttons and use ActiveTextEditor**

Replace `OnStyleClick`:

```csharp
    void OnStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(
                bold:          sender == BoldBtn      ? BoldBtn.IsChecked      : null,
                italic:        sender == ItalicBtn    ? ItalicBtn.IsChecked    : null,
                underline:     sender == UnderlineBtn ? UnderlineBtn.IsChecked : null,
                strikethrough: sender == StrikeBtn    ? StrikeBtn.IsChecked    : null);
            return;
        }

        VM.BeginLayerEdit();
        if (sender == BoldBtn)      layer.Bold          = BoldBtn.IsChecked      == true;
        if (sender == ItalicBtn)    layer.Italic        = ItalicBtn.IsChecked    == true;
        if (sender == UnderlineBtn) layer.Underline     = UnderlineBtn.IsChecked == true;
        if (sender == StrikeBtn)    layer.Strikethrough = StrikeBtn.IsChecked    == true;
        VM.NotifySlideChanged();
    }
```

---

- [ ] **Step 5: Update OnFontSizeLostFocus to use ActiveTextEditor**

Replace `OnFontSizeLostFocus`:

```csharp
    void OnFontSizeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (!float.TryParse(FontSizeBox.Text, out float px) || px <= 0) return;
        float normalized = px / VH;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null) { editor.ApplyFormat(fontSize: normalized); return; }

        VM.BeginLayerEdit();
        layer.FontSize = normalized;
        VM.NotifySlideChanged();
    }
```

---

- [ ] **Step 6: Update OnFontFamilySelectionChanged to use ActiveTextEditor**

Replace `OnFontFamilySelectionChanged`:

```csharp
    void OnFontFamilySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (FontFamilyBox.SelectedItem is not string fam || string.IsNullOrEmpty(fam)) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null) { editor.ApplyFormat(fontFamily: fam); return; }

        VM.BeginLayerEdit();
        layer.FontFamily = fam;
        VM.NotifySlideChanged();
    }
```

---

- [ ] **Step 7: Add OnBaselineLostFocus, OnKerningLostFocus, and wire SpanColorPicker**

Add these new handler methods:

```csharp
    void OnBaselineLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (!float.TryParse(BaselineBox.Text, out float val)) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null) { editor.ApplyFormat(baseline: val); return; }

        VM.BeginLayerEdit();
        layer.Baseline = val;
        VM.NotifySlideChanged();
    }

    void OnKerningLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (!float.TryParse(KerningBox.Text, out float val)) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null) { editor.ApplyFormat(kerning: val); return; }

        VM.BeginLayerEdit();
        layer.Kerning = val;
        VM.NotifySlideChanged();
    }
```

For `SpanColorPicker`, wire it up in `OnDataContextChanged` (add next to the other color picker subscriptions):

```csharp
        SpanColorPicker.ColorChanged += OnSpanColorChanged;
```

And add the handler:

```csharp
    void OnSpanColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null) { editor.ApplyFormat(color: color); return; }

        VM.BeginLayerEdit();
        layer.Color = color;
        VM.NotifySlideChanged();
    }
```

Also unsubscribe in `OnDataContextChanged` before re-subscribing:

```csharp
        SpanColorPicker.ColorChanged -= OnSpanColorChanged;
```

---

- [ ] **Step 8: Update OnSingleLineKeyDown to handle new text boxes**

Find `OnSingleLineKeyDown`. Add the new cases:

```csharp
        else if (tb == BaselineBox) OnBaselineLostFocus(tb, null!);
        else if (tb == KerningBox)  OnKerningLostFocus(tb, null!);
```

---

- [ ] **Step 9: Build and verify**

```
dotnet build ShowCast.csproj -c Debug -v minimal 2>&1 | tail -5
```

Expected: 0 errors.

---

- [ ] **Step 10: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | tail -5
```

Expected: all pass.

---

- [ ] **Step 11: Commit**

```
git add Views/EditorInspectorPanel.axaml Views/EditorInspectorPanel.axaml.cs
git commit -m "feat(rich-text): inspector — underline/strike/baseline/kerning/span-color controls; wire ActiveTextEditor"
```

---

## Implementation Notes

**Coordinate space:** `SpanLayout.Compute` takes `Rect displayImageRect` and returns all positions in display pixel space (Avalonia overlay coordinates). Cursor Line and selection Rectangle controls are positioned directly with `Canvas.SetLeft/Top` from these rects — no scaling needed.

**WYSIWYG mechanism:** `CanvasTextEditor` mutates `layer.Spans` on every keystroke, then calls `_rebuildSlide()`. `PageRenderer.DrawSpans` renders the live styled text automatically. The cursor and selection overlays sit on top in `_overlay`. No extra render pass required.

**Cancel vs Commit:** `Cancel()` restores the original spans from `_origSpans` (deep-copied in constructor). `Commit()` just calls `NotifySlideChanged()` — edits have been accumulating in `layer.Spans` live throughout the session.

**No recency window:** The old 1-second timer was a workaround for `LostFocus` closing the TextBox before inspector button clicks fired. With `CanvasTextEditor`, the IME box (not a visible control) holds focus — inspector clicks don't cause focus loss — so `ActiveTextEditor` is always non-null while editing, making the timer unnecessary.

**Ctrl+U for underline:** GIMP supports `Ctrl+U`. We add it here as a natural extension of `Ctrl+B`/`Ctrl+I`.

**IME:** The off-screen `TextBox` (`_imeBox`) uses tunnel routing on `TextInputEvent` (so we intercept before the TextBox inserts text into its own buffer) and on `KeyDownEvent` (so navigation keys are handled by our handler with `e.Handled = true`, preventing TextBox default behavior). This preserves system IME composition for CJK input.
