using System;
using System.Collections.Generic;
using System.Linq;
using ShowCast.Core;
using SkiaSharp;
using Xunit;

namespace ShowCast.Tests.Core;

file sealed class FakePlayer : IVideoLayerPlayer
{
    public bool   Started      { get; private set; }
    public bool   Stopped      { get; private set; }
    public bool   Disposed     { get; private set; }
    public string? StartedPath { get; private set; }
    public SKImage? CurrentFrame { get; set; }
    public long TimeMs   { get; set; }
    public long LengthMs { get; set; }
    public Action? VideoEnded { get; set; }

    public void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId,
                      NdiSender? ndiSender = null)
    {
        Started     = true;
        StartedPath = filePath;
    }
    public void Stop()    => Stopped = true;
    public void Dispose() => Disposed = true;
}

public class VideoFrameRegistryTests
{
    static Page MakeVideoPage(params (Guid id, string path)[] layers)
    {
        var page = new Page();
        foreach (var (id, path) in layers)
            page.AddLayer(new SlideLayer { Id = id, Type = LayerType.Video, AssetPath = path });
        return page;
    }

    [Fact]
    public void UpdateSlide_StartsPlayerForEachVideoLayer()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        var id = Guid.NewGuid();
        registry.UpdateSlide(MakeVideoPage((id, "clip.mp4")));

        Assert.Single(players);
        Assert.True(players[0].Started);
    }

    [Fact]
    public void UpdateSlide_NullPage_StopsAllPlayers()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        registry.UpdateSlide(MakeVideoPage((Guid.NewGuid(), "clip.mp4")));
        registry.UpdateSlide(null);

        Assert.True(players[0].Stopped);
        Assert.True(players[0].Disposed);
    }

    [Fact]
    public void UpdateSlide_LayerRemoved_DisposesItsPlayer()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        var keepId   = Guid.NewGuid();
        var removeId = Guid.NewGuid();

        registry.UpdateSlide(MakeVideoPage((keepId, "a.mp4"), (removeId, "b.mp4")));
        registry.UpdateSlide(MakeVideoPage((keepId, "a.mp4")));

        Assert.Equal(2, players.Count);
        var removedPlayer = players.First(p => p.StartedPath!.EndsWith("b.mp4"));
        var keptPlayer    = players.First(p => p.StartedPath!.EndsWith("a.mp4"));
        Assert.True(removedPlayer.Disposed);
        Assert.False(keptPlayer.Disposed);
    }

    [Fact]
    public void UpdateSlide_SameLayerIdTwice_DoesNotCreateDuplicatePlayer()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        var id = Guid.NewGuid();
        registry.UpdateSlide(MakeVideoPage((id, "clip.mp4")));
        registry.UpdateSlide(MakeVideoPage((id, "clip.mp4")));

        Assert.Single(players);
    }

    [Fact]
    public void TryGetFrame_ReturnsNullWhenNoPlayer()
    {
        var registry = new VideoFrameRegistry(new List<AudioDestination>());
        Assert.Null(registry.TryGetFrame(Guid.NewGuid()));
    }

    [Fact]
    public void Dispose_DisposesAllPlayers()
    {
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        registry.UpdateSlide(MakeVideoPage((Guid.NewGuid(), "a.mp4"), (Guid.NewGuid(), "b.mp4")));
        registry.Dispose();

        Assert.All(players, p => Assert.True(p.Disposed));
    }

    [Fact]
    public void UpdateSlide_WiresVideoEndedCallbackToNewPlayer()
    {
        // FakePlayer needs to expose VideoEnded so we can check it was set.
        // (After this test is added, FakePlayer must implement the new interface member.)
        bool callbackFired = false;
        var players = new List<FakePlayer>();
        var registry = new VideoFrameRegistry(
            new List<AudioDestination>(),
            () => { var p = new FakePlayer(); players.Add(p); return p; });

        registry.OnVideoEnded = () => callbackFired = true;

        var id = Guid.NewGuid();
        var layer = new SlideLayer
        {
            Id = id, Type = LayerType.Video, AssetPath = "clip.mp4",
            VideoLoopMode = VideoLoopMode.AdvanceOnEnd
        };
        var page = new Page();
        page.AddLayer(layer);
        registry.UpdateSlide(page);

        // Simulate the player calling VideoEnded (as VLC would after video finishes).
        players[0].VideoEnded?.Invoke();

        Assert.True(callbackFired);
    }
}
