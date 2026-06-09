using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class PageViewModelCountdownTests
{
    [Fact]
    public void UpdateCountdown_SetsProgressFractionAndHasProgress()
    {
        var vm = new PageViewModel(new Page { Name = "Test" });

        vm.UpdateCountdown(0.75, "3.5s");

        Assert.Equal(0.75, vm.ProgressFraction);
        Assert.True(vm.HasProgress);
    }

    [Fact]
    public void UpdateCountdown_LiveTimerLabel_ShowsCountdownText()
    {
        var vm = new PageViewModel(new Page { Name = "Test", DurationMs = 5000 });

        vm.UpdateCountdown(0.5, "2.5s");

        Assert.Equal("2.5s", vm.LiveTimerLabel);
    }

    [Fact]
    public void ClearCountdown_ResetsProgressAndFallsBackToStaticLabel()
    {
        var vm = new PageViewModel(new Page { Name = "Test", DurationMs = 5000 });
        vm.UpdateCountdown(0.5, "2.5s");

        vm.ClearCountdown();

        Assert.Equal(0.0, vm.ProgressFraction);
        Assert.False(vm.HasProgress);
        Assert.Equal("5s", vm.LiveTimerLabel); // falls back to static TimerLabel
    }

    [Fact]
    public void LiveTimerLabel_NoCountdownActive_ReturnsStaticTimerLabel()
    {
        var vm = new PageViewModel(new Page { Name = "Test", DurationMs = 3000 });

        Assert.Equal("3s", vm.LiveTimerLabel);
    }

    [Fact]
    public void LiveTimerLabel_NoTimerAndNoCountdown_ReturnsNull()
    {
        var vm = new PageViewModel(new Page { Name = "Test" }); // DurationMs = 0

        Assert.Null(vm.LiveTimerLabel);
    }
}
