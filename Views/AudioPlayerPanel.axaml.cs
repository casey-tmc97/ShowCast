using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class AudioPlayerPanel : UserControl
{
    AudioPlayerViewModel? VM => DataContext as AudioPlayerViewModel;

    public AudioPlayerPanel() => InitializeComponent();

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        RefreshSpeedButtons();
    }

    // ── Playlist management ───────────────────────────────────────────────────

    async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("New Playlist", "Playlist name:", "New Playlist");
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            VM?.CreatePlaylist(result);
    }

    void OnDeletePlaylist(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedPlaylist is { } pl)
            VM.DeletePlaylist(pl);
    }

    // ── Track management ──────────────────────────────────────────────────────

    void OnDeleteTrack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AudioTrack track)
            VM?.DeleteTrack(track);
    }

    async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;

        var picker = new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title         = "Import Audio Files",
            AllowMultiple = true,
            FileTypeFilter = new[]
            {
                new Avalonia.Platform.Storage.FilePickerFileType("Audio Files")
                {
                    Patterns = new[] {
                        "*.mp3","*.wav","*.flac","*.aac","*.m4a","*.ogg","*.opus",
                        "*.wma","*.aiff","*.aif","*.mp4","*.m4b","*.mka","*.ape",
                        "*.wv","*.tta","*.caf","*.au","*.amr","*.spx"
                    }
                },
                new Avalonia.Platform.Storage.FilePickerFileType("All Files")
                {
                    Patterns = new[] { "*.*" }
                }
            }
        };

        var files = await owner.StorageProvider.OpenFilePickerAsync(picker);
        if (files.Count == 0) return;

        var paths = files.Select(f => f.Path.LocalPath).Where(p => !string.IsNullOrEmpty(p));
        await VM.ImportFilesAsync(paths);
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (VM.State == PlaybackState.Playing) VM.Pause();
        else VM.Play();
    }

    void OnPrevious(object? sender, RoutedEventArgs e)    => VM?.Previous();
    void OnNext(object? sender, RoutedEventArgs e)        => VM?.Next();
    void OnSeekBack(object? sender, RoutedEventArgs e)    => VM?.SeekRelative(-10);
    void OnSeekForward(object? sender, RoutedEventArgs e) => VM?.SeekRelative(10);

    void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider s) VM?.Seek(s.Value);
    }

    // ── Options ───────────────────────────────────────────────────────────────

    void OnCycleRepeat(object? sender, RoutedEventArgs e) => VM?.CycleRepeat();

    void OnSpd05(object? s, RoutedEventArgs e)  => SetSpeed(0.5f);
    void OnSpd075(object? s, RoutedEventArgs e) => SetSpeed(0.75f);
    void OnSpd1(object? s, RoutedEventArgs e)   => SetSpeed(1.0f);
    void OnSpd125(object? s, RoutedEventArgs e) => SetSpeed(1.25f);
    void OnSpd15(object? s, RoutedEventArgs e)  => SetSpeed(1.5f);
    void OnSpd2(object? s, RoutedEventArgs e)   => SetSpeed(2.0f);

    void SetSpeed(float speed)
    {
        VM?.SetSpeed(speed);
        RefreshSpeedButtons();
    }

    void RefreshSpeedButtons()
    {
        float cur    = VM?.Speed ?? 1.0f;
        var active   = new SolidColorBrush(Color.Parse("#3b82f6"));
        var inactive = new SolidColorBrush(Color.Parse("#3a3a3a"));
        if (Spd05  is not null) Spd05.Background  = cur == 0.5f  ? active : inactive;
        if (Spd075 is not null) Spd075.Background = cur == 0.75f ? active : inactive;
        if (Spd1   is not null) Spd1.Background   = cur == 1.0f  ? active : inactive;
        if (Spd125 is not null) Spd125.Background = cur == 1.25f ? active : inactive;
        if (Spd15  is not null) Spd15.Background  = cur == 1.5f  ? active : inactive;
        if (Spd2   is not null) Spd2.Background   = cur == 2.0f  ? active : inactive;
    }

    void OnResumeTop(object? sender, RoutedEventArgs e)  => VM?.SetResumeMode(ResumeMode.FromTop);
    void OnResumeLast(object? sender, RoutedEventArgs e) => VM?.SetResumeMode(ResumeMode.FromLastPosition);
}
