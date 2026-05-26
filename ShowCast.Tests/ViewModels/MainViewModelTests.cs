using System.Reflection;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelTests
{
    static void InvokeFirePageAudioTrigger(MainViewModel vm, Page page)
    {
        var method = typeof(MainViewModel).GetMethod(
            "FirePageAudioTrigger",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("FirePageAudioTrigger not found");
        method.Invoke(vm, new object[] { page });
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistIdEmpty_DoesNotChangeSelectedPlaylist()
    {
        var vm      = new MainViewModel();
        var initial = vm.AudioPlayer.SelectedPlaylist;
        var page    = new Page { TriggerAudioPlaylistId = Guid.Empty };

        InvokeFirePageAudioTrigger(vm, page);

        Assert.Same(initial, vm.AudioPlayer.SelectedPlaylist);
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistIdMatchesPlaylist_SetsSelectedPlaylist()
    {
        var vm       = new MainViewModel();
        var playlist = new AudioPlaylist { Name = "Service Music" };
        vm.AudioPlayer.Playlists.Add(playlist);
        var page = new Page { TriggerAudioPlaylistId = playlist.Id };

        InvokeFirePageAudioTrigger(vm, page);

        Assert.Same(playlist, vm.AudioPlayer.SelectedPlaylist);
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistNotFound_DoesNotChangeSelectedPlaylist()
    {
        var vm      = new MainViewModel();
        var initial = vm.AudioPlayer.SelectedPlaylist;
        var page    = new Page { TriggerAudioPlaylistId = Guid.NewGuid() };

        InvokeFirePageAudioTrigger(vm, page);

        Assert.Same(initial, vm.AudioPlayer.SelectedPlaylist);
    }
}
