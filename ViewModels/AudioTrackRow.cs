using Avalonia.Media;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

/// <summary>
/// UI wrapper around <see cref="AudioTrack"/> that carries per-row visual state
/// (IsPlaying, IsSelected) so the DataTemplate can bind directly without converters.
/// </summary>
public class AudioTrackRow : ReactiveObject
{
    // ── Static brushes (allocated once) ──────────────────────────────────────
    static readonly SolidColorBrush BrushPlaying  = new(Color.Parse("#3d1a08")); // warm dark-orange
    static readonly SolidColorBrush BrushSelected = new(Color.Parse("#0f2540")); // cool dark-blue
    static readonly SolidColorBrush BrushDefault  = new(Color.Parse("#2a2a2a")); // unchanged default

    // ── Data ──────────────────────────────────────────────────────────────────
    public AudioTrack Track      { get; }
    public string     Title      => Track.Title;
    public long       DurationMs => Track.DurationMs;

    // ── Visual state ──────────────────────────────────────────────────────────
    bool _isPlaying;
    public bool IsPlaying
    {
        get => _isPlaying;
        set
        {
            this.RaiseAndSetIfChanged(ref _isPlaying, value);
            this.RaisePropertyChanged(nameof(RowBackground));
        }
    }

    bool _isSelected;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            this.RaiseAndSetIfChanged(ref _isSelected, value);
            this.RaisePropertyChanged(nameof(RowBackground));
        }
    }

    /// <summary>Background brush for the track row. IsPlaying takes priority over IsSelected.</summary>
    public IBrush RowBackground =>
        IsPlaying  ? BrushPlaying  :
        IsSelected ? BrushSelected :
                     BrushDefault;

    public AudioTrackRow(AudioTrack track) => Track = track;
}
