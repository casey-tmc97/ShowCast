using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.Views;

public partial class OutputWindow : Window
{
    readonly OutputState        _output;
    readonly List<IDisposable>  _subs  = new();
    readonly DispatcherTimer    _timer = new();

    DateTime _pageStartTime;
    DateTime _transStartTime;
    Page?    _fromPage;

    public OutputWindow() { }

    public OutputWindow(OutputState output)
    {
        InitializeComponent();
        _output = output;
        Title   = $"ShowCast — {output.Name}";

        _timer.Interval = TimeSpan.FromMilliseconds(16);
        _timer.Tick += OnTick;

        Page? prev = null;
        _subs.Add(output.WhenAnyValue(o => o.LivePage).Subscribe(page =>
        {
            OnLivePageChanged(prev, page);
            prev = page;
        }));

        Redraw();
    }

    public void PositionOnScreen(Screens screens)
    {
        var all = screens.All;
        int idx = Math.Clamp(_output.Config.DisplayIndex, 0, all.Count - 1);
        var screen = all[idx];
        Position    = screen.Bounds.TopLeft;
        WindowState = WindowState.FullScreen;
    }

    void OnLivePageChanged(Page? from, Page? to)
    {
        bool skipAnims = _output.PendingSkipEntryAnimations;

        // Far-past start time makes all entry animations appear already complete.
        _pageStartTime = skipAnims
            ? DateTime.UtcNow.AddSeconds(-10)
            : DateTime.UtcNow;

        bool hasTransition = !skipAnims
                          && from is not null && to is not null
                          && _output.PendingTransitionType != TransitionType.Cut
                          && _output.PendingTransitionDuration > 0;

        if (hasTransition)
        {
            _fromPage       = from;
            _transStartTime = DateTime.UtcNow;
            if (!_timer.IsEnabled) _timer.Start();
        }
        else
        {
            _fromPage = null;
            if (skipAnims)
            {
                // Stop any running animation loop, render the final (fully-animated) state
                // immediately, then restart only if timer-bound layers need live updates.
                _timer.Stop();
                Redraw();
                if (HasTimerBoundLayers(_output.LivePage))
                    _timer.Start();
            }
            else
            {
                StartTimerIfNeeded();
            }
        }
    }

    void StartTimerIfNeeded()
    {
        bool hasAnims = _output.LivePage?.Layers.Any(l =>
            l.EntryAnim != LayerAnimation.None ||
            (l.ExitAnim != LayerExitAnimation.None && l.HoldDurationMs > 0)) == true;

        if (hasAnims || HasTimerBoundLayers(_output.LivePage))
            { if (!_timer.IsEnabled) _timer.Start(); }
        else Redraw();
    }

    static bool HasTimerBoundLayers(Page? page) =>
        page?.Layers.Any(l => l.Type == LayerType.Text && l.TimerBinding is not null) == true;

    void OnTick(object? sender, EventArgs e)
    {
        if (_output.LivePage is null) { _timer.Stop(); Redraw(); return; }

        if (_fromPage is not null)
        {
            double trans = (DateTime.UtcNow - _transStartTime).TotalMilliseconds;
            float  prog  = _output.PendingTransitionDuration > 0
                ? (float)(trans / _output.PendingTransitionDuration) : 1f;

            if (prog < 1f) { RenderTransitionFrame(prog, trans); return; }
            _fromPage = null;
        }

        double elapsed = (DateTime.UtcNow - _pageStartTime).TotalMilliseconds;
        bool animating = _output.LivePage.Layers.Any(l =>
        {
            int entryDur = l.EntryDurationMs > 0 ? l.EntryDurationMs : 400;
            if (l.EntryAnim != LayerAnimation.None &&
                elapsed < l.EntryDelayMs + entryDur)
                return true;
            if (l.ExitAnim != LayerExitAnimation.None && l.HoldDurationMs > 0)
            {
                float xs = l.EntryDelayMs + entryDur + l.HoldDurationMs;
                float xe = xs + l.ExitDelayMs + (l.ExitDurationMs > 0 ? l.ExitDurationMs : 400);
                if (elapsed < xe) return true;
            }
            return false;
        });

        if (animating || HasTimerBoundLayers(_output.LivePage))
            { RenderLayerAnimFrame(elapsed); return; }

        _timer.Stop();
        RenderLayerAnimFrame(elapsed);
    }

    void RenderTransitionFrame(float prog, double exitElapsed)
    {
        int w = _output.Config.Width, h = _output.Config.Height;
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));
        TransitionCompositor.Composite(surface.Canvas, _fromPage!, _output.LivePage!,
            _output.Roles, _output.PendingTransitionType,
            prog, _output.PendingTransitionEasing, w, h, exitElapsed);
        RenderImage.Source = ToWriteableBitmap(surface, w, h);
    }

    void RenderLayerAnimFrame(double elapsed)
    {
        int w = _output.Config.Width, h = _output.Config.Height;
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));
        PageRenderer.Render(surface.Canvas, _output.LivePage!, _output.Roles, w, h, elapsed);
        RenderImage.Source = ToWriteableBitmap(surface, w, h);
    }

    void Redraw()
    {
        int w = _output.Config.Width, h = _output.Config.Height;
        using var surface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));
        if (_output.LivePage is not null)
            PageRenderer.Render(surface.Canvas, _output.LivePage, _output.Roles, w, h);
        else
            surface.Canvas.Clear(SKColors.Black);
        RenderImage.Source = ToWriteableBitmap(surface, w, h);
    }

    static Bitmap ToWriteableBitmap(SKSurface surface, int w, int h)
    {
        var wb = new WriteableBitmap(new PixelSize(w, h), new Vector(96, 96),
                                     PixelFormats.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            using var img = surface.Snapshot();
            img.ReadPixels(new SKImageInfo(w, h, SKColorType.Bgra8888, SKAlphaType.Premul),
                           fb.Address, fb.RowBytes, 0, 0);
        }
        return wb;
    }

    protected override void OnClosed(EventArgs e)
    {
        _timer.Stop();
        foreach (var s in _subs) s.Dispose();
        base.OnClosed(e);
    }
}
