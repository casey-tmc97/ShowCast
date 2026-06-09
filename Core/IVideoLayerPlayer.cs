using System;
using SkiaSharp;

namespace ShowCast.Core;

public interface IVideoLayerPlayer : IDisposable
{
    SKImage? CurrentFrame { get; }
    long TimeMs   { get; }
    long LengthMs { get; }
    Action? VideoEnded { get; set; }
    void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId, NdiSender? ndiSender = null);
    void Stop();
}
