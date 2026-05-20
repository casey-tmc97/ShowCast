using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class AudioPlayerViewModelTests
{
    // ── Playlist CRUD ─────────────────────────────────────────────────────────

    [Fact]
    public void CreatePlaylist_AddsToPlaylists_AndSelectsIt()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("Sunday Service");

        Assert.Single(vm.Playlists);
        Assert.Equal("Sunday Service", vm.Playlists[0].Name);
        Assert.Same(vm.Playlists[0], vm.SelectedPlaylist);
        vm.Dispose();
    }

    [Fact]
    public void DeletePlaylist_RemovesFromPlaylists()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("A");
        vm.CreatePlaylist("B");
        var first = vm.Playlists[0];

        vm.DeletePlaylist(first);

        Assert.Single(vm.Playlists);
        Assert.Equal("B", vm.Playlists[0].Name);
        vm.Dispose();
    }

    [Fact]
    public void DeletePlaylist_WhenOnlyOne_SetsSelectedPlaylistNull()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("Only");
        vm.DeletePlaylist(vm.Playlists[0]);

        Assert.Empty(vm.Playlists);
        Assert.Null(vm.SelectedPlaylist);
        vm.Dispose();
    }

    [Fact]
    public void RenamePlaylist_UpdatesName()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("Old");
        vm.RenamePlaylist(vm.Playlists[0], "New");

        Assert.Equal("New", vm.Playlists[0].Name);
        vm.Dispose();
    }

    [Fact]
    public void DeleteTrack_RemovesFromTrackListAndPlaylist()
    {
        var vm = new AudioPlayerViewModel();
        vm.CreatePlaylist("P");
        var track = new AudioTrack { Title = "T1", RelativePath = "t1.mp3" };
        vm.SelectedPlaylist!.Tracks.Add(track);
        vm.TrackList.Add(track);

        vm.DeleteTrack(track);

        Assert.Empty(vm.TrackList);
        Assert.Empty(vm.SelectedPlaylist.Tracks);
        vm.Dispose();
    }

    [Fact]
    public void LoadPlaylists_PopulatesPlaylists()
    {
        var vm = new AudioPlayerViewModel();
        var list = new List<AudioPlaylist>
        {
            new() { Name = "Alpha" },
            new() { Name = "Beta"  }
        };

        vm.LoadPlaylists(list, selectedId: Guid.Empty);

        Assert.Equal(2, vm.Playlists.Count);
        Assert.Equal("Alpha", vm.Playlists[0].Name);
        vm.Dispose();
    }

    [Fact]
    public void LoadPlaylists_RestoresSelectedPlaylistById()
    {
        var vm   = new AudioPlayerViewModel();
        var beta = new AudioPlaylist { Name = "Beta" };
        var list = new List<AudioPlaylist> { new() { Name = "Alpha" }, beta };

        vm.LoadPlaylists(list, selectedId: beta.Id);

        Assert.Same(beta, vm.SelectedPlaylist);
        vm.Dispose();
    }

    // ── Next-index logic ──────────────────────────────────────────────────────

    [Fact]
    public void PickNextIndex_MiddleOfPlaylist_ReturnsNextIndex()
    {
        var pl = new AudioPlaylist();
        pl.Tracks.AddRange(new[] {
            new AudioTrack(), new AudioTrack(), new AudioTrack()
        });
        Assert.Equal(2, AudioPlayerViewModel.PickNextIndex(pl, 1));
    }

    [Fact]
    public void PickNextIndex_EndNoRepeat_ReturnsMinusOne()
    {
        var pl = new AudioPlaylist { Repeat = RepeatMode.None };
        pl.Tracks.Add(new AudioTrack());
        Assert.Equal(-1, AudioPlayerViewModel.PickNextIndex(pl, 0));
    }

    [Fact]
    public void PickNextIndex_EndRepeatAll_ReturnsZero()
    {
        var pl = new AudioPlaylist { Repeat = RepeatMode.All };
        pl.Tracks.AddRange(new[] { new AudioTrack(), new AudioTrack() });
        Assert.Equal(0, AudioPlayerViewModel.PickNextIndex(pl, 1));
    }

    [Fact]
    public void PickNextIndex_EndRepeatOne_ReturnsSameIndex()
    {
        var pl = new AudioPlaylist { Repeat = RepeatMode.One };
        pl.Tracks.AddRange(new[] { new AudioTrack(), new AudioTrack() });
        Assert.Equal(1, AudioPlayerViewModel.PickNextIndex(pl, 1));
    }

    [Fact]
    public void PickNextIndex_EmptyPlaylist_ReturnsMinusOne()
    {
        var pl = new AudioPlaylist();
        Assert.Equal(-1, AudioPlayerViewModel.PickNextIndex(pl, 0));
    }

    [Fact]
    public void PickNextIndex_Shuffle_ReturnsValidIndexDifferentFromCurrent()
    {
        var pl = new AudioPlaylist { Shuffle = true };
        for (int i = 0; i < 5; i++) pl.Tracks.Add(new AudioTrack());

        // Run many times to confirm it never returns the current index
        for (int i = 0; i < 50; i++)
        {
            int next = AudioPlayerViewModel.PickNextIndex(pl, 2);
            Assert.InRange(next, 0, pl.Tracks.Count - 1);
            Assert.NotEqual(2, next);
        }
    }
}
