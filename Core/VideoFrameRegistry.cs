using System;
using System.Collections.Concurrent;
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
    readonly Func<string, NdiSender?>?       _ndiLookup;
    readonly ConcurrentDictionary<Guid, IVideoLayerPlayer> _players = new();

    /// <summary>Invoked (on the threadpool) when a video with AdvanceOnEnd mode reaches its end.</summary>
    public Action? OnVideoEnded { get; set; }

    public VideoFrameRegistry(IReadOnlyList<AudioDestination> destinations,
                              Func<IVideoLayerPlayer>? playerFactory = null,
                              Func<string, NdiSender?>? ndiLookup = null)
    {
        _destinations  = destinations;
        _playerFactory = playerFactory ?? (() => new VideoLayerPlayer());
        _ndiLookup     = ndiLookup;
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
            _players.TryRemove(id, out _);
        }

        // Start players for new layers.
        foreach (var layer in newLayers.Where(l => !_players.ContainsKey(l.Id)))
        {
            var player = _playerFactory();
            var filePath = Path.Combine(AppFolders.Video, layer.AssetPath);

            // Resolve audio routing: hardware uses a WASAPI device ID; NDI uses a sender reference.
            var dest = layer.VideoAudioDestinationId is { } destId
                ? _destinations.FirstOrDefault(d => d.Id == destId)
                : null;
            string? deviceId = dest?.Type == AudioRouteType.Hardware ? dest.DeviceId : null;
            NdiSender? ndiSender = dest?.Type == AudioRouteType.Ndi
                ? _ndiLookup?.Invoke(dest.DeviceId)
                : null;

            player.Start(filePath, layer.VideoLoopMode, layer.VideoVolume, deviceId, ndiSender);
            if (layer.VideoLoopMode == VideoLoopMode.AdvanceOnEnd)
                player.VideoEnded = OnVideoEnded;
            _players[layer.Id] = player;
        }
    }

    /// <summary>Returns the most recently decoded frame for a layer, or null if unavailable.</summary>
    public SKImage? TryGetFrame(Guid layerId) =>
        _players.TryGetValue(layerId, out var p) ? p.CurrentFrame : null;

    /// <summary>Returns position and duration of the first active video player, or zeros if none.</summary>
    public (long timeMs, long lengthMs) GetPrimaryTime()
    {
        var player = _players.Values.FirstOrDefault();
        return player is null ? (0, 0) : (Math.Max(0, player.TimeMs), Math.Max(0, player.LengthMs));
    }

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
