using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ShowCast.Blackmagic;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Owns one DeckLink output instance and streams the live page on a background thread.
/// DisplayVideoFrameSync blocks until the frame is displayed, providing frame pacing.
/// </summary>
public sealed class BlackmagicSender : IDisposable
{
    readonly OutputState             _output;
    readonly int                     _w, _h, _stride;
    readonly byte[]                  _buffer;
    readonly GCHandle                _pin;
    readonly VideoFrameRegistry      _videoRegistry;

    IDeckLinkOutput?             _deckLinkOutput;
    IDeckLinkMutableVideoFrame?  _frame;
    Thread?                      _thread;

    volatile bool _running = true;

    // Transition + animation state (background-thread-only)
    Page?    _prevLive;
    Page?    _fromPage;
    DateTime _transStartTime;
    DateTime _pageStartTime;

    public BlackmagicSender(OutputState output, IReadOnlyList<AudioDestination> audioDestinations,
                            Func<string, NdiSender?>? ndiLookup = null)
    {
        _output  = output;
        _w       = output.Config.Width;
        _h       = output.Config.Height;
        _stride  = _w * 4;
        _buffer  = new byte[_stride * _h];
        _pin     = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _videoRegistry = new VideoFrameRegistry(audioDestinations, ndiLookup: ndiLookup);
        output.VideoRegistry = _videoRegistry;

        if (!DeckLinkApi.IsAvailable) return;

        // Resolve device by name; fall back to first device if serial not found.
        var devices = DeckLinkApi.EnumerateDevices();
        int deviceIndex = devices.IndexOf(output.Config.DeviceSerial);
        if (deviceIndex < 0 && devices.Count > 0) deviceIndex = 0;
        if (deviceIndex < 0)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] No DeckLink device found.");
            return;
        }

        // Walk the iterator to the chosen device.
        var iter  = (IDeckLinkIterator)new CDeckLinkIteratorClass();
        IDeckLink? card = null;
        for (int i = 0; iter.Next(out var dev) == 0; i++)
        {
            if (i == deviceIndex) { card = dev; break; }
            Marshal.ReleaseComObject(dev);
        }
        Marshal.ReleaseComObject(iter);

        if (card is null)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] Could not acquire device.");
            return;
        }

        _deckLinkOutput = card as IDeckLinkOutput;
        Marshal.ReleaseComObject(card);

        if (_deckLinkOutput is null)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] Device has no output capability.");
            return;
        }

        int displayMode = DeckLinkApi.GetDisplayMode(_w, _h, output.Config.FrameRate);
        int hr = _deckLinkOutput.EnableVideoOutput(displayMode, 0 /* bmdVideoOutputFlagDefault */);
        if (hr < 0)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] EnableVideoOutput failed: 0x{hr:X8}");
            Marshal.ReleaseComObject(_deckLinkOutput);
            _deckLinkOutput = null;
            return;
        }

        hr = _deckLinkOutput.CreateVideoFrame(_w, _h, _stride, DeckLinkApi.PixelFormat_8BitBGRA, 0, out _frame);
        if (hr < 0)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] CreateVideoFrame failed: 0x{hr:X8}");
            _deckLinkOutput.DisableVideoOutput();
            Marshal.ReleaseComObject(_deckLinkOutput);
            _deckLinkOutput = null;
            return;
        }

        _thread = new Thread(SendLoop)
        {
            Name         = $"DeckLink:{output.Config.Name}",
            IsBackground = true
        };
        _thread.Start();
    }

    // ── Send loop (background thread) ─────────────────────────────────────────

    void SendLoop()
    {
        if (_deckLinkOutput is null || _frame is null) return;

        while (_running)
        {
            try
            {
                DetectPageChange();
                RenderFrame();

                _frame.GetBytes(out IntPtr ptr);
                Marshal.Copy(_buffer, 0, ptr, _buffer.Length);

                // Cast triggers COM QI → IDeckLinkVideoFrame vtable pointer.
                _deckLinkOutput.DisplayVideoFrameSync((IDeckLinkVideoFrame)_frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DeckLink:{_output.Config.Name}] frame error: {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(33);
            }
        }
    }

    void DetectPageChange()
    {
        var currentLive = _output.LivePage;
        if (currentLive == _prevLive) return;

        bool skipAnims = _output.PendingSkipEntryAnimations;
        bool hasTransition = !skipAnims
                          && _prevLive is not null && currentLive is not null
                          && _output.PendingTransitionType != TransitionType.Cut
                          && _output.PendingTransitionDuration > 0;

        _fromPage      = hasTransition ? _prevLive : null;
        _pageStartTime = skipAnims ? DateTime.UtcNow.AddSeconds(-10) : DateTime.UtcNow;
        if (hasTransition) _transStartTime = DateTime.UtcNow;
        _prevLive = currentLive;
        _videoRegistry.UpdateSlide(currentLive);
    }

    void RenderFrame()
    {
        var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888);

        if (_fromPage is not null && _output.LivePage is not null)
        {
            double trans = (DateTime.UtcNow - _transStartTime).TotalMilliseconds;
            float  prog  = _output.PendingTransitionDuration > 0
                ? (float)(trans / _output.PendingTransitionDuration) : 1f;

            if (prog < 1f)
            {
                using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
                TransitionCompositor.Composite(surface.Canvas, _fromPage, _output.LivePage,
                    _output.Roles, _output.PendingTransitionType,
                    prog, _output.PendingTransitionEasing, _w, _h, trans);
                return;
            }
            _fromPage      = null;
            _pageStartTime = DateTime.UtcNow;
        }

        if (_output.LivePage is { } page)
        {
            double elapsed = (DateTime.UtcNow - _pageStartTime).TotalMilliseconds;
            using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
            PageRenderer.Render(surface.Canvas, page, _output.Roles, _w, _h, elapsed,
                                getVideoFrame: _videoRegistry.TryGetFrame);
        }
        else
        {
            Array.Clear(_buffer);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _running = false;
        _thread?.Join(250);

        if (_deckLinkOutput is not null)
        {
            try { _deckLinkOutput.DisableVideoOutput(); } catch { }
            Marshal.ReleaseComObject(_deckLinkOutput);
            _deckLinkOutput = null;
        }
        if (_frame is not null)
        {
            Marshal.ReleaseComObject(_frame);
            _frame = null;
        }

        _output.VideoRegistry = null;
        _videoRegistry.Dispose();
        _pin.Free();
    }
}
