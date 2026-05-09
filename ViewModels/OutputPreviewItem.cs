using Avalonia.Media.Imaging;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class OutputPreviewItem : ReactiveObject
{
    public OutputState Output { get; }

    public string Label => Output.Config.Name;

    public string Name => Output.Config.Type switch
    {
        OutputType.Display    => $"Display {Output.Config.DisplayIndex}",
        OutputType.NDI        => string.IsNullOrEmpty(Output.Config.NdiStreamName)
                                     ? "NDI" : Output.Config.NdiStreamName,
        OutputType.AJA        => string.IsNullOrEmpty(Output.Config.DeviceSerial)
                                     ? "AJA" : Output.Config.DeviceSerial,
        OutputType.Blackmagic => string.IsNullOrEmpty(Output.Config.DeviceSerial)
                                     ? "Blackmagic" : Output.Config.DeviceSerial,
        OutputType.BirdDog    => string.IsNullOrEmpty(Output.Config.DeviceSerial)
                                     ? "BirdDog" : Output.Config.DeviceSerial,
        OutputType.Preview    => "Preview",
        _                     => Output.Config.Name
    };

    private Bitmap? _bitmap;
    public Bitmap? Bitmap
    {
        get => _bitmap;
        set => this.RaiseAndSetIfChanged(ref _bitmap, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    public OutputPreviewItem(OutputState output) => Output = output;

    public void RaiseConfigChanged()
    {
        this.RaisePropertyChanged(nameof(Name));
        this.RaisePropertyChanged(nameof(Label));
    }
}
