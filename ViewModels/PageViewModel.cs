using Avalonia.Media.Imaging;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.ViewModels;

public class PageViewModel : ViewModelBase
{
    private const int ThumbW = 320;
    private const int ThumbH = 180;

    public Page Model { get; }
    public Package? Owner { get; }

    public PageViewModel(Page page, Package? owner = null)
    {
        Model = page;
        Owner = owner;
        RebuildThumbnail();
    }

    public string Name => Model.Name;

    public int DurationMs
    {
        get => Model.DurationMs;
        set
        {
            Model.DurationMs = value;
            this.RaisePropertyChanged(nameof(DurationMs));
            this.RaisePropertyChanged(nameof(TimerLabel));
            this.RaisePropertyChanged(nameof(HasTimer));
        }
    }

    public bool LoopToStart
    {
        get => Model.LoopToStart;
        set
        {
            Model.LoopToStart = value;
            this.RaisePropertyChanged(nameof(LoopToStart));
        }
    }

    /// <summary>Human-readable label shown on the page card, e.g. "5s". Null when no timer.</summary>
    public string? TimerLabel => Model.DurationMs > 0
        ? (Model.DurationMs % 1000 == 0
            ? $"{Model.DurationMs / 1000}s"
            : $"{Model.DurationMs / 1000.0:F1}s")
        : null;

    public bool HasTimer => Model.DurationMs > 0;

    private bool _isLive;
    public bool IsLive
    {
        get => _isLive;
        set => this.RaiseAndSetIfChanged(ref _isLive, value);
    }

    private bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set => this.RaiseAndSetIfChanged(ref _isSelected, value);
    }

    private bool _showDropIndicator;
    public bool ShowDropIndicator
    {
        get => _showDropIndicator;
        set => this.RaiseAndSetIfChanged(ref _showDropIndicator, value);
    }

    private bool _showInsertBefore;
    public bool ShowInsertBefore
    {
        get => _showInsertBefore;
        set => this.RaiseAndSetIfChanged(ref _showInsertBefore, value);
    }

    private bool _showInsertAfter;
    public bool ShowInsertAfter
    {
        get => _showInsertAfter;
        set => this.RaiseAndSetIfChanged(ref _showInsertAfter, value);
    }

    private Bitmap? _thumbnail;
    public Bitmap? Thumbnail
    {
        get => _thumbnail;
        private set => this.RaiseAndSetIfChanged(ref _thumbnail, value);
    }

    public void RebuildThumbnail()
    {
        try
        {
            using var surface = SKSurface.Create(
                new SKImageInfo(ThumbW, ThumbH, SKColorType.Rgba8888));

            PageRenderer.Render(surface.Canvas, Model, LayerRole.All, ThumbW, ThumbH);

            using var image = surface.Snapshot();
            using var data  = image.Encode(SKEncodedImageFormat.Png, 80);
            Thumbnail = new Bitmap(data.AsStream());
        }
        catch
        {
            Thumbnail = null;
        }
    }

    public void RefreshName() => this.RaisePropertyChanged(nameof(Name));

    public void Refresh()
    {
        this.RaisePropertyChanged(nameof(Name));
        RebuildThumbnail();
    }
}
