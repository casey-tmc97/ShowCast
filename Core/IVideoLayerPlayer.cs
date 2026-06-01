using System;
using SkiaSharp;

namespace ShowCast.Core;

public interface IVideoLayerPlayer : IDisposable
{
    SKBitmap? CurrentFrame { get; }
    void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId);
    void Stop();
}
