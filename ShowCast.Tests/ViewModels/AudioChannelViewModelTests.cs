using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class AudioChannelViewModelTests
{
    [Fact]
    public void AudioChannelViewModel_Player_IsNotNull()
    {
        var model = new AudioChannel { Name = "Main PA" };
        var vm = new AudioChannelViewModel(model);
        Assert.NotNull(vm.Player);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_Name_ReflectsModel()
    {
        var model = new AudioChannel { Name = "Lobby" };
        var vm = new AudioChannelViewModel(model);
        Assert.Equal("Lobby", vm.Name);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_SetName_UpdatesModel()
    {
        var model = new AudioChannel { Name = "Old" };
        var vm = new AudioChannelViewModel(model);
        vm.Name = "New";
        Assert.Equal("New", model.Name);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_Dispose_DoesNotThrow()
    {
        var model = new AudioChannel();
        var vm = new AudioChannelViewModel(model);
        var ex = Record.Exception((Action)(() => vm.Dispose()));
        Assert.Null(ex);
    }

    [Fact]
    public void AudioChannelViewModel_ApplyRoute_Null_DoesNotThrow()
    {
        var model = new AudioChannel();
        var vm = new AudioChannelViewModel(model);
        var ex = Record.Exception((Action)(() => vm.ApplyRoute(null)));
        Assert.Null(ex);
        vm.Dispose();
    }

    [Fact]
    public void AudioChannelViewModel_ApplyRoute_Hardware_DoesNotThrow()
    {
        var model = new AudioChannel();
        var vm = new AudioChannelViewModel(model);
        var dest = new AudioDestination
        {
            Type     = AudioRouteType.Hardware,
            DeviceId = "hw-001"
        };
        var ex = Record.Exception((Action)(() => vm.ApplyRoute(dest)));
        Assert.Null(ex);
        vm.Dispose();
    }
}
