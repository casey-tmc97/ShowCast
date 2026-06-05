// ShowCast.Tests/Blackmagic/DeckLinkApiTests.cs
using ShowCast.Blackmagic;
using Xunit;

namespace ShowCast.Tests.Blackmagic;

public class DeckLinkApiTests
{
    [Fact]
    public void TryInitialize_DoesNotThrow()
    {
        // Must not throw whether or not the DeckLink driver is installed.
        var ex = Record.Exception(() => DeckLinkApi.TryInitialize());
        Assert.Null(ex);
    }

    [Fact]
    public void EnumerateDevices_ReturnsEmptyWhenUnavailable()
    {
        DeckLinkApi.TryInitialize();
        if (!DeckLinkApi.IsAvailable)
            Assert.Empty(DeckLinkApi.EnumerateDevices());
        // If driver IS available this test is a no-op (result may be non-empty).
    }

    [Fact]
    public void GetDisplayMode_KnownResolution_ReturnsNonZero()
    {
        int mode = DeckLinkApi.GetDisplayMode(1920, 1080, 59.94);
        Assert.NotEqual(0, mode);
    }

    [Fact]
    public void GetDisplayMode_UnknownResolution_ReturnsFallback()
    {
        // 800x600 is not a broadcast mode — must return the 1080p59.94 fallback.
        int mode = DeckLinkApi.GetDisplayMode(800, 600, 30.0);
        Assert.Equal(0x48703539, mode); // bmdModeHD1080p5994
    }
}
