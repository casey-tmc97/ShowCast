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
    MainViewModel?        VM     => DataContext as MainViewModel;
    AudioPlayerViewModel? Player => VM?.SelectedAudioChannel?.Player;

    public AudioPlayerPanel() => InitializeComponent();

    // ── Channel tab strip ─────────────────────────────────────────────────────

    async void OnAddChannel(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("New Audio Channel", "Channel name:", "New Channel");
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            VM.AddAudioChannel(result.Trim());
    }

    async void OnRenameChannel(object? sender, RoutedEventArgs e)
    {
        if (ChannelTabList.SelectedItem is not AudioChannelViewModel ch) return;
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("Rename Channel", "Channel name:", ch.Name);
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            ch.Name = result.Trim();
    }

    void OnRemoveChannel(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        if (ChannelTabList.SelectedItem is not AudioChannelViewModel ch) return;
        VM.RemoveAudioChannel(ch);
    }

    // ── Playlist management ───────────────────────────────────────────────────

    async void OnNewPlaylist(object? sender, RoutedEventArgs e)
    {
        if (TopLevel.GetTopLevel(this) is not Window owner) return;
        var dlg = new TextInputDialog("New Playlist", "Playlist name:", "New Playlist");
        var result = await dlg.ShowAsync(owner);
        if (!string.IsNullOrWhiteSpace(result))
            Player?.CreatePlaylist(result);
    }

    void OnDeletePlaylist(object? sender, RoutedEventArgs e)
    {
        if (Player?.SelectedPlaylist is { } pl)
            Player.DeletePlaylist(pl);
    }

    // ── Track management ──────────────────────────────────────────────────────

    void OnDeleteTrack(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is AudioTrack track)
            Player?.DeleteTrack(track);
    }

    void OnTrackPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if ((sender as Border)?.DataContext is not AudioTrackRow row) return;

        var props = e.GetCurrentPoint(null).Properties;
        if (props.IsRightButtonPressed) return;

        Player?.SelectTrack(row);

        if (e.ClickCount == 2)
        {
            Player?.Play(row.Track);
            e.Handled = true;
        }
    }

    async void OnImport(object? sender, RoutedEventArgs e)
    {
        if (Player is null) return;
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
        await Player.ImportFilesAsync(paths, async fileName =>
        {
            var dlg = new FileConflictDialog(fileName);
            return await dlg.ShowAsync(owner);
        });
    }

    // ── Playback ──────────────────────────────────────────────────────────────

    void OnPlayPause(object? sender, RoutedEventArgs e)
    {
        if (Player is null) return;
        if (Player.State == PlaybackState.Playing) Player.Pause();
        else Player.Play();
    }

    void OnPrevious(object? sender, RoutedEventArgs e)    => Player?.Previous();
    void OnNext(object? sender, RoutedEventArgs e)        => Player?.Next();
    void OnSeekBack(object? sender, RoutedEventArgs e)    => Player?.SeekRelative(-10);
    void OnSeekForward(object? sender, RoutedEventArgs e) => Player?.SeekRelative(10);

    void OnSeekReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (sender is Slider s) Player?.Seek(s.Value);
    }

    // ── Clip Properties context menu ──────────────────────────────────────────

    void OnSpd05(object? s, RoutedEventArgs e)  => Player?.SetSpeed(0.5f);
    void OnSpd075(object? s, RoutedEventArgs e) => Player?.SetSpeed(0.75f);
    void OnSpd1(object? s, RoutedEventArgs e)   => Player?.SetSpeed(1.0f);
    void OnSpd125(object? s, RoutedEventArgs e) => Player?.SetSpeed(1.25f);
    void OnSpd15(object? s, RoutedEventArgs e)  => Player?.SetSpeed(1.5f);
    void OnSpd2(object? s, RoutedEventArgs e)   => Player?.SetSpeed(2.0f);

    // ── Playlist Properties context menu ─────────────────────────────────────

    void OnResumeTop(object? sender, RoutedEventArgs e)  => Player?.SetResumeMode(ResumeMode.FromTop);
    void OnResumeLast(object? sender, RoutedEventArgs e) => Player?.SetResumeMode(ResumeMode.FromLastPosition);
}
