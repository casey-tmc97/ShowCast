using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class AudioChannelViewModel : ReactiveObject, IDisposable
{
    public AudioChannel         Model  { get; }
    public AudioPlayerViewModel Player { get; }

    /// <summary>Injected by MainViewModel. Returns the NdiSender for a given stream name, or null.</summary>
    public Func<string, NdiSender?>? NdiSenderLookup { get; set; }

    public string Name
    {
        get => Model.Name;
        set { Model.Name = value; this.RaisePropertyChanged(); }
    }

    public AudioChannelViewModel(AudioChannel model)
    {
        Model  = model;
        Player = new AudioPlayerViewModel();
    }

    /// <summary>
    /// Configures the Player to output to the assigned destination on next Play() call.
    /// </summary>
    public void ApplyRoute(AudioDestination? destination)
    {
        if (destination is null)
        {
            Player.SetNdiOutput(null);
            return;
        }

        if (destination.Type == AudioRouteType.Hardware)
        {
            Player.SetNdiOutput(null);
            Player.SetAudioDevice("mmdevice", destination.DeviceId);
        }
        else if (destination.Type == AudioRouteType.Ndi)
        {
            var sender = NdiSenderLookup?.Invoke(destination.DeviceId);
            Player.SetNdiOutput(sender);
        }
    }

    public void Dispose() => Player.Dispose();
}
