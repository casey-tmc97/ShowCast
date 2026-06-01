using System;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Decodes one video file into a continuously-updated <see cref="CurrentFrame"/> SKBitmap
/// using LibVLC's software callback API. Thread-safe: frame is read on the render thread,
/// written on LibVLC's decode thread.
/// </summary>
public sealed class VideoLayerPlayer : IVideoLayerPlayer
{
    readonly LibVLC      _libVlc;
    readonly MediaPlayer _player;

    byte[]?  _frameBuffer;
    GCHandle _pin;
    uint     _frameWidth;
    uint     _frameHeight;

    readonly object _frameLock = new();
    SKBitmap?       _currentFrame;
    VideoLoopMode   _loopMode;

    // Managed delegate fields — must stay rooted while VLC holds unmanaged pointers.
    readonly MediaPlayer.LibVLCVideoFormatCb  _fmtCb;
    readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    readonly MediaPlayer.LibVLCVideoLockCb    _lockCb;
    readonly MediaPlayer.LibVLCVideoUnlockCb  _unlockCb;
    readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    public SKBitmap? CurrentFrame
    {
        get { lock (_frameLock) return _currentFrame; }
    }

    public VideoLayerPlayer()
    {
        LibVLCSharp.Shared.Core.Initialize();
        _libVlc = new LibVLC();
        _player  = new MediaPlayer(_libVlc);

        _fmtCb     = OnVideoFormat;
        _cleanupCb = OnVideoCleanup;
        _lockCb    = OnVideoLock;
        _unlockCb  = OnVideoUnlock;
        _displayCb = OnVideoDisplay;

        _player.SetVideoFormatCallbacks(_fmtCb, _cleanupCb);
        _player.SetVideoCallbacks(_lockCb, _unlockCb, _displayCb);
        _player.EndReached += OnEndReached;
    }

    uint OnVideoFormat(ref IntPtr opaque, IntPtr chroma, ref uint width, ref uint height,
                       ref uint pitches, ref uint lines)
    {
        // Tell VLC to output BGRA (matches SkiaSharp native on Windows).
        Marshal.WriteByte(chroma, 0, (byte)'B');
        Marshal.WriteByte(chroma, 1, (byte)'G');
        Marshal.WriteByte(chroma, 2, (byte)'R');
        Marshal.WriteByte(chroma, 3, (byte)'A');

        _frameWidth  = width;
        _frameHeight = height;
        pitches      = width * 4;
        lines        = height;

        if (_pin.IsAllocated) _pin.Free();
        _frameBuffer = new byte[pitches * lines];
        _pin         = GCHandle.Alloc(_frameBuffer, GCHandleType.Pinned);

        return 1; // one picture buffer
    }

    void OnVideoCleanup(ref IntPtr opaque)
    {
        if (_pin.IsAllocated) _pin.Free();
        _frameBuffer = null;
    }

    IntPtr OnVideoLock(IntPtr opaque, IntPtr planes)
    {
        // planes is a pointer to an array of plane pointers; write our buffer address into slot 0.
        if (_pin.IsAllocated)
            Marshal.WriteIntPtr(planes, 0, _pin.AddrOfPinnedObject());
        return IntPtr.Zero; // picture handle (unused by VLC when opaque is null)
    }

    void OnVideoUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        if (_frameBuffer is null || !_pin.IsAllocated) return;

        var info   = new SKImageInfo((int)_frameWidth, (int)_frameHeight,
                                     SKColorType.Bgra8888, SKAlphaType.Premul);
        var newBmp = new SKBitmap(info);
        Marshal.Copy(_frameBuffer, 0, newBmp.GetPixels(), _frameBuffer.Length);

        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = newBmp;
        }
    }

    void OnVideoDisplay(IntPtr opaque, IntPtr picture) { }

    void OnEndReached(object? sender, EventArgs e)
    {
        // Calling Stop/Play directly on the VLC event thread causes a deadlock.
        // Queue to thread pool so we return from the event handler first.
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            switch (_loopMode)
            {
                case VideoLoopMode.Loop:
                    _player.Stop();
                    _player.Play();
                    break;
                case VideoLoopMode.GoBlack:
                    lock (_frameLock)
                    {
                        _currentFrame?.Dispose();
                        _currentFrame = null;
                    }
                    break;
                // HoldLastFrame: do nothing — last decoded frame stays in _currentFrame.
            }
        });
    }

    public void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId)
    {
        _loopMode = loopMode;

        if (!string.IsNullOrEmpty(audioDeviceId))
            _player.SetOutputDevice("mmdevice", audioDeviceId);

        using var media = new Media(_libVlc, filePath);
        _player.Media  = media;
        _player.Volume = (int)(volume * 100);
        _player.Play();
    }

    public void Stop()
    {
        _player.Stop();
        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }
    }

    public void Dispose()
    {
        _player.EndReached -= OnEndReached;
        try { _player.Stop(); } catch { }
        _player.Dispose();
        _libVlc.Dispose();
        if (_pin.IsAllocated) _pin.Free();
        lock (_frameLock)
        {
            _currentFrame?.Dispose();
            _currentFrame = null;
        }
    }
}
