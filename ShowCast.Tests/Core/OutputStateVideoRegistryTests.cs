using System.Collections.Generic;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class OutputStateVideoRegistryTests
{
    static OutputState MakeState() => new(new OutputConfig());

    [Fact]
    public void VideoRegistry_DefaultsToNull()
    {
        var state = MakeState();
        Assert.Null(state.VideoRegistry);
    }

    [Fact]
    public void VideoRegistry_CanBeSetAndRead()
    {
        var state    = MakeState();
        var registry = new VideoFrameRegistry(new List<AudioDestination>());

        state.VideoRegistry = registry;

        Assert.Same(registry, state.VideoRegistry);
        registry.Dispose();
    }

    [Fact]
    public void VideoRegistry_RaisesPropertyChanged()
    {
        var state    = MakeState();
        var registry = new VideoFrameRegistry(new List<AudioDestination>());
        string? changedProp = null;
        state.PropertyChanged += (_, e) => changedProp = e.PropertyName;

        state.VideoRegistry = registry;

        Assert.Equal(nameof(OutputState.VideoRegistry), changedProp);
        registry.Dispose();
    }

    [Fact]
    public void VideoRegistry_CanBeClearedToNull()
    {
        var state    = MakeState();
        var registry = new VideoFrameRegistry(new List<AudioDestination>());
        state.VideoRegistry = registry;

        state.VideoRegistry = null;

        Assert.Null(state.VideoRegistry);
        registry.Dispose();
    }
}
