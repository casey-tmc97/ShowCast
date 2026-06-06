using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.Core;

public sealed class AjaSender : IDisposable
{
    readonly OutputState        _output;
    readonly int                _w, _h, _stride;
    readonly byte[]             _buffer;
    readonly GCHandle           _pin;
    readonly VideoFrameRegistry _videoRegistry;

    IntPtr  _handle;
    Thread? _thread;

    volatile bool _running = true;

    // Transition + animation state (background-thread-only)
    Page?    _prevLive;
    Page?    _fromPage;
    DateTime _transStartTime;
    DateTime _pageStartTime;

    readonly int _sleepMs;

    public AjaSender(OutputState output, IReadOnlyList<AudioDestination> audioDestinations,
                     Func<string, NdiSender?>? ndiLookup = null)
    {
        _output   = output;
        _w        = output.Config.Width;
        _h        = output.Config.Height;
        _stride   = _w * 4;
        _buffer   = new byte[_stride * _h];
        _pin      = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _sleepMs  = (int)Math.Round(1000.0 / output.Config.FrameRate);

        _videoRegistry       = new VideoFrameRegistry(audioDestinations, ndiLookup: ndiLookup);
        output.VideoRegistry = _videoRegistry;

        if (!AjaApi.IsAvailable) return;

        // Resolve device by name; fall back to first device if serial not found.
        var devices     = AjaApi.EnumerateDevices();
        int deviceIndex = devices.IndexOf(output.Config.DeviceSerial);
        if (deviceIndex < 0 && devices.Count > 0) deviceIndex = 0;
        if (deviceIndex < 0)
        {
            Console.Error.WriteLine($"[AJA:{output.Config.Name}] No AJA device found.");
            return;
        }

        _handle = AjaApi.Open(deviceIndex, _w, _h, output.Config.FrameRate);
        if (_handle == IntPtr.Zero)
        {
            Console.Error.WriteLine($"[AJA:{output.Config.Name}] aja_open returned null handle.");
            return;
        }

        _thread = new Thread(SendLoop)
        {
            Name         = $"AJA:{output.Config.Name}",
            IsBackground = true
        };
        _thread.Start();
    }

    // ── Send loop (background thread) ─────────────────────────────────────────

    void SendLoop()
    {
        while (_running)
        {
            try
            {
                DetectPageChange();
                RenderFrame();
                AjaApi.SubmitFrame(_handle, _pin.AddrOfPinnedObject(), _buffer.Length);
                Thread.Sleep(_sleepMs);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[AJA:{_output.Config.Name}] frame error: {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(33);
            }
        }
    }

    void DetectPageChange()
    {
        var currentLive = _output.LivePage;
        if (currentLive == _prevLive) return;

        bool skipAnims    = _output.PendingSkipEntryAnimations;
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

        if (_handle != IntPtr.Zero)
        {
            try { AjaApi.Close(_handle); } catch { }
            _handle = IntPtr.Zero;
        }

        _output.VideoRegistry = null;
        _videoRegistry.Dispose();
        _pin.Free();
    }
}
