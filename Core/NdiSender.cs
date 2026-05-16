using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Owns one NDI send instance and streams the output's live page on a background thread.
/// Clock mode keeps the thread paced by the NDI SDK — no extra sleep needed.
/// </summary>
public sealed class NdiSender : IDisposable
{
    readonly OutputState _output;
    readonly IntPtr      _sender;
    readonly Thread      _thread;

    readonly int    _w, _h, _stride;
    readonly byte[] _buffer;
    readonly GCHandle _pin;

    volatile bool _running = true;

    // Transition + animation state (background-thread-only, no locking needed)
    Page?    _prevLive;
    Page?    _fromPage;
    DateTime _transStartTime;
    DateTime _pageStartTime;

    public NdiSender(OutputState output)
    {
        _output = output;
        _w      = output.Config.Width;
        _h      = output.Config.Height;
        _stride = _w * 4;
        _buffer = new byte[_stride * _h];
        _pin    = GCHandle.Alloc(_buffer, GCHandleType.Pinned);

        string streamName = string.IsNullOrWhiteSpace(output.Config.NdiStreamName)
            ? output.Config.Name
            : output.Config.NdiStreamName;

        byte[] nameBytes = Encoding.UTF8.GetBytes(streamName + "\0");
        IntPtr namePtr   = Marshal.AllocHGlobal(nameBytes.Length);
        Marshal.Copy(nameBytes, 0, namePtr, nameBytes.Length);

        var create = new NewTek.NDIlib.send_create_t
        {
            p_ndi_name  = namePtr,
            p_groups    = IntPtr.Zero,
            clock_video = true,
            clock_audio = false
        };

        _sender = NewTek.NDIlib.send_create(ref create);
        Marshal.FreeHGlobal(namePtr);

        _thread = new Thread(SendLoop)
        {
            Name         = $"NDI:{streamName}",
            IsBackground = true
        };
        _thread.Start();
    }

    // ── Send loop (background thread) ─────────────────────────────────────────

    void SendLoop()
    {
        if (_sender == IntPtr.Zero) return;

        (int rateN, int rateD) = FrameRateND(_output.Config.FrameRate);

        var frame = new NewTek.NDIlib.video_frame_v2_t
        {
            xres                 = _w,
            yres                 = _h,
            FourCC               = NewTek.NDIlib.FourCC_BGRA,
            frame_rate_N         = rateN,
            frame_rate_D         = rateD,
            picture_aspect_ratio = (float)_w / _h,
            frame_format_type    = NewTek.NDIlib.FrameFormat_Progressive,
            timecode             = long.MaxValue,
            p_data               = _pin.AddrOfPinnedObject(),
            line_stride_in_bytes = _stride
        };

        while (_running)
        {
            try
            {
                DetectPageChange();

                bool hasConnections = NewTek.NDIlib.send_get_no_connections(_sender, 0) > 0;
                RenderFrame(hasConnections);

                // Blocking send — with clock_video=true the SDK sleeps here to maintain frame rate
                NewTek.NDIlib.send_send_video_v2(_sender, ref frame);
            }
            catch (Exception ex)
            {
                // Log and continue — a single frame error must not kill the output stream.
                System.Diagnostics.Debug.WriteLine(
                    $"[NDI:{_output.Config.Name}] frame error: {ex.GetType().Name}: {ex.Message}");
                // Brief pause to avoid a tight spin if the error is persistent.
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
        _pageStartTime = skipAnims
            ? DateTime.UtcNow.AddSeconds(-10)
            : DateTime.UtcNow;
        if (hasTransition) _transStartTime = DateTime.UtcNow;
        _prevLive = currentLive;
    }

    void RenderFrame(bool render)
    {
        if (!render) { Array.Clear(_buffer); return; }

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
            _pageStartTime = DateTime.UtcNow; // reset so layer animations start from transition end
        }

        if (_output.LivePage is { } page)
        {
            double elapsed = (DateTime.UtcNow - _pageStartTime).TotalMilliseconds;
            using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
            PageRenderer.Render(surface.Canvas, page, _output.Roles, _w, _h, elapsed);
        }
        else
        {
            Array.Clear(_buffer);
        }
    }

    static (int N, int D) FrameRateND(double fps)
    {
        int rounded = (int)Math.Round(fps * 1000);
        return rounded switch
        {
            23976 => (24000, 1001),
            24000 => (24, 1),
            25000 => (25, 1),
            29970 => (30000, 1001),
            30000 => (30, 1),
            50000 => (50, 1),
            59940 => (60000, 1001),
            _     => (60, 1)
        };
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _running = false;
        // Wait for the send loop to exit before destroying the sender.
        // send_send_video_v2 blocks for up to one frame when clock_video=true,
        // so we give it a bit more than one frame at the minimum supported rate (24fps ≈ 42ms).
        _thread.Join(250);
        if (_sender != IntPtr.Zero)
            NewTek.NDIlib.send_destroy(_sender);
        _pin.Free();
    }
}
