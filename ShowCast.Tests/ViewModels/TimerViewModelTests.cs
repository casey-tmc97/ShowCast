// ShowCast.Tests/ViewModels/TimerViewModelTests.cs
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class TimerViewModelTests
{
    [Fact]
    public void ClockTimer_DoesNotAutoStart_OnCreation()
    {
        var def = new TimerDef { Type = TimerType.Clock, ClockTime = "23:59" };
        var vm  = new TimerViewModel(def);

        Assert.False(vm.IsRunning);

        vm.Dispose();
    }

    [Fact]
    public void CounterTimer_DoesNotAutoStart_OnCreation()
    {
        var def = new TimerDef { Type = TimerType.Counter, StartSeconds = 300 };
        var vm  = new TimerViewModel(def);

        Assert.False(vm.IsRunning);

        vm.Dispose();
    }

    [Fact]
    public void ClockTimer_ClockSecondsRemaining_RollsToNextDay_WhenTargetIsPast()
    {
        // A time that was 5 minutes ago — should roll to next day's occurrence.
        var past = DateTime.Now.AddMinutes(-5);
        var def  = new TimerDef
        {
            Type      = TimerType.Clock,
            ClockTime = $"{past.Hour:00}:{past.Minute:00}"
        };
        var vm = new TimerViewModel(def);

        int remaining = InvokeClockSecondsRemaining(vm);
        // Next occurrence is tomorrow — roughly 24h minus 5 minutes away.
        Assert.True(remaining > 0, $"Expected > 0 (next occurrence) but got {remaining}");
        Assert.True(remaining <= 86400, $"Expected <= 86400 but got {remaining}");

        vm.Dispose();
    }

    [Fact]
    public void CounterTimer_DisplayText_NeverShowsNegative_WhenOverflowEnabled()
    {
        var def = new TimerDef
        {
            Type            = TimerType.Counter,
            StartSeconds    = 5,
            EndSeconds      = 0,
            OverflowEnabled = true
        };
        var vm = new TimerViewModel(def);

        // Force backing field below zero via reflection (bypasses dispatcher requirement)
        var field = typeof(TimerViewModel).GetField("_currentSeconds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(vm, -30);

        Assert.DoesNotContain("−", vm.DisplayText);
        Assert.Equal("0:30", vm.DisplayText);  // absolute elapsed-past-end, no sign

        vm.Dispose();
    }

    [Fact]
    public void ClockTimer_DisplayText_ShowsNextOccurrence_WhenTargetIsPast()
    {
        // Timer set 5 minutes in the past — should display time to next-day occurrence.
        var past = DateTime.Now.AddMinutes(-5);
        var def  = new TimerDef
        {
            Type      = TimerType.Clock,
            ClockTime = $"{past.Hour:00}:{past.Minute:00}"
        };
        var vm = new TimerViewModel(def);

        Assert.DoesNotContain("−", vm.DisplayText);
        Assert.NotEqual("0:00", vm.DisplayText);

        vm.Dispose();
    }

    [Fact]
    public void CounterTimer_Play_ResetsToStart_WhenAtEnd()
    {
        var def = new TimerDef { Type = TimerType.Counter, StartSeconds = 300, EndSeconds = 0, OverflowEnabled = false };
        var vm  = new TimerViewModel(def);

        // Simulate timer having reached its end
        var field = typeof(TimerViewModel).GetField("_currentSeconds",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        field.SetValue(vm, 0);

        vm.Play();

        Assert.Equal("5:00", vm.DisplayText);
        Assert.True(vm.IsRunning);

        vm.Pause();
        vm.Dispose();
    }

    // Reflection helper — accesses the private method for direct testing.
    static int InvokeClockSecondsRemaining(TimerViewModel vm)
    {
        var m = typeof(TimerViewModel).GetMethod("ClockSecondsRemaining",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
        return (int)m.Invoke(vm, null)!;
    }
}
