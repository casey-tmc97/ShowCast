using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class AudioSettingsViewModelTests
{
    static MainViewModel MakeMainVm() => new MainViewModel();

    [Fact]
    public void AudioSettingsViewModel_Constructs_WithChannelsFromMain()
    {
        var mainVm     = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        Assert.NotEmpty(settingsVm.Channels);
    }

    [Fact]
    public void SetRoute_AssignsActiveDestinationId_ToChannelModel()
    {
        var mainVm     = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        var dest = new AudioDestination
        {
            Id       = Guid.NewGuid(),
            DeviceId = "hw-001",
            Type     = AudioRouteType.Hardware
        };
        settingsVm.Destinations.Add(dest);

        var channel = settingsVm.Channels[0];
        settingsVm.SetRoute(channel, dest);

        Assert.Equal(dest.Id, channel.Model.ActiveDestinationId);
    }

    [Fact]
    public void SetRoute_Null_ClearsActiveDestinationId()
    {
        var mainVm     = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        var channel    = settingsVm.Channels[0];
        channel.Model.ActiveDestinationId = Guid.NewGuid();

        settingsVm.SetRoute(channel, null);

        Assert.Null(channel.Model.ActiveDestinationId);
    }

    [Fact]
    public void SetRoute_SameDestinationTwice_TogglesOff()
    {
        var mainVm     = MakeMainVm();
        var settingsVm = new AudioSettingsViewModel(mainVm);
        var dest = new AudioDestination { Id = Guid.NewGuid(), DeviceId = "hw-001" };
        settingsVm.Destinations.Add(dest);

        var channel = settingsVm.Channels[0];
        settingsVm.SetRoute(channel, dest);
        settingsVm.SetRoute(channel, dest); // toggle off

        Assert.Null(channel.Model.ActiveDestinationId);
    }
}
