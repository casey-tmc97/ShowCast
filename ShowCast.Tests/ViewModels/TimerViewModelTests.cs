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
    public void ClockTimer_ClockSecondsRemaining_ReturnsNonPositive_WhenTargetIsPast()
    {
        // A time that was 5 minutes ago
        var past = DateTime.Now.AddMinutes(-5);
        var def  = new TimerDef
        {
            Type      = TimerType.Clock,
            ClockTime = $"{past.Hour:00}:{past.Minute:00}"
        };
        var vm = new TimerViewModel(def);

        // Before fix: ClockSecondsRemaining adds a day when target <= now,
        // so the value is large (~86400). After fix it should be <= 0.
        int remaining = InvokeClockSecondsRemaining(vm);
        Assert.True(remaining <= 0, $"Expected ≤ 0 but got {remaining}");

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
