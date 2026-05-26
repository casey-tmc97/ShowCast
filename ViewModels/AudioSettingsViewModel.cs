using System.Collections.ObjectModel;
using System.Linq;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class AudioSettingsViewModel : ReactiveObject
{
    readonly MainViewModel _main;

    /// <summary>Reflects MainViewModel.AudioChannels — rows in the routing matrix.</summary>
    public ObservableCollection<AudioChannelViewModel> Channels => _main.AudioChannels;

    /// <summary>All known hardware and NDI destinations — columns in the routing matrix.</summary>
    public ObservableCollection<AudioDestination> Destinations { get; } = new();

    public AudioSettingsViewModel(MainViewModel main)
    {
        _main = main;

        // Populate destinations from AppSettings
        foreach (var d in main.ShowFileDestinations)
            Destinations.Add(d);
    }

    /// <summary>
    /// Sets or toggles the route for a channel.
    /// Passing the currently active destination clears the route (OS default).
    /// Passing null clears unconditionally.
    /// </summary>
    public void SetRoute(AudioChannelViewModel channel, AudioDestination? destination)
    {
        if (destination is not null &&
            channel.Model.ActiveDestinationId == destination.Id)
        {
            // Toggle off: clear the route
            channel.Model.ActiveDestinationId = null;
            channel.ApplyRoute(null);
        }
        else
        {
            channel.Model.ActiveDestinationId = destination?.Id;
            channel.ApplyRoute(destination);
        }
    }

    /// <summary>
    /// Re-enumerates hardware devices and refreshes the Destinations list.
    /// Requires a LibVLC instance from the first available channel's player.
    /// </summary>
    public void RefreshDevices()
    {
        // Enumerate hardware via first available LibVLC instance
        var libVlc = _main.AudioChannels
            .Select(c => c.Player.LibVlc)
            .FirstOrDefault(v => v is not null);

        if (libVlc is not null)
        {
            var fresh  = AudioDeviceEnumerator.EnumerateHardware(libVlc);
            var stored = _main.ShowFileDestinations;
            AudioDeviceEnumerator.MergeHardware(stored, fresh);

            // Sync to observable collection
            Destinations.Clear();
            foreach (var d in stored)
                Destinations.Add(d);
        }

        // Enumerate NDI
        var ndiDestinations = AudioDeviceEnumerator.EnumerateNdi(_main.ShowFile);
        foreach (var nd in ndiDestinations)
        {
            if (!Destinations.Any(d => d.Type == AudioRouteType.Ndi && d.DeviceId == nd.DeviceId))
                Destinations.Add(nd);
        }
    }
}
