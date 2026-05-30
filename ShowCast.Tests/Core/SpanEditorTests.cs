using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class SpanEditorTests
{
    static SlideLayer LayerWithText(string text)
    {
        var l = new SlideLayer { Type = LayerType.Text };
        l.Spans.Add(new TextSpan { Text = text });
        return l;
    }

    static SlideLayer LayerWithSpans(params (string text, bool? bold, bool? italic)[] spans)
    {
        var l = new SlideLayer { Type = LayerType.Text };
        foreach (var (text, bold, italic) in spans)
            l.Spans.Add(new TextSpan { Text = text, Bold = bold, Italic = italic });
        return l;
    }

    [Fact]
    public void ApplyFormat_SplitsAndAppliesBold_ToMiddleOfSingleSpan()
    {
        var layer = LayerWithText("Hello World");
        SpanEditor.ApplyFormat(layer, 0, 5, bold: true);

        Assert.Equal(2, layer.Spans.Count);
        Assert.Equal("Hello", layer.Spans[0].Text);
        Assert.True(layer.Spans[0].Bold);
        Assert.Equal(" World", layer.Spans[1].Text);
        Assert.Null(layer.Spans[1].Bold);
    }

    [Fact]
    public void ApplyFormat_MergesAdjacentSpans_WhenFormatBecomesIdentical()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", true, null));
        SpanEditor.ApplyFormat(layer, 0, 11, bold: false);

        Assert.Single(layer.Spans);
        Assert.Equal("Hello World", layer.Spans[0].Text);
        Assert.False(layer.Spans[0].Bold);
    }

    [Fact]
    public void ApplyFormat_CreatesSpanFromLayerText_WhenNoSpansExist()
    {
        var layer = new SlideLayer { Type = LayerType.Text, Text = "Hello" };
        SpanEditor.ApplyFormat(layer, 0, 3, bold: true);

        Assert.Equal(2, layer.Spans.Count);
        Assert.Equal("Hel", layer.Spans[0].Text);
        Assert.True(layer.Spans[0].Bold);
        Assert.Equal("lo", layer.Spans[1].Text);
        Assert.Null(layer.Spans[1].Bold);
    }

    [Fact]
    public void ApplyFormat_DoesNothing_WhenSelStartEqualsSelEnd()
    {
        var layer = LayerWithText("Hello");
        SpanEditor.ApplyFormat(layer, 2, 2, bold: true);

        Assert.Single(layer.Spans);
        Assert.Null(layer.Spans[0].Bold);
    }

    [Fact]
    public void GetFormatAt_ReturnsFormatOfCorrectSpan()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, true));

        var (bold, italic, _, _) = SpanEditor.GetFormatAt(layer, 0);
        Assert.True(bold);
        Assert.Null(italic);

        (bold, italic, _, _) = SpanEditor.GetFormatAt(layer, 7);
        Assert.Null(bold);
        Assert.True(italic);
    }

    [Fact]
    public void GetFormatAt_ReturnsLastSpan_WhenPosAtEnd()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, true));
        var (bold, italic, _, _) = SpanEditor.GetFormatAt(layer, 11);
        Assert.Null(bold);
        Assert.True(italic);
    }

    [Fact]
    public void ReconcileSpans_PreservesFormatting_WhenCharAppendedAfterSecondSpan()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, null));
        SpanEditor.ReconcileSpans(layer, "Hello World", "Hello World!");

        Assert.Equal(2, layer.Spans.Count);
        Assert.Equal("Hello", layer.Spans[0].Text);
        Assert.True(layer.Spans[0].Bold);
        Assert.Equal(" World!", layer.Spans[1].Text);
        Assert.Null(layer.Spans[1].Bold);
    }

    [Fact]
    public void ReconcileSpans_CollapsesToLayerText_WhenResultIsUnformatted()
    {
        var layer = LayerWithText("Hello");
        SpanEditor.ReconcileSpans(layer, "Hello", "Hi");

        Assert.Empty(layer.Spans);
        Assert.Equal("Hi", layer.Text);
    }

    [Fact]
    public void ReconcileSpans_NoOp_WhenTextUnchanged()
    {
        var layer = LayerWithSpans(("Hello", true, null), (" World", null, null));
        SpanEditor.ReconcileSpans(layer, "Hello World", "Hello World");

        Assert.Equal(2, layer.Spans.Count);
    }
}
