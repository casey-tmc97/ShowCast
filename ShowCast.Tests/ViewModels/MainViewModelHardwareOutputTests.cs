using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelHardwareOutputTests
{
    [Fact]
    public void NotifyOutputConfigsChanged_BlackmagicOutputEnabled_DoesNotThrow()
    {
        var vm  = new MainViewModel();
        var cfg = new OutputConfig { Name = "DL Out", Type = OutputType.Blackmagic, Enabled = true };
        vm.ShowFile.AddOutput(cfg);
        vm.OutputStates.Add(new OutputState(cfg));

        var ex = Record.Exception(() => vm.NotifyOutputConfigsChanged());
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyOutputConfigsChanged_AjaOutputEnabled_DoesNotThrow()
    {
        var vm  = new MainViewModel();
        var cfg = new OutputConfig { Name = "AJA Out", Type = OutputType.AJA, Enabled = true };
        vm.ShowFile.AddOutput(cfg);
        vm.OutputStates.Add(new OutputState(cfg));

        var ex = Record.Exception(() => vm.NotifyOutputConfigsChanged());
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyOutputConfigsChanged_BlackmagicOutputDisabled_DoesNotThrow()
    {
        var vm  = new MainViewModel();
        var cfg = new OutputConfig { Name = "DL Out", Type = OutputType.Blackmagic, Enabled = false };
        vm.ShowFile.AddOutput(cfg);
        vm.OutputStates.Add(new OutputState(cfg));

        var ex = Record.Exception(() => vm.NotifyOutputConfigsChanged());
        Assert.Null(ex);
    }
}
