using System;
using SkiaSharp;

namespace ShowCast.Core;

public interface IVideoLayerPlayer : IDisposable
{
    SKImage? CurrentFrame { get; }
    void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId, NdiSender? ndiSender = null);
    void Stop();
}
