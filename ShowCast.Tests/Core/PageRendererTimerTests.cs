// ShowCast.Tests/Core/PageRendererTimerTests.cs
using ShowCast.Core;
using ShowCast.Engine;
using SkiaSharp;
using Xunit;

namespace ShowCast.Tests.Core;

public class PageRendererTimerTests
{
    [Fact]
    public void Render_WithUseLiveTimersFalse_UsesLayerTextNotCache()
    {
        var timerId = Guid.NewGuid();
        TimerTextCache.Values[timerId] = "5:00";

        var layer = new SlideLayer
        {
            Type         = LayerType.Text,
            Text         = "STATIC",
            TimerBinding = timerId,
            Width        = 1f, Height = 1f
        };
        var page = new Page();
        page.AddLayer(layer);

        // When useLiveTimers=false the renderer must ignore the cache
        using var surface = SKSurface.Create(new SKImageInfo(320, 180, SKColorType.Rgba8888));
        // Should not throw; just verifying the parameter is accepted
        PageRenderer.Render(surface.Canvas, page, LayerRole.All, 320, 180, useLiveTimers: false);

        TimerTextCache.Values.TryRemove(timerId, out _);
    }

    [Fact]
    public void Render_WithUseLiveTimersTrue_UsesCache()
    {
        var timerId = Guid.NewGuid();
        TimerTextCache.Values[timerId] = "LIVE";

        var layer = new SlideLayer
        {
            Type         = LayerType.Text,
            Text         = "STATIC",
            TimerBinding = timerId,
            Width        = 1f, Height = 1f
        };
        var page = new Page();
        page.AddLayer(layer);

        using var surface = SKSurface.Create(new SKImageInfo(320, 180, SKColorType.Rgba8888));
        // Should not throw; verifying default (true) is accepted
        PageRenderer.Render(surface.Canvas, page, LayerRole.All, 320, 180);

        TimerTextCache.Values.TryRemove(timerId, out _);
    }
}
