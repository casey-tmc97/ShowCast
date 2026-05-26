using System;
using System.Text.Json;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class AudioChannelTests
{
    // ── AudioChannel defaults ─────────────────────────────────────────────────

    [Fact]
    public void AudioChannel_DefaultValues_AreCorrect()
    {
        var ch = new AudioChannel();
        Assert.NotEqual(Guid.Empty, ch.Id);
        Assert.Equal("New Channel", ch.Name);
        Assert.Null(ch.ActiveDestinationId);
        Assert.Equal(Guid.Empty, ch.SelectedPlaylistId);
    }

    // ── AudioDestination defaults ─────────────────────────────────────────────

    [Fact]
    public void AudioDestination_DefaultValues_AreCorrect()
    {
        var dest = new AudioDestination();
        Assert.NotEqual(Guid.Empty, dest.Id);
        Assert.Equal("", dest.DisplayName);
        Assert.Equal("", dest.DeviceId);
        Assert.Equal("", dest.SystemName);
        Assert.Equal(AudioRouteType.Hardware, dest.Type);
    }

    // ── AudioPlaylist.ChannelId ───────────────────────────────────────────────

    [Fact]
    public void AudioPlaylist_ChannelId_DefaultsToEmpty()
    {
        var pl = new AudioPlaylist();
        Assert.Equal(Guid.Empty, pl.ChannelId);
    }

    [Fact]
    public void AudioPlaylist_ChannelId_SurvivesShowFileRoundTrip()
    {
        var channelId = Guid.NewGuid();
        var file = new ShowFile();
        var pl = new AudioPlaylist { Name = "Test", ChannelId = channelId };
        file.AudioPlaylists.Add(pl);

        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var json   = JsonSerializer.Serialize(file, opts);
        var loaded = JsonSerializer.Deserialize<ShowFile>(json, opts)!;

        Assert.Equal(channelId, loaded.AudioPlaylists[0].ChannelId);
    }

    // ── AppSettings collections ───────────────────────────────────────────────

    [Fact]
    public void AppSettings_AudioChannels_DeserializesToEmptyList_WhenMissing()
    {
        var json = """{"Version":1}""";
        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var file   = JsonSerializer.Deserialize<ShowFile>(json, opts)!;
        Assert.NotNull(file.Settings.AudioChannels);
        Assert.Empty(file.Settings.AudioChannels);
    }

    [Fact]
    public void AppSettings_AudioDestinations_DeserializesToEmptyList_WhenMissing()
    {
        var json = """{"Version":1}""";
        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var file   = JsonSerializer.Deserialize<ShowFile>(json, opts)!;
        Assert.NotNull(file.Settings.AudioDestinations);
        Assert.Empty(file.Settings.AudioDestinations);
    }

    [Fact]
    public void AppSettings_AudioChannelsAndDestinations_SurviveRoundTrip()
    {
        var file = new ShowFile();
        var dest = new AudioDestination
        {
            DisplayName = "Front of House",
            DeviceId    = "hw-001",
            SystemName  = "Realtek",
            Type        = AudioRouteType.Hardware
        };
        file.Settings.AudioDestinations.Add(dest);
        var ch = new AudioChannel { Name = "Main PA", ActiveDestinationId = dest.Id };
        file.Settings.AudioChannels.Add(ch);

        var opts   = ShowFileSerializer.CreateSerializerOptions();
        var json   = JsonSerializer.Serialize(file, opts);
        var loaded = JsonSerializer.Deserialize<ShowFile>(json, opts)!;

        Assert.Single(loaded.Settings.AudioChannels);
        Assert.Single(loaded.Settings.AudioDestinations);
        Assert.Equal("Main PA", loaded.Settings.AudioChannels[0].Name);
        Assert.Equal(dest.Id, loaded.Settings.AudioChannels[0].ActiveDestinationId);
        Assert.Equal("Front of House", loaded.Settings.AudioDestinations[0].DisplayName);
        Assert.Equal("hw-001", loaded.Settings.AudioDestinations[0].DeviceId);
    }
}
