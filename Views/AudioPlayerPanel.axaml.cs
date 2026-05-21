using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ShowCast.Core;
using ShowCast.ViewModels;
using AudioTrack = ShowCast.Core.AudioTrack;

namespace ShowCast.Views;

public partial class AudioPlayerPanel : UserControl
{
    AudioPlayerViewModel? VM => DataContext as AudioPlayerViewModel;

    public AudioPlayerPanel() => InitializeComponent();

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

    void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Border)?.DataContext is not AudioTrackRow row) return;

        var props = e.GetCurrentPoint(null).Properties;

        // Right-click only opens the context menu — don't change selection
        if (props.IsRightButtonPressed) return;

        VM?.SelectTrack(row);

        // Double-click starts playback of the clicked track
        if (e.ClickCount == 2)
        {
            VM?.Play(row.Track);
            e.Handled = true;
        }
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
        await VM.ImportFilesAsync(paths, async fileName =>
        {
            var dlg = new FileConflictDialog(fileName);
            return await dlg.ShowAsync(owner);
        });
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

    // ── Clip Properties context menu ──────────────────────────────────────────

    void OnSpd05(object? s, RoutedEventArgs e)  => VM?.SetSpeed(0.5f);
    void OnSpd075(object? s, RoutedEventArgs e) => VM?.SetSpeed(0.75f);
    void OnSpd1(object? s, RoutedEventArgs e)   => VM?.SetSpeed(1.0f);
    void OnSpd125(object? s, RoutedEventArgs e) => VM?.SetSpeed(1.25f);
    void OnSpd15(object? s, RoutedEventArgs e)  => VM?.SetSpeed(1.5f);
    void OnSpd2(object? s, RoutedEventArgs e)   => VM?.SetSpeed(2.0f);

    // ── Playlist Properties context menu ─────────────────────────────────────

    void OnResumeTop(object? sender, RoutedEventArgs e)  => VM?.SetResumeMode(ResumeMode.FromTop);
    void OnResumeLast(object? sender, RoutedEventArgs e) => VM?.SetResumeMode(ResumeMode.FromLastPosition);
}
