using Avalonia;
using ShowCast.Views;
using Xunit;

namespace ShowCast.Tests.Views;

public class EditorCanvasZoomTests
{
    [Fact]
    public void ComputeZoomedRect_AtZoomOne_ReturnsUnchangedRect()
    {
        var baseRect = new Rect(100, 50, 640, 360);
        var result = EditorCanvas.ComputeZoomedRect(baseRect, 1.0, new Point(0.5, 0.5));
        Assert.Equal(baseRect.X,      result.X,      precision: 4);
        Assert.Equal(baseRect.Y,      result.Y,      precision: 4);
        Assert.Equal(baseRect.Width,  result.Width,  precision: 4);
        Assert.Equal(baseRect.Height, result.Height, precision: 4);
    }

    [Fact]
    public void ComputeZoomedRect_AtZoomTwo_CenterOrigin_DoublesSizeKeepsCenter()
    {
        // Base rect: (0, 0, 400, 225). Center origin (0.5, 0.5) → screen pt (200, 112.5).
        // After 2×: size=(800,450), x=200-0.5*800=-200, y=112.5-0.5*450=-112.5
        var baseRect = new Rect(0, 0, 400, 225);
        var result = EditorCanvas.ComputeZoomedRect(baseRect, 2.0, new Point(0.5, 0.5));
        Assert.Equal(800,    result.Width,  precision: 4);
        Assert.Equal(450,    result.Height, precision: 4);
        Assert.Equal(-200,   result.X,      precision: 4);
        Assert.Equal(-112.5, result.Y,      precision: 4);
    }

    [Fact]
    public void ComputeZoomedRect_AtZoomTwo_TopLeftOrigin_TopLeftStaysFixed()
    {
        // Origin (0,0) = top-left of slide → screen pt (100,50).
        // After 2×: x=100-0*800=100, y=50-0*450=50
        var baseRect = new Rect(100, 50, 400, 225);
        var result = EditorCanvas.ComputeZoomedRect(baseRect, 2.0, new Point(0, 0));
        Assert.Equal(100, result.X,      precision: 4);
        Assert.Equal(50,  result.Y,      precision: 4);
        Assert.Equal(800, result.Width,  precision: 4);
        Assert.Equal(450, result.Height, precision: 4);
    }
}
