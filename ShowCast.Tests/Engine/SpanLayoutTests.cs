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

        var r0 = result.GetCharRect(0);
        var r1 = result.GetCharRect(1);
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

        int hit = result.HitTest(-100f, result.GetCharRect(0).Top + 5f);
        Assert.Equal(0, hit);
    }

    [Fact]
    public void HitTest_ClickAfterLastChar_ReturnsTextLength()
    {
        var layer = SingleSpanLayer("Hello");
        var result = SpanLayout.Compute(layer, TestRect);

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

        Assert.Equal(6, result.GetWordStart(10));
        Assert.Equal(0, result.GetWordStart(4));
    }

    [Fact]
    public void GetWordEnd_SkipsToWordEnd()
    {
        var layer = SingleSpanLayer("Hello World");
        var result = SpanLayout.Compute(layer, TestRect);

        Assert.Equal(5, result.GetWordEnd(1));
        Assert.Equal(11, result.GetWordEnd(7));
    }
}
