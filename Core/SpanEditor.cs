using System;
using System.Collections.Generic;
using System.Linq;

namespace ShowCast.Core;

public static class SpanEditor
{
    /// <summary>
    /// Apply formatting to characters [selStart, selEnd) in layer.Spans.
    /// Splits spans at boundaries, applies overrides, merges adjacent identical spans.
    /// If selStart == selEnd, does nothing (no selection).
    /// </summary>
    public static void ApplyFormat(SlideLayer layer, int selStart, int selEnd,
                                   bool? bold = null, bool? italic = null,
                                   float? fontSize = null, string? fontFamily = null)
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
                if (bold.HasValue)           span.Bold       = bold;
                if (italic.HasValue)         span.Italic     = italic;
                if (fontSize.HasValue)       span.FontSize   = fontSize;
                if (fontFamily is not null)  span.FontFamily = fontFamily;
            }
            ci = spanEnd;
        }

        Merge(layer.Spans);
    }

    /// <summary>
    /// Returns the formatting of the span that covers character position pos.
    /// All values are nullable (null = inherit from layer).
    /// </summary>
    public static (bool? bold, bool? italic, float? fontSize, string? fontFamily)
        GetFormatAt(SlideLayer layer, int pos)
    {
        if (layer.Spans.Count == 0) return default;
        int ci = 0;
        foreach (var span in layer.Spans)
        {
            ci += span.Text.Length;
            if (pos < ci) return (span.Bold, span.Italic, span.FontSize, span.FontFamily);
        }
        var last = layer.Spans[^1];
        return (last.Bold, last.Italic, last.FontSize, last.FontFamily);
    }

    /// <summary>
    /// Reconcile layer.Spans after the inline editor text changed.
    /// Preserves span formatting for unchanged prefix/suffix; the changed
    /// middle gets the format of the first span that was touched.
    /// If the result is a single unformatted span, promotes to layer.Text.
    /// </summary>
    public static void ReconcileSpans(SlideLayer layer, string oldText, string newText)
    {
        if (layer.Spans.Count == 0) { layer.Text = newText; return; }
        if (oldText == newText) return;

        // Common prefix/suffix lengths
        int p = 0;
        while (p < oldText.Length && p < newText.Length && oldText[p] == newText[p]) p++;
        int maxS = Math.Min(oldText.Length - p, newText.Length - p);
        int s = 0;
        while (s < maxS && oldText[oldText.Length - 1 - s] == newText[newText.Length - 1 - s]) s++;

        // Anchor span: first span that covers position p
        int anchor = GetSpanIndexAt(layer.Spans, p);
        TextSpan anchorSpan = layer.Spans[anchor];

        var result = new List<TextSpan>();

        // Prefix [0, p)
        if (p > 0)
        {
            int ci = 0;
            foreach (var span in layer.Spans)
            {
                if (ci >= p) break;
                int take = Math.Min(span.Text.Length, p - ci);
                if (take > 0) result.Add(Clone(span, span.Text[..take]));
                ci += span.Text.Length;
            }
        }

        // Changed middle — inherits anchor span format
        int midLen = newText.Length - p - s;
        if (midLen > 0)
            result.Add(Clone(anchorSpan, newText.Substring(p, midLen)));

        // Suffix [oldText.Length - s, oldText.Length)
        if (s > 0)
        {
            int suffixStart = oldText.Length - s;
            int ci = 0;
            foreach (var span in layer.Spans)
            {
                int spanEnd = ci + span.Text.Length;
                if (spanEnd > suffixStart)
                {
                    int skip = Math.Max(0, suffixStart - ci);
                    string portion = span.Text[skip..];
                    if (portion.Length > 0) result.Add(Clone(span, portion));
                }
                ci = spanEnd;
            }
        }

        layer.Spans.Clear();
        foreach (var sp in Merged(result).Where(sp => sp.Text.Length > 0))
            layer.Spans.Add(sp);

        // Single unformatted span → collapse to layer.Text
        if (layer.Spans.Count == 1 && !HasOverride(layer.Spans[0]))
        {
            layer.Text = layer.Spans[0].Text;
            layer.Spans.Clear();
        }
    }

    // ── Private helpers ────────────────────────────────────────────────────────

    static void SplitAt(List<TextSpan> spans, int pos)
    {
        if (pos <= 0) return;
        int ci = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            int spanEnd = ci + spans[i].Text.Length;
            if (ci < pos && spanEnd > pos)
            {
                int local = pos - ci;
                var a = Clone(spans[i], spans[i].Text[..local]);
                var b = Clone(spans[i], spans[i].Text[local..]);
                spans[i] = a;
                spans.Insert(i + 1, b);
                return;
            }
            ci = spanEnd;
            if (ci >= pos) return;
        }
    }

    static void Merge(List<TextSpan> spans)
    {
        var merged = Merged(spans).ToList();
        spans.Clear();
        spans.AddRange(merged);
    }

    static IEnumerable<TextSpan> Merged(IEnumerable<TextSpan> spans)
    {
        TextSpan? cur = null;
        foreach (var sp in spans)
        {
            if (cur is null) { cur = sp; continue; }
            if (SameFormat(cur, sp)) cur = Clone(cur, cur.Text + sp.Text);
            else { yield return cur; cur = sp; }
        }
        if (cur is not null) yield return cur;
    }

    static int GetSpanIndexAt(IReadOnlyList<TextSpan> spans, int pos)
    {
        int ci = 0;
        for (int i = 0; i < spans.Count; i++)
        {
            ci += spans[i].Text.Length;
            if (pos < ci) return i;
        }
        return spans.Count - 1;
    }

    static TextSpan Clone(TextSpan src, string text) => new()
    {
        Text = text, Bold = src.Bold, Italic = src.Italic,
        FontSize = src.FontSize, FontFamily = src.FontFamily, Color = src.Color
    };

    static bool HasOverride(TextSpan s) =>
        s.Bold.HasValue || s.Italic.HasValue || s.FontSize.HasValue
        || s.FontFamily is not null || s.Color.HasValue;

    static bool SameFormat(TextSpan a, TextSpan b) =>
        a.Bold == b.Bold && a.Italic == b.Italic && a.FontSize == b.FontSize
        && a.FontFamily == b.FontFamily && a.Color == b.Color;
}
