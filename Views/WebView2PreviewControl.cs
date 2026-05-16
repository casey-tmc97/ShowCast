using System;
using System.Collections.Generic;
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

public sealed class WebView2PreviewControl : UserControl, IDisposable
{
    // ── Styled properties ─────────────────────────────────────────────────────

    public static readonly StyledProperty<OutputState?> OutputProperty =
        AvaloniaProperty.Register<WebView2PreviewControl, OutputState?>(nameof(Output));

    public static readonly StyledProperty<LayerRole> RolesProperty =
        AvaloniaProperty.Register<WebView2PreviewControl, LayerRole>(nameof(Roles), LayerRole.All);

    public OutputState? Output
    {
        get => GetValue(OutputProperty);
        set => SetValue(OutputProperty, value);
    }

    public LayerRole Roles
    {
        get => GetValue(RolesProperty);
        set => SetValue(RolesProperty, value);
    }

    // ── State ─────────────────────────────────────────────────────────────────

    const int W = 320, H = 180;
    readonly Image           _img   = new();
    readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    readonly List<IDisposable> _subs = new();

    DateTime _transStartTime;
    DateTime _pageStartTime;
    Page?    _fromPage;
    Page?    _currentPage;
    OutputState? _currentOutput;

    // ── Constructor ───────────────────────────────────────────────────────────

    public WebView2PreviewControl()
    {
        Content = _img;
        _timer.Tick += OnTick;
        TimerTextCache.Changed += OnTimerChanged;
        RenderBlack();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        double w = double.IsInfinity(availableSize.Width) ? W : availableSize.Width;
        return new Size(w, w * 9.0 / 16.0);
    }

    // ── Property changes ──────────────────────────────────────────────────────

    protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
    {
        base.OnPropertyChanged(change);

        if (change.Property == OutputProperty)
        {
            // Tear down previous subscription
            foreach (var s in _subs) s.Dispose();
            _subs.Clear();
            _fromPage = null;
            _timer.Stop();

            _currentOutput = change.GetNewValue<OutputState?>();
            if (_currentOutput is not null)
            {
                Page? prev = null;
                _subs.Add(_currentOutput.WhenAnyValue(o => o.LivePage).Subscribe(page =>
                {
                    OnLivePageChanged(_currentOutput, prev, page);
                    prev = page;
                }));
            }
            else
            {
                _currentPage = null;
                RenderBlack();
            }
        }
        else if (change.Property == RolesProperty)
        {
            RenderStatic(_currentPage);
        }
    }

    // ── Live-page change handler ──────────────────────────────────────────────

    void OnLivePageChanged(OutputState output, Page? from, Page? to)
    {
        _currentPage = to;

        bool skipAnims = output.PendingSkipEntryAnimations;

        _pageStartTime = skipAnims
            ? DateTime.UtcNow.AddSeconds(-10)
            : DateTime.UtcNow;

        bool hasTransition = !skipAnims
                          && from is not null && to is not null
                          && output.PendingTransitionType != TransitionType.Cut
                          && output.PendingTransitionDuration > 0;

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
                _timer.Stop();
                RenderStatic(to);
            }
            else
            {
                StartTimerIfNeeded(to);
            }
        }
    }

    void StartTimerIfNeeded(Page? page)
    {
        bool hasAnims = page?.Layers.Any(l =>
            l.EntryAnim != LayerAnimation.None ||
            (l.ExitAnim != LayerExitAnimation.None && l.HoldDurationMs > 0)) == true;

        if (hasAnims)
            { if (!_timer.IsEnabled) _timer.Start(); }
        else
            RenderStatic(page);
    }

    // ── Timer tick ────────────────────────────────────────────────────────────

    void OnTick(object? sender, EventArgs e)
    {
        var output = _currentOutput;
        if (output is null || _currentPage is null) { _timer.Stop(); RenderBlack(); return; }

        if (_fromPage is not null)
        {
            double trans = (DateTime.UtcNow - _transStartTime).TotalMilliseconds;
            float  prog  = output.PendingTransitionDuration > 0
                ? (float)(trans / output.PendingTransitionDuration) : 1f;

            if (prog < 1f)
            {
                RenderTransition(output, prog, trans);
                return;
            }
            _fromPage = null;
        }

        double elapsed  = (DateTime.UtcNow - _pageStartTime).TotalMilliseconds;
        bool animating = _currentPage.Layers.Any(l =>
        {
            int entryDur = l.EntryDurationMs > 0 ? l.EntryDurationMs : 400;
            if (l.EntryAnim != LayerAnimation.None && elapsed < l.EntryDelayMs + entryDur)
                return true;
            if (l.ExitAnim != LayerExitAnimation.None && l.HoldDurationMs > 0)
            {
                float xs = l.EntryDelayMs + entryDur + l.HoldDurationMs;
                float xe = xs + l.ExitDelayMs + (l.ExitDurationMs > 0 ? l.ExitDurationMs : 400);
                if (elapsed < xe) return true;
            }
            return false;
        });

        if (animating)
            { RenderAnimFrame(elapsed); return; }

        _timer.Stop();
        RenderAnimFrame(elapsed);
    }

    // ── Render helpers ────────────────────────────────────────────────────────

    void RenderTransition(OutputState output, float prog, double exitElapsed)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888));
        TransitionCompositor.Composite(surface.Canvas, _fromPage!, _currentPage!,
            output.Roles, output.PendingTransitionType,
            prog, output.PendingTransitionEasing, W, H, exitElapsed);
        _img.Source = ToWriteableBitmap(surface);
    }

    void RenderAnimFrame(double elapsed)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888));
        if (_currentPage is not null)
            PageRenderer.Render(surface.Canvas, _currentPage, Roles, W, H, elapsed);
        else
            surface.Canvas.Clear(SKColors.Black);
        _img.Source = ToWriteableBitmap(surface);
    }

    void RenderStatic(Page? page)
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888));
        if (page is not null)
            PageRenderer.Render(surface.Canvas, page, Roles, W, H);
        else
            surface.Canvas.Clear(SKColors.Black);
        _img.Source = ToWriteableBitmap(surface);
    }

    void RenderBlack()
    {
        using var surface = SKSurface.Create(new SKImageInfo(W, H, SKColorType.Rgba8888));
        surface.Canvas.Clear(SKColors.Black);
        _img.Source = ToWriteableBitmap(surface);
    }

    static WriteableBitmap ToWriteableBitmap(SKSurface surface)
    {
        var wb = new WriteableBitmap(new PixelSize(W, H), new Vector(96, 96),
                                     PixelFormats.Bgra8888, AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            using var img = surface.Snapshot();
            img.ReadPixels(new SKImageInfo(W, H, SKColorType.Bgra8888, SKAlphaType.Premul),
                           fb.Address, fb.RowBytes, 0, 0);
        }
        return wb;
    }

    public void SetNativeVisible(bool visible) { }

    void OnTimerChanged()
    {
        // Animation loop already re-renders on its own tick; only act when idle.
        if (_currentPage is not null && !_timer.IsEnabled)
            RenderStatic(_currentPage);
    }

    public void Dispose()
    {
        _timer.Stop();
        TimerTextCache.Changed -= OnTimerChanged;
        foreach (var s in _subs) s.Dispose();
    }
}
