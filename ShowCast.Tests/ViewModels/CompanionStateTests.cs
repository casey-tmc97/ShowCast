using System.Text.Json;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class CompanionStateTests
{
    [Fact]
    public void BuildCompanionState_AudioSection_IncludesPlaylistsArray()
    {
        var vm = new MainViewModel();
        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var audio = doc.RootElement.GetProperty("audio");
        var playlists = audio.GetProperty("playlists");
        Assert.Equal(JsonValueKind.Array, playlists.ValueKind);
    }

    [Fact]
    public void BuildCompanionState_AudioSection_PlaylistsHaveIdAndName()
    {
        var vm = new MainViewModel();
        // Clear existing playlists and add a known test playlist
        vm.AudioChannels[0].Player.Playlists.Clear();
        vm.AudioChannels[0].Player.CreatePlaylist("Test Playlist");

        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var playlists = doc.RootElement.GetProperty("audio").GetProperty("playlists");

        // Verify we have at least one playlist
        Assert.Equal(1, playlists.GetArrayLength());

        var first = playlists[0];
        Assert.Equal(JsonValueKind.String, first.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, first.GetProperty("name").ValueKind);
        Assert.Equal("Test Playlist", first.GetProperty("name").GetString());
    }

    [Fact]
    public void BuildCompanionState_AudioSection_EmptyPlaylistsWhenNone()
    {
        var vm = new MainViewModel();
        // Clear all playlists from all audio channels
        foreach (var channel in vm.AudioChannels)
        {
            channel.Player.Playlists.Clear();
        }

        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var playlists = doc.RootElement.GetProperty("audio").GetProperty("playlists");

        // Verify the playlists array is empty
        Assert.Equal(JsonValueKind.Array, playlists.ValueKind);
        Assert.Equal(0, playlists.GetArrayLength());
    }

    [Fact]
    public void BuildCompanionState_PlaylistId_MatchesActualPlaylistGuid()
    {
        var vm = new MainViewModel();
        var expectedId = vm.AudioChannels[0].Player.Playlists[0].Id.ToString();
        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var playlists = doc.RootElement.GetProperty("audio").GetProperty("playlists");
        var ids = playlists.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Contains(expectedId, ids);
    }
}
