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
            var line     = _lines[li];
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

        string text         = layer.EffectiveText;
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

        // ── Build effective spans ─────────────────────────────────────────────
        IReadOnlyList<TextSpan> spans = layer.Spans.Count > 0
            ? layer.Spans
            : (IReadOnlyList<TextSpan>)new[] { new TextSpan { Text = layer.Text } };

        // Build per-span paint + metrics (disposed before return)
        var paints = new List<(SKPaint p, SKTypeface tf, float lineH, float bshift, float kern)>();
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
                    tokens.Add(("\n", si, gi, bw + 1f, lh, bs, kn));
                    gi++;
                }
            }
        }

        // ── Word-wrap ─────────────────────────────────────────────────────────
        var lineGroups      = new List<List<(string txt, int si, int cs, float tw, float lh, float bs, float kn)>>();
        var lineGroupStarts = new List<int>();   // char index where each line starts
        var cur             = new List<(string txt, int si, int cs, float tw, float lh, float bs, float kn)>();
        float curW    = 0f;
        int   nextStart = 0;
        foreach (var tok in tokens)
        {
            bool isNl = tok.txt == "\n";
            if (isNl || (cur.Count > 0 && curW + tok.tw > bw))
            {
                lineGroups.Add(cur);
                lineGroupStarts.Add(nextStart);
                cur       = new();
                curW      = 0f;
                nextStart = isNl ? tok.cs + 1 : (cur.Count > 0 ? cur[0].cs : nextStart);
                if (isNl) { nextStart = tok.cs + 1; continue; }
            }
            if (cur.Count == 0) nextStart = tok.cs;
            cur.Add(tok);
            curW += tok.tw;
        }
        if (cur.Count > 0) { lineGroups.Add(cur); lineGroupStarts.Add(nextStart); }
        if (lineGroups.Count == 0) { lineGroups.Add(new()); lineGroupStarts.Add(0); }

        // ── Vertical layout ───────────────────────────────────────────────────
        float maxLH  = paints.Count > 0 ? paints.Max(p => p.lineH) : defaultLineH;
        float totalH = lineGroups.Count * maxLH;
        float startY = layer.TextVAlign switch
        {
            TextVAlign.Bottom => by + bh - totalH,
            TextVAlign.Middle => by + (bh - totalH) / 2f,
            _                 => by
        };

        // ── Build char rects ──────────────────────────────────────────────────
        var charRects    = new SKRect[text.Length + 1];
        var charLineIdx  = new int[text.Length + 1];
        var lines        = new List<LayoutLine>();
        float lineY = startY;

        for (int li = 0; li < lineGroups.Count; li++)
        {
            var ltoks   = lineGroups[li];
            float lineW = ltoks.Sum(t => t.tw);
            float lineX = layer.TextHAlign switch
            {
                TextHAlign.Right  => bx + bw - lineW,
                TextHAlign.Center => bx + (bw - lineW) / 2f,
                _                 => bx
            };
            float lineH   = ltoks.Count > 0 ? ltoks.Max(t => t.lh) : maxLH;
            float lineTop = lineY;
            float baseline = lineTop + lineH * 0.8f;

            int lineCharStart = lineGroupStarts[li];
            int lineCharEnd   = lineCharStart;

            var runs = new List<LayoutRun>();
            float rx = lineX;

            foreach (var tok in ltoks)
            {
                var (p, _, _, bs, kn) = paints[tok.si];
                float runX    = rx;
                float runBase = baseline - bs;

                // Record each char position (0..length inclusive)
                float accW = 0f;
                for (int ci = 0; ci <= tok.txt.Length; ci++)
                {
                    int gci = tok.cs + ci;
                    if (gci > text.Length) break;
                    float cw = ci < tok.txt.Length ? p.MeasureText(tok.txt[ci..(ci+1)]) : 0f;
                    charRects[gci]   = new SKRect(runX + accW, lineTop, runX + accW + cw, lineTop + lineH);
                    charLineIdx[gci] = li;
                    accW += cw;
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

            // Cursor after last char on this line (skip for empty lines — char already placed by previous line)
            if (ltoks.Count > 0 && lineCharEnd <= text.Length)
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
