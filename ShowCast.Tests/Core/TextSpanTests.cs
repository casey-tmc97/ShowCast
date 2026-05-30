using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class TextSpanTests
{
    [Fact]
    public void SlideLayer_Clone_DeepClonesSpans()
    {
        var layer = new SlideLayer { Type = LayerType.Text, Text = "Hello" };
        layer.Spans.Add(new TextSpan { Text = "Hello", Bold = true });

        var clone = layer.Clone(newId: false);

        Assert.Single(clone.Spans);
        Assert.Equal("Hello", clone.Spans[0].Text);
        Assert.True(clone.Spans[0].Bold);

        // Mutation isolation
        clone.Spans[0].Text = "Changed";
        Assert.Equal("Hello", layer.Spans[0].Text);
    }

    [Fact]
    public void EffectiveText_ReturnsSpansConcatenated_WhenSpansExist()
    {
        var layer = new SlideLayer { Type = LayerType.Text, Text = "Fallback" };
        layer.Spans.Add(new TextSpan { Text = "Hello " });
        layer.Spans.Add(new TextSpan { Text = "World" });

        Assert.Equal("Hello World", layer.EffectiveText);
    }

    [Fact]
    public void EffectiveText_FallsBackToText_WhenNoSpans()
    {
        var layer = new SlideLayer { Type = LayerType.Text, Text = "Legacy" };
        Assert.Equal("Legacy", layer.EffectiveText);
    }
}
