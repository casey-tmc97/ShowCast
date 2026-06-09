using System;
using System.Runtime.InteropServices;
using LibVLCSharp.Shared;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Decodes one video file into a continuously-updated <see cref="CurrentFrame"/> SKImage
/// using LibVLC's software callback API.
///
/// Thread-safety: <see cref="CurrentFrame"/> is a volatile reference — the render thread
/// reads it lock-free and holds a GC-rooted reference through its stack, so the old image
/// stays alive until all readers are done (GC collects it; no explicit Dispose race).
/// </summary>
public sealed class VideoLayerPlayer : IVideoLayerPlayer
{
    readonly LibVLC      _libVlc;
    readonly MediaPlayer _player;

    byte[]?  _frameBuffer;
    GCHandle _pin;
    uint     _frameWidth;
    uint     _frameHeight;

    // Volatile: render thread reads lock-free; old SKImage released to GC on swap (not disposed).
    volatile SKImage? _currentFrame;
    VideoLoopMode     _loopMode;
    Media?            _media;

    public Action? VideoEnded { get; set; }

    // NDI audio routing — mirrors the approach in AudioPlayerViewModel.
    volatile NdiSender? _ndiSender;
    MediaPlayer.LibVLCAudioPlayCb? _audioPlayDelegate;

    [DllImport("libvlc", CallingConvention = CallingConvention.Cdecl,
               EntryPoint = "libvlc_audio_set_callbacks")]
    static extern void NativeSetAudioCallbacks(IntPtr mp,
        IntPtr play, IntPtr pause, IntPtr resume, IntPtr flush, IntPtr drain, IntPtr opaque);

    // Managed delegate fields — must stay rooted while VLC holds unmanaged pointers.
    readonly MediaPlayer.LibVLCVideoFormatCb  _fmtCb;
    readonly MediaPlayer.LibVLCVideoCleanupCb _cleanupCb;
    readonly MediaPlayer.LibVLCVideoLockCb    _lockCb;
    readonly MediaPlayer.LibVLCVideoUnlockCb  _unlockCb;
    readonly MediaPlayer.LibVLCVideoDisplayCb _displayCb;

    public SKImage? CurrentFrame => _currentFrame;
    public long TimeMs   => _player.Time;
    public long LengthMs => _player.Length;

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
        // planes points to an array of plane pointers; write our buffer address into slot 0.
        if (_pin.IsAllocated)
            Marshal.WriteIntPtr(planes, 0, _pin.AddrOfPinnedObject());
        return IntPtr.Zero;
    }

    void OnVideoUnlock(IntPtr opaque, IntPtr picture, IntPtr planes)
    {
        if (_frameBuffer is null || !_pin.IsAllocated) return;

        // Copy decoded BGRA pixels into a temporary SKBitmap, then snapshot to SKImage.
        // SKImage.FromBitmap copies pixel data — disposing the bitmap below is safe.
        // Volatile write: render thread holds a live reference via its stack while drawing;
        // GC collects the old SKImage only after all readers drop their references.
        var info = new SKImageInfo((int)_frameWidth, (int)_frameHeight,
                                   SKColorType.Bgra8888, SKAlphaType.Unpremul);
        using var bmp = new SKBitmap(info);
        Marshal.Copy(_frameBuffer, 0, bmp.GetPixels(), _frameBuffer.Length);
        _currentFrame = SKImage.FromBitmap(bmp);
    }

    void OnVideoDisplay(IntPtr opaque, IntPtr picture) { }

    void OnEndReached(object? sender, EventArgs e)
    {
        // Queue to thread pool — calling Stop/Play on the VLC event thread deadlocks.
        var capturedMode  = _loopMode;
        var capturedEnded = VideoEnded;
        System.Threading.ThreadPool.QueueUserWorkItem(_ =>
        {
            switch (capturedMode)
            {
                case VideoLoopMode.Loop:
                    _player.Stop();
                    _player.Play();
                    break;
                case VideoLoopMode.GoBlack:
                    _currentFrame = null;
                    break;
                case VideoLoopMode.AdvanceOnEnd:
                    _player.Stop();
                    capturedEnded?.Invoke();
                    break;
                // HoldLastFrame: do nothing.
            }
        });
    }

    public void Start(string filePath, VideoLoopMode loopMode, float volume, string? audioDeviceId,
                      NdiSender? ndiSender = null)
    {
        _loopMode  = loopMode;
        _ndiSender = ndiSender;

        if (ndiSender is not null)
        {
            // NDI: intercept decoded PCM and forward to NdiSender.SubmitAudio.
            if (_audioPlayDelegate is null)
            {
                _audioPlayDelegate = OnAudioPlay;
                _player.SetAudioFormat("S16N", 48000, 2);
                _player.SetAudioCallbacks(_audioPlayDelegate, null, null, null, null!);
            }
        }
        else
        {
            // Hardware: clear any prior NDI callbacks, then select WASAPI device if specified.
            if (_audioPlayDelegate is not null)
            {
                NativeSetAudioCallbacks(_player.NativeReference,
                    IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                _audioPlayDelegate = null;
            }
            if (!string.IsNullOrEmpty(audioDeviceId))
                _player.SetOutputDevice("mmdevice", audioDeviceId);
        }

        _media         = new Media(_libVlc, filePath);
        _player.Media  = _media;
        _player.Volume = (int)(volume * 100);
        _player.Play();
    }

    void OnAudioPlay(IntPtr data, IntPtr samples, uint count, long pts)
        => _ndiSender?.SubmitAudio(samples, count, 48000, 2);

    /// <summary>Seek to a position in milliseconds. Only call after Start().</summary>
    public void SeekMs(long ms) => _player.Time = ms;

    public void Stop()
    {
        _player.Stop();
        _currentFrame = null;
    }

    public void Dispose()
    {
        _player.EndReached -= OnEndReached;
        try { _player.Stop(); } catch { }
        _player.Dispose();
        _libVlc.Dispose();
        _media?.Dispose();
        _media        = null;
        _currentFrame = null;
        if (_pin.IsAllocated) _pin.Free();
    }
}
