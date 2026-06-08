using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class CompanionServerTests
{
    [Fact]
    public void OutputState_Blank_ClearsLivePage()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);

        state.Blank();

        Assert.Null(state.LivePage);
    }

    [Fact]
    public void OutputState_Unblank_RestoresLivePage()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);
        state.Blank();

        state.Unblank();

        Assert.Equal(page, state.LivePage);
        Assert.Equal(0, state.LivePageIndex);
    }

    [Fact]
    public void OutputState_Unblank_WithoutPriorBlank_DoesNothing()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);

        state.Unblank(); // must not throw

        Assert.Null(state.LivePage);
    }

    [Fact]
    public void OutputState_Blank_WhenAlreadyBlank_DoesNotOverwriteSavedState()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);
        state.Blank(); // first blank saves page

        state.Blank(); // second blank should be a no-op

        state.Unblank();
        Assert.Equal(page, state.LivePage); // original page still restored
    }
}
