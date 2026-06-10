using System;
using System.Reflection;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelCountdownTests
{
    /// <summary>
    /// Creates a MainViewModel with one package containing one page.
    /// Sets DurationMs on the page via the PageViewModel setter so HasTimer is correct.
    /// </summary>
    static MainViewModel CreateVm(out PageViewModel pageVm, int durationMs = 3000)
    {
        var vm = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages[^1]; // last package (the one just added)

        vm.LoadPackageToSelectedOutput(pkg);
        vm.SelectedPage = vm.Pages[0];
        pageVm = vm.Pages[0];
        pageVm.DurationMs = durationMs; // use VM setter to raise HasTimer notification
        return vm;
    }

    static void CallTickCountdown(MainViewModel vm)
    {
        var method = typeof(MainViewModel).GetMethod("TickCountdown",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("TickCountdown not found");
        method.Invoke(vm, null);
    }

    static string CallFormatCountdown(double remainingSec)
    {
        var method = typeof(MainViewModel).GetMethod("FormatCountdown",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("FormatCountdown not found");
        return (string)method.Invoke(null, new object[] { remainingSec })!;
    }

    // ── FormatCountdown ───────────────────────────────────────────────────────

    [Fact]
    public void FormatCountdown_UnderOneMinute_ReturnsZeroMinutes()
    {
        Assert.Equal("0:14", CallFormatCountdown(14.8));
        Assert.Equal("0:10", CallFormatCountdown(10.0));
        Assert.Equal("0:04", CallFormatCountdown(4.2));
        Assert.Equal("0:09", CallFormatCountdown(9.9));
    }

    [Fact]
    public void FormatCountdown_OverOneMinute_ReturnsMinutesAndSeconds()
    {
        Assert.Equal("1:30", CallFormatCountdown(90.0));
        Assert.Equal("2:05", CallFormatCountdown(125.7));
    }

    [Fact]
    public void FormatCountdown_Zero_ReturnsZeroLabel()
    {
        Assert.Equal("0:00", CallFormatCountdown(0.0));
        Assert.Equal("0:00", CallFormatCountdown(-1.0));
    }

    // ── GoLive / TickCountdown ────────────────────────────────────────────────

    [Fact]
    public void GoLive_TimerPage_AfterTick_HasProgressIsTrueAndFractionInRange()
    {
        var vm = CreateVm(out var pageVm, durationMs: 5000);
        vm.GoLive();

        CallTickCountdown(vm);

        Assert.True(pageVm.HasProgress);
        Assert.InRange(pageVm.ProgressFraction, 0.0, 1.0);
    }

    [Fact]
    public void GoLive_NoTimerNoVideo_AfterTick_HasProgressStaysFalse()
    {
        var vm = CreateVm(out var pageVm, durationMs: 0);
        vm.GoLive();

        CallTickCountdown(vm);

        Assert.False(pageVm.HasProgress);
    }

    // ── ClearLive ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClearLive_AfterGoLive_HasProgressIsFalse()
    {
        var vm = CreateVm(out var pageVm, durationMs: 5000);
        vm.GoLive();
        CallTickCountdown(vm);
        Assert.True(pageVm.HasProgress); // sanity

        vm.ClearLive();

        Assert.False(pageVm.HasProgress);
    }
}
