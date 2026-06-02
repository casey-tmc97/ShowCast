using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Manages one <see cref="IVideoLayerPlayer"/> per active video layer for a single live output.
/// Call <see cref="UpdateSlide"/> when the live page changes; read frames via <see cref="TryGetFrame"/>.
/// </summary>
public sealed class VideoFrameRegistry : IDisposable
{
    readonly IReadOnlyList<AudioDestination> _destinations;
    readonly Func<IVideoLayerPlayer>         _playerFactory;
    readonly Dictionary<Guid, IVideoLayerPlayer> _players = new();

    public VideoFrameRegistry(IReadOnlyList<AudioDestination> destinations,
                              Func<IVideoLayerPlayer>? playerFactory = null)
    {
        _destinations  = destinations;
        _playerFactory = playerFactory ?? (() => new VideoLayerPlayer());
    }

    /// <summary>
    /// Diffs the new page against currently-running players.
    /// Stops players for layers no longer present; starts players for new video layers.
    /// </summary>
    public void UpdateSlide(Page? page)
    {
        var newLayers = page?.Layers
            .Where(l => l.Type == LayerType.Video && !string.IsNullOrEmpty(l.AssetPath))
            .ToList() ?? new List<SlideLayer>();

        var newIds = newLayers.Select(l => l.Id).ToHashSet();

        // Stop and dispose players for removed layers.
        foreach (var id in _players.Keys.Except(newIds).ToList())
        {
            _players[id].Stop();
            _players[id].Dispose();
            _players.Remove(id);
        }

        // Start players for new layers.
        foreach (var layer in newLayers.Where(l => !_players.ContainsKey(l.Id)))
        {
            var player   = _playerFactory();
            var filePath = Path.Combine(AppFolders.Video, layer.AssetPath);
            var deviceId = layer.VideoAudioDestinationId is { } destId
                ? _destinations.FirstOrDefault(d => d.Id == destId)?.DeviceId
                : null;

            player.Start(filePath, layer.VideoLoopMode, layer.VideoVolume, deviceId);
            _players[layer.Id] = player;
        }
    }

    /// <summary>Returns the most recently decoded frame for a layer, or null if unavailable.</summary>
    public SKImage? TryGetFrame(Guid layerId) =>
        _players.TryGetValue(layerId, out var p) ? p.CurrentFrame : null;

    public void Dispose()
    {
        foreach (var p in _players.Values)
        {
            p.Stop();
            p.Dispose();
        }
        _players.Clear();
    }
}
