using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class AudioChannelViewModel : ReactiveObject, IDisposable
{
    public AudioChannel         Model  { get; }
    public AudioPlayerViewModel Player { get; }

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
        if (destination is null) return;
        if (destination.Type == AudioRouteType.Hardware)
            Player.SetAudioDevice("wasapi", destination.DeviceId);
        // NDI: no-op in V1 (requires video integration — next phase)
    }

    public void Dispose() => Player.Dispose();
}
