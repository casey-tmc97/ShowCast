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
        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var playlists = doc.RootElement.GetProperty("audio").GetProperty("playlists");
        Assert.True(playlists.GetArrayLength() > 0);
        var first = playlists[0];
        Assert.Equal(JsonValueKind.String, first.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, first.GetProperty("name").ValueKind);
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
