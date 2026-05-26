using System;
using System.Reflection;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelTests
{
    static void InvokeFirePageAudioTrigger(MainViewModel vm, Page page)
    {
        var method = typeof(MainViewModel).GetMethod("FirePageAudioTrigger",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("FirePageAudioTrigger not found");
        method.Invoke(vm, new object[] { page });
    }

    // ── Channel seeding ───────────────────────────────────────────────────────

    [Fact]
    public void MainViewModel_Init_SeedsOneDefaultChannel()
    {
        var vm = new MainViewModel();
        Assert.NotEmpty(vm.AudioChannels);
        Assert.NotNull(vm.SelectedAudioChannel);
    }

    [Fact]
    public void MainViewModel_Init_DefaultChannelHasAtLeastOnePlaylist()
    {
        var vm = new MainViewModel();
        Assert.NotEmpty(vm.SelectedAudioChannel!.Player.Playlists);
    }

    // ── AddAudioChannel / RemoveAudioChannel ──────────────────────────────────

    [Fact]
    public void AddAudioChannel_AddsChannelAndSelectsIt()
    {
        var vm = new MainViewModel();
        var before = vm.AudioChannels.Count;
        vm.AddAudioChannel("Stage Monitors");
        Assert.Equal(before + 1, vm.AudioChannels.Count);
        Assert.Equal("Stage Monitors", vm.SelectedAudioChannel!.Name);
    }

    [Fact]
    public void RemoveAudioChannel_RemovesAndSelectsFirst()
    {
        var vm = new MainViewModel();
        vm.AddAudioChannel("Stage Monitors");
        var toRemove = vm.AudioChannels[1];
        vm.RemoveAudioChannel(toRemove);
        Assert.DoesNotContain(toRemove, vm.AudioChannels);
        Assert.Equal(vm.AudioChannels[0], vm.SelectedAudioChannel);
    }

    [Fact]
    public void RemoveAudioChannel_CannotRemoveLastChannel()
    {
        var vm = new MainViewModel();
        Assert.Single(vm.AudioChannels);
        vm.RemoveAudioChannel(vm.AudioChannels[0]);
        Assert.Single(vm.AudioChannels); // still there
    }

    // ── FirePageAudioTrigger (multi-channel) ──────────────────────────────────

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistIdEmpty_DoesNotChangeSelectedPlaylist()
    {
        var vm             = new MainViewModel();
        var player         = vm.SelectedAudioChannel!.Player;
        var originalPlaylist = player.SelectedPlaylist;
        InvokeFirePageAudioTrigger(vm, new Page { TriggerAudioPlaylistId = Guid.Empty });
        Assert.Equal(originalPlaylist, player.SelectedPlaylist);
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistIdMatchesDefaultChannel_SetsSelectedPlaylist()
    {
        var vm     = new MainViewModel();
        var player = vm.SelectedAudioChannel!.Player;
        player.CreatePlaylist("Worship");
        var target = player.Playlists[^1];

        InvokeFirePageAudioTrigger(vm, new Page { TriggerAudioPlaylistId = target.Id });

        Assert.Equal(target, player.SelectedPlaylist);
    }

    [Fact]
    public void FirePageAudioTrigger_WhenPlaylistNotFound_DoesNotChangeSelectedPlaylist()
    {
        var vm             = new MainViewModel();
        var player         = vm.SelectedAudioChannel!.Player;
        var originalPlaylist = player.SelectedPlaylist;

        InvokeFirePageAudioTrigger(vm, new Page { TriggerAudioPlaylistId = Guid.NewGuid() });

        Assert.Equal(originalPlaylist, player.SelectedPlaylist);
    }
}
