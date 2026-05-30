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
}
