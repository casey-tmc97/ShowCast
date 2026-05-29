using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.Engine;
using ShowCast.ViewModels;
using SkiaSharp;

namespace ShowCast.Views;

enum HandleKind { None = -1, NW = 0, N = 1, NE = 2, W = 3, E = 4, SW = 5, S = 6, SE = 7, Move = 8, Rotate = 9 }

/// <summary>
/// Interactive slide editing canvas with GIMP/Inkscape-style rulers, grid,
/// 8-point resize handles, rotation handle, and snap-to-grid.
/// </summary>
public class EditorCanvas : UserControl, IDisposable
{
    const double HandleSize   = 9;
    const double HandleHalf   = HandleSize / 2;
    const double RotHandSize  = 11;
    const double RotHandHalf  = RotHandSize / 2;
    const double RotHandDist  = 26;   // px above N handle
    const int    RenderW      = 1280;
    const int    RenderH      = 720;
    const double RulerSize    = 22;

    // ── Slide / overlay ───────────────────────────────────────────────────────
    readonly Image     _slideImg   = new() { Stretch = Stretch.Uniform, IsHitTestVisible = false };
    readonly Canvas    _overlay    = new() { Background = Brushes.Transparent };
    readonly Canvas    _gridCanvas = new() { IsHitTestVisible = false };
    readonly Canvas    _safeCanvas = new() { IsHitTestVisible = false };
    readonly Rectangle _selBorder  = new()
    {
        Stroke           = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
        StrokeThickness  = 1.5,
        Fill             = Brushes.Transparent,
        IsHitTestVisible = false,
        IsVisible        = false
    };
    readonly Rectangle[] _handles = new Rectangle[8];
    readonly List<Rectangle> _layerBounds = new();

    // Rotation handle (red circle above selection)
    readonly Ellipse _rotHandle = new()
    {
        Width  = RotHandSize, Height = RotHandSize,
        Fill   = new SolidColorBrush(Color.FromRgb(220, 80, 80)),
        Stroke = Brushes.White, StrokeThickness = 1,
        IsHitTestVisible = false, IsVisible = false
    };
    readonly Line _rotHandleLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
        StrokeThickness = 1, IsHitTestVisible = false, IsVisible = false
    };

    // ── Move crosshairs ───────────────────────────────────────────────────────
    readonly Line _crossH = new()
    {
        Stroke           = new SolidColorBrush(Color.FromArgb(180, 59, 130, 246)),
        StrokeThickness  = 1,
        StrokeDashArray  = new Avalonia.Collections.AvaloniaList<double> { 5, 3 },
        IsHitTestVisible = false, IsVisible = false
    };
    readonly Line _crossV = new()
    {
        Stroke           = new SolidColorBrush(Color.FromArgb(180, 59, 130, 246)),
        StrokeThickness  = 1,
        StrokeDashArray  = new Avalonia.Collections.AvaloniaList<double> { 5, 3 },
        IsHitTestVisible = false, IsVisible = false
    };

    // ── Rulers ────────────────────────────────────────────────────────────────
    readonly Image  _hRulerImg  = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly Image  _vRulerImg  = new() { Stretch = Stretch.Fill, IsHitTestVisible = false };
    readonly Line   _hRulerLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(220, 80, 80)),
        StrokeThickness = 1, IsHitTestVisible = false, IsVisible = false
    };
    readonly Line   _vRulerLine = new()
    {
        Stroke = new SolidColorBrush(Color.FromRgb(220, 80, 80)),
        StrokeThickness = 1, IsHitTestVisible = false, IsVisible = false
    };
    readonly Canvas _hRulerCanvas = new() { ClipToBounds = true };
    readonly Canvas _vRulerCanvas = new() { ClipToBounds = true };
    readonly Border _cornerBox    = new()
    {
        Width = RulerSize, Height = RulerSize,
        Background = new SolidColorBrush(Color.FromRgb(30, 30, 30))
    };

    // ── Root grid ─────────────────────────────────────────────────────────────
    readonly Grid _rootGrid = new();


    // ── Drag state ────────────────────────────────────────────────────────────
    MainViewModel?          _vm;
    readonly List<IDisposable> _subs = new();
    bool       _dragging;
    HandleKind _dragKind;
    Point      _dragOrigin;
    float      _origX, _origY, _origW, _origH;
    double     _rotDragAngle0;
    float      _rotDragOrigDeg;

    // ── Animation preview ─────────────────────────────────────────────────────
    readonly DispatcherTimer _animTimer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    DateTime _animStart;

    // ── Inline text editing ───────────────────────────────────────────────────
    TextBox?    _inlineBox;
    SlideLayer? _inlineLayer;
    bool        _inlineCommitting;

    public EditorCanvas()
    {
        for (int i = 0; i < 8; i++)
        {
            _handles[i] = new Rectangle
            {
                Width = HandleSize, Height = HandleSize,
                Fill  = Brushes.White,
                Stroke = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
                StrokeThickness = 1,
                IsHitTestVisible = false, IsVisible = false
            };
            _overlay.Children.Add(_handles[i]);
        }
        _overlay.Children.Add(_selBorder);
        _overlay.Children.Add(_rotHandleLine);
        _overlay.Children.Add(_rotHandle);
        _overlay.Children.Add(_crossH);
        _overlay.Children.Add(_crossV);

        _hRulerCanvas.Children.Add(_hRulerImg);
        _hRulerCanvas.Children.Add(_hRulerLine);
        _vRulerCanvas.Children.Add(_vRulerImg);
        _vRulerCanvas.Children.Add(_vRulerLine);

        _rootGrid.RowDefinitions.Add(new RowDefinition(RulerSize, GridUnitType.Pixel));
        _rootGrid.RowDefinitions.Add(new RowDefinition(1, GridUnitType.Star));
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition(RulerSize, GridUnitType.Pixel));
        _rootGrid.ColumnDefinitions.Add(new ColumnDefinition(1, GridUnitType.Star));

        Grid.SetRow(_cornerBox, 0);    Grid.SetColumn(_cornerBox, 0);
        Grid.SetRow(_hRulerCanvas, 0); Grid.SetColumn(_hRulerCanvas, 1);
        Grid.SetRow(_vRulerCanvas, 1); Grid.SetColumn(_vRulerCanvas, 0);

        var cell = new Grid();
        cell.Children.Add(_slideImg);
        cell.Children.Add(_gridCanvas);
        cell.Children.Add(_safeCanvas);
        cell.Children.Add(_overlay);
        Grid.SetRow(cell, 1); Grid.SetColumn(cell, 1);

        _rootGrid.Children.Add(_cornerBox);
        _rootGrid.Children.Add(_hRulerCanvas);
        _rootGrid.Children.Add(_vRulerCanvas);
        _rootGrid.Children.Add(cell);
        Content = _rootGrid;

        _overlay.PointerPressed  += OnPointerPressed;
        _overlay.PointerMoved    += OnPointerMoved;
        _overlay.PointerReleased += OnPointerReleased;
        _overlay.DoubleTapped    += OnDoubleTapped;

        _animTimer.Tick += OnAnimTick;
    }

    // ── Animation preview ─────────────────────────────────────────────────────

    public void PreviewAnimation()
    {
        _animTimer.Stop();
        _animStart = DateTime.UtcNow;
        _animTimer.Start();
    }

    void OnAnimTick(object? sender, EventArgs e)
    {
        var slide = _vm?.EditingPage;
        if (slide is null) { _animTimer.Stop(); return; }

        double elapsedMs = (DateTime.UtcNow - _animStart).TotalMilliseconds;

        // Find the total animation duration across all layers so we know when to stop.
        double maxMs = 0;
        foreach (var layer in slide.Layers)
        {
            double layerEnd = layer.EntryDelayMs + layer.EntryDurationMs;
            if (layer.HoldDurationMs > 0)
                layerEnd += layer.HoldDurationMs + layer.ExitDelayMs + (layer.ExitDurationMs > 0 ? layer.ExitDurationMs : 400);
            if (layerEnd > maxMs) maxMs = layerEnd;
        }
        if (maxMs == 0) maxMs = 1000; // nothing to animate, show for 1 s then stop

        RebuildSlideAnimated(elapsedMs);

        if (elapsedMs >= maxMs)
        {
            _animTimer.Stop();
            RebuildSlide(); // return to static render
        }
    }

    // ── DataContext ───────────────────────────────────────────────────────────

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        if (_vm is not null) _vm.SlideContentChanged -= OnSlideContentChanged;

        _vm = DataContext as MainViewModel;
        if (_vm is null) return;

        _vm.SlideContentChanged += OnSlideContentChanged;
        _subs.Add(_vm.WhenAnyValue(x => x.EditingSlide).Subscribe(_ => RebuildSlide()));
        _subs.Add(_vm.WhenAnyValue(x => x.SelectedLayer).Subscribe(_ => UpdateHandles()));
        _subs.Add(_vm.WhenAnyValue(x => x.ShowGrid)            .Subscribe(v => { _gridCanvas.IsVisible = v; RebuildGrid(); }));
        _subs.Add(_vm.WhenAnyValue(x => x.ShowSafeBoundaries)  .Subscribe(v => { _safeCanvas.IsVisible = v; RebuildSafeBoundaries(); }));
        _subs.Add(_vm.WhenAnyValue(x => x.ShowRulers)          .Subscribe(v =>
        {
            _cornerBox.IsVisible    = v;
            _hRulerCanvas.IsVisible = v;
            _vRulerCanvas.IsVisible = v;
            _rootGrid.RowDefinitions[0].Height   = v ? new GridLength(RulerSize) : new GridLength(0);
            _rootGrid.ColumnDefinitions[0].Width = v ? new GridLength(RulerSize) : new GridLength(0);
        }));
        _subs.Add(_vm.WhenAnyValue(x => x.GridSpacing).Subscribe(_ => RebuildGrid()));
    }

    protected override void OnSizeChanged(SizeChangedEventArgs e)
    {
        base.OnSizeChanged(e);
        RebuildRulers();
        RebuildGrid();
        RebuildSafeBoundaries();
        UpdateHandles();
    }

    // ── Slide rendering ───────────────────────────────────────────────────────

    void OnSlideContentChanged() { _animTimer.Stop(); RebuildSlide(); }

    void RebuildSlideAnimated(double elapsedMs)
    {
        var slide = _vm?.EditingPage;
        if (slide is null) return;

        using var surface = SKSurface.Create(new SKImageInfo(RenderW, RenderH, SKColorType.Rgba8888));
        PageRenderer.Render(surface.Canvas, slide, LayerRole.All, RenderW, RenderH, elapsedMs);
        UpdateSlideImage(surface);
    }

    void RebuildSlide()
    {
        var slide = _vm?.EditingPage;
        if (slide is null) { _slideImg.Source = null; return; }

        using var surface = SKSurface.Create(new SKImageInfo(RenderW, RenderH, SKColorType.Rgba8888));
        PageRenderer.Render(surface.Canvas, slide, LayerRole.All, RenderW, RenderH);
        UpdateSlideImage(surface);

        UpdateHandles();
        RebuildGrid();
        RebuildSafeBoundaries();
    }

    void UpdateSlideImage(SKSurface surface)
    {
        // Fast raw-pixel path — avoids PNG encode so this is safe to call every pointer-move tick
        var wb = new Avalonia.Media.Imaging.WriteableBitmap(
            new Avalonia.PixelSize(RenderW, RenderH),
            new Avalonia.Vector(96, 96),
            Avalonia.Platform.PixelFormats.Bgra8888,
            Avalonia.Platform.AlphaFormat.Premul);
        using (var fb = wb.Lock())
        {
            using var img = surface.Snapshot();
            img.ReadPixels(
                new SkiaSharp.SKImageInfo(RenderW, RenderH, SkiaSharp.SKColorType.Bgra8888, SkiaSharp.SKAlphaType.Premul),
                fb.Address, fb.RowBytes, 0, 0);
        }
        _slideImg.Source = wb;
    }

    // ── Rulers ────────────────────────────────────────────────────────────────

    void RebuildRulers()
    {
        var ir = GetImageRect();
        if (ir.Width > 0) { RebuildHRuler(ir); RebuildVRuler(ir); }
    }

    void RebuildHRuler(Rect ir)
    {
        int pw = (int)_overlay.Bounds.Width, ph = (int)RulerSize;
        if (pw <= 0) return;
        using var bmp    = new SKBitmap(pw, ph);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(30, 30, 30));
        using var tick = new SKPaint { Color = new SKColor(160, 160, 160), StrokeWidth = 1 };
        using var txt  = new SKPaint { Color = new SKColor(160, 160, 160), TextSize = 9, IsAntialias = true };
        canvas.DrawLine(0, ph - 1, pw, ph - 1, tick);
        double spacing = _vm?.GridSpacing ?? 100;
        double scale = ir.Width / 1920.0;
        for (double vx = 0; vx <= 1920; vx += spacing / 2)
        {
            double rx = ir.X + vx * scale;
            bool major = Math.Abs(vx % spacing) < 0.001;
            float th = major ? 10f : 5f;
            canvas.DrawLine((float)rx, ph - 1 - th, (float)rx, ph - 1, tick);
            if (major && vx > 0) canvas.DrawText(vx % 1 == 0 ? ((int)vx).ToString() : vx.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), (float)rx + 2, ph - 1 - th - 1, txt);
        }
        using var img  = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        _hRulerImg.Source = new Bitmap(data.AsStream());
        Canvas.SetLeft(_hRulerImg, 0); Canvas.SetTop(_hRulerImg, 0);
        _hRulerImg.Width = pw; _hRulerImg.Height = ph;
    }

    void RebuildVRuler(Rect ir)
    {
        int ph = (int)_overlay.Bounds.Height, pw = (int)RulerSize;
        if (ph <= 0) return;
        using var bmp    = new SKBitmap(pw, ph);
        using var canvas = new SKCanvas(bmp);
        canvas.Clear(new SKColor(30, 30, 30));
        using var tick = new SKPaint { Color = new SKColor(160, 160, 160), StrokeWidth = 1 };
        using var txt  = new SKPaint { Color = new SKColor(160, 160, 160), TextSize = 9, IsAntialias = true };
        canvas.DrawLine(pw - 1, 0, pw - 1, ph, tick);
        double spacing = _vm?.GridSpacing ?? 100;
        double scale = ir.Height / 1080.0;
        for (double vy = 0; vy <= 1080; vy += spacing / 2)
        {
            double ry = ir.Y + vy * scale;
            bool major = Math.Abs(vy % spacing) < 0.001;
            float tw = major ? 10f : 5f;
            canvas.DrawLine(pw - 1 - tw, (float)ry, pw - 1, (float)ry, tick);
            if (major && vy > 0)
            {
                canvas.Save();
                canvas.RotateDegrees(-90, pw - 1 - tw - 1, (float)ry);
                canvas.DrawText(vy % 1 == 0 ? ((int)vy).ToString() : vy.ToString("0.#", System.Globalization.CultureInfo.InvariantCulture), pw - 1 - tw - 1, (float)ry - 1, txt);
                canvas.Restore();
            }
        }
        using var img  = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 90);
        _vRulerImg.Source = new Bitmap(data.AsStream());
        Canvas.SetLeft(_vRulerImg, 0); Canvas.SetTop(_vRulerImg, 0);
        _vRulerImg.Width = pw; _vRulerImg.Height = ph;
    }

    // ── Grid ──────────────────────────────────────────────────────────────────

    void RebuildGrid()
    {
        _gridCanvas.Children.Clear();
        if (_vm?.ShowGrid != true) return;
        var ir = GetImageRect();
        if (ir.Width <= 0) return;
        double spacing = _vm?.GridSpacing ?? 100;
        var brush   = new SolidColorBrush(Color.FromArgb(60, 120, 120, 200));
        for (double vx = spacing; vx < 1920; vx += spacing)
        {
            double rx = ir.X + vx * (ir.Width / 1920.0);
            _gridCanvas.Children.Add(new Line { StartPoint = new Point(rx, ir.Y), EndPoint = new Point(rx, ir.Y + ir.Height), Stroke = brush, StrokeThickness = 0.5 });
        }
        for (double vy = spacing; vy < 1080; vy += spacing)
        {
            double ry = ir.Y + vy * (ir.Height / 1080.0);
            _gridCanvas.Children.Add(new Line { StartPoint = new Point(ir.X, ry), EndPoint = new Point(ir.X + ir.Width, ry), Stroke = brush, StrokeThickness = 0.5 });
        }
    }

    // ── Broadcast safe boundaries ─────────────────────────────────────────────

    void RebuildSafeBoundaries()
    {
        _safeCanvas.Children.Clear();
        if (_vm?.ShowSafeBoundaries != true) return;
        var ir = GetImageRect();
        if (ir.Width <= 0) return;

        // Action safe: 5% inset — amber dashed
        AddSafeRect(ir, 0.05, Color.FromArgb(200, 255, 165, 0), "Action");
        // Title safe: 10% inset — red dashed
        AddSafeRect(ir, 0.10, Color.FromArgb(200, 220, 60, 60), "Title");
    }

    void AddSafeRect(Rect ir, double inset, Color color, string label)
    {
        double x = ir.X + ir.Width  * inset;
        double y = ir.Y + ir.Height * inset;
        double w = ir.Width  * (1 - 2 * inset);
        double h = ir.Height * (1 - 2 * inset);

        var brush = new SolidColorBrush(color);

        var rect = new Rectangle
        {
            Width            = w,
            Height           = h,
            Stroke           = brush,
            StrokeThickness  = 1,
            StrokeDashArray  = new Avalonia.Collections.AvaloniaList<double> { 8, 4 },
            Fill             = Brushes.Transparent,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(rect, x);
        Canvas.SetTop(rect, y);
        _safeCanvas.Children.Add(rect);

        // Small label tag in the top-left corner of each zone
        var tag = new TextBlock
        {
            Text       = label,
            Foreground = brush,
            FontSize   = 9,
            Opacity    = 0.85,
            IsHitTestVisible = false
        };
        Canvas.SetLeft(tag, x + 3);
        Canvas.SetTop(tag, y + 2);
        _safeCanvas.Children.Add(tag);
    }

    // ── Ruler pointer lines ───────────────────────────────────────────────────

    void UpdateRulerPointers(Point pt)
    {
        if (_vm?.ShowRulers != true) { _hRulerLine.IsVisible = false; _vRulerLine.IsVisible = false; return; }
        _hRulerLine.StartPoint = new Point(pt.X, 0); _hRulerLine.EndPoint = new Point(pt.X, RulerSize); _hRulerLine.IsVisible = true;
        _vRulerLine.StartPoint = new Point(0, pt.Y); _vRulerLine.EndPoint = new Point(RulerSize, pt.Y); _vRulerLine.IsVisible = true;
        Canvas.SetLeft(_hRulerLine, 0); Canvas.SetTop(_hRulerLine, 0);
        Canvas.SetLeft(_vRulerLine, 0); Canvas.SetTop(_vRulerLine, 0);
    }

    // ── Move crosshairs ───────────────────────────────────────────────────────

    void UpdateCrosshairs()
    {
        var layer = _vm?.SelectedLayer;
        if (layer is null) { HideCrosshairs(); return; }
        var    ir = GetImageRect();
        double cx = ir.X + (layer.X + layer.Width  / 2f) * ir.Width;
        double cy = ir.Y + (layer.Y + layer.Height / 2f) * ir.Height;
        double cw = _overlay.Bounds.Width;
        double ch = _overlay.Bounds.Height;

        _crossH.StartPoint = new Point(0,  cy); _crossH.EndPoint = new Point(cw, cy);
        _crossV.StartPoint = new Point(cx, 0);  _crossV.EndPoint = new Point(cx, ch);
        _crossH.IsVisible  = true;
        _crossV.IsVisible  = true;
    }

    void HideCrosshairs()
    {
        _crossH.IsVisible = false;
        _crossV.IsVisible = false;
    }

    // ── Handle overlay ────────────────────────────────────────────────────────

    void UpdateHandles()
    {
        // Remove previous per-layer bounding boxes
        foreach (var r in _layerBounds) _overlay.Children.Remove(r);
        _layerBounds.Clear();

        var slide = _vm?.EditingPage;
        var sel   = _vm?.SelectedLayer;

        if (slide is not null && _overlay.Bounds.Width > 0)
        {
            var ir = GetImageRect();
            foreach (var layer in slide.Layers)
            {
                if (layer == sel) continue;
                double x = ir.X + layer.X * ir.Width;
                double y = ir.Y + layer.Y * ir.Height;
                double w = layer.Width  * ir.Width;
                double h = layer.Height * ir.Height;
                var box = new Rectangle
                {
                    Stroke           = new SolidColorBrush(Color.FromArgb(100, 120, 120, 160)),
                    StrokeThickness  = 0.75,
                    StrokeDashArray  = new Avalonia.Collections.AvaloniaList<double> { 4, 3 },
                    Fill             = Brushes.Transparent,
                    IsHitTestVisible = false
                };
                Canvas.SetLeft(box, x); Canvas.SetTop(box, y);
                box.Width = w; box.Height = h;
                _overlay.Children.Insert(0, box);
                _layerBounds.Add(box);
            }
        }

        if (sel is null || _overlay.Bounds.Width <= 0)
        {
            _selBorder.IsVisible = false;
            _rotHandle.IsVisible = false; _rotHandleLine.IsVisible = false;
            foreach (var hnd in _handles) hnd.IsVisible = false;
            return;
        }

        var    irSel = GetImageRect();
        double sx  = irSel.X + sel.X * irSel.Width;
        double sy  = irSel.Y + sel.Y * irSel.Height;
        double sw  = sel.Width  * irSel.Width;
        double sh  = sel.Height * irSel.Height;

        Canvas.SetLeft(_selBorder, sx); Canvas.SetTop(_selBorder, sy);
        _selBorder.Width = sw; _selBorder.Height = sh; _selBorder.IsVisible = true;

        // Resize handles: NW N NE W E SW S SE
        double[] hx = { sx - HandleHalf, sx + sw/2 - HandleHalf, sx + sw - HandleHalf,
                         sx - HandleHalf,                          sx + sw - HandleHalf,
                         sx - HandleHalf, sx + sw/2 - HandleHalf,  sx + sw - HandleHalf };
        double[] hy = { sy - HandleHalf, sy - HandleHalf,           sy - HandleHalf,
                         sy + sh/2 - HandleHalf,                   sy + sh/2 - HandleHalf,
                         sy + sh - HandleHalf, sy + sh - HandleHalf, sy + sh - HandleHalf };
        for (int i = 0; i < 8; i++)
        {
            Canvas.SetLeft(_handles[i], hx[i]);
            Canvas.SetTop (_handles[i], hy[i]);
            _handles[i].IsVisible = true;
        }

        // Rotation handle (red circle above N)
        double rhx = sx + sw/2 - RotHandHalf;
        double rhy = sy - RotHandDist - RotHandHalf;
        Canvas.SetLeft(_rotHandle, rhx); Canvas.SetTop(_rotHandle, rhy);
        _rotHandle.IsVisible = true;
        _rotHandleLine.StartPoint = new Point(sx + sw/2, sy);
        _rotHandleLine.EndPoint   = new Point(sx + sw/2, rhy + RotHandHalf);
        _rotHandleLine.IsVisible  = true;
    }

    // ── Coordinate helpers ────────────────────────────────────────────────────

    Rect GetImageRect()
    {
        double cw = _overlay.Bounds.Width, ch = _overlay.Bounds.Height;
        if (cw <= 0 || ch <= 0) return new Rect(0, 0, cw, ch);
        const double aspect = 16.0 / 9.0;
        double iw, ih;
        if (cw / ch > aspect) { ih = ch; iw = ih * aspect; }
        else                  { iw = cw; ih = iw / aspect; }
        return new Rect((cw - iw) / 2, (ch - ih) / 2, iw, ih);
    }

    (float nx, float ny) ToNorm(Point pt)
    {
        var ir = GetImageRect();
        return ((float)((pt.X - ir.X) / ir.Width), (float)((pt.Y - ir.Y) / ir.Height));
    }

    // ── Snap helper ───────────────────────────────────────────────────────────

    float SnapX(float v)
    {
        if (_vm?.SnapToGrid != true || _vm.GridSpacing <= 0) return v;
        float step = (float)(_vm.GridSpacing / 1920.0);
        return (float)Math.Round(v / step) * step;
    }

    float SnapY(float v)
    {
        if (_vm?.SnapToGrid != true || _vm.GridSpacing <= 0) return v;
        float step = (float)(_vm.GridSpacing / 1080.0);
        return (float)Math.Round(v / step) * step;
    }

    // ── Pointer events ────────────────────────────────────────────────────────

    void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (_vm is null) return;
        if (_inlineBox is not null) { CommitInlineEdit(); return; }
        var pt = e.GetPosition(_overlay);

        // 1. Rotation handle
        if (_rotHandle.IsVisible && _vm.SelectedLayer is not null)
        {
            double rhx = Canvas.GetLeft(_rotHandle), rhy = Canvas.GetTop(_rotHandle);
            if (pt.X >= rhx && pt.X <= rhx + RotHandSize && pt.Y >= rhy && pt.Y <= rhy + RotHandSize)
            {
                StartDrag(HandleKind.Rotate, pt);
                e.Pointer.Capture(_overlay);
                e.Handled = true;
                return;
            }
        }

        // 2. Resize handles
        var handle = HitTestHandle(pt);
        if (handle != HandleKind.None && _vm.SelectedLayer is not null)
        {
            StartDrag(handle, pt);
            e.Pointer.Capture(_overlay);
            e.Handled = true;
            return;
        }

        // 3. Move (inside selection)
        var sel = _vm.SelectedLayer;
        if (sel is not null)
        {
            var ir = GetImageRect();
            var r  = new Rect(ir.X + sel.X * ir.Width, ir.Y + sel.Y * ir.Height, sel.Width * ir.Width, sel.Height * ir.Height);
            if (r.Contains(pt))
            {
                StartDrag(HandleKind.Move, pt);
                e.Pointer.Capture(_overlay);
                e.Handled = true;
                return;
            }
        }

        // 4. Click to select layer
        var (nx, ny) = ToNorm(pt);
        SlideLayer? hit = null;
        if (_vm.EditingSlide is { } slide)
        {
            foreach (var l in slide.Layers.OrderByDescending(l => l.ZOrder))
            {
                if (!l.Locked && nx >= l.X && nx <= l.X + l.Width && ny >= l.Y && ny <= l.Y + l.Height)
                { hit = l; break; }
            }
        }
        _vm.SelectedLayer = hit;
        e.Handled = true;
    }

    void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var pt = e.GetPosition(_overlay);
        UpdateRulerPointers(pt);

        if (!_dragging || _vm?.SelectedLayer is not { } layer) return;
        var ir = GetImageRect();
        float dx = (float)((pt.X - _dragOrigin.X) / ir.Width);
        float dy = (float)((pt.Y - _dragOrigin.Y) / ir.Height);

        switch (_dragKind)
        {
            case HandleKind.Rotate:
                float cxR = (float)(ir.X + (_origX + _origW / 2) * ir.Width);
                float cyR = (float)(ir.Y + (_origY + _origH / 2) * ir.Height);
                double angle = Math.Atan2(pt.Y - cyR, pt.X - cxR) * 180.0 / Math.PI;
                layer.RotationDegrees = (float)(_rotDragOrigDeg + angle - _rotDragAngle0);
                break;
            case HandleKind.Move:
                layer.X = Math.Clamp(SnapX(_origX + dx), 0f, Math.Max(0f, 1f - layer.Width));
                layer.Y = Math.Clamp(SnapY(_origY + dy), 0f, Math.Max(0f, 1f - layer.Height));
                break;
            case HandleKind.SE:
                layer.Width  = Math.Max(0.05f, _origW + dx);
                layer.Height = Math.Max(0.05f, _origH + dy);
                break;
            case HandleKind.SW:
                float swW = Math.Max(0.05f, _origW - dx);
                layer.X = _origX + (_origW - swW); layer.Width = swW;
                layer.Height = Math.Max(0.05f, _origH + dy);
                break;
            case HandleKind.NE:
                layer.Width = Math.Max(0.05f, _origW + dx);
                float neH = Math.Max(0.05f, _origH - dy);
                layer.Y = _origY + (_origH - neH); layer.Height = neH;
                break;
            case HandleKind.NW:
                float nwW = Math.Max(0.05f, _origW - dx);
                float nwH = Math.Max(0.05f, _origH - dy);
                layer.X = _origX + (_origW - nwW); layer.Y = _origY + (_origH - nwH);
                layer.Width = nwW; layer.Height = nwH;
                break;
            case HandleKind.N:
                float nH = Math.Max(0.05f, _origH - dy);
                layer.Y = _origY + (_origH - nH); layer.Height = nH;
                break;
            case HandleKind.S:
                layer.Height = Math.Max(0.05f, _origH + dy);
                break;
            case HandleKind.W:
                float wW = Math.Max(0.05f, _origW - dx);
                layer.X = _origX + (_origW - wW); layer.Width = wW;
                break;
            case HandleKind.E:
                layer.Width = Math.Max(0.05f, _origW + dx);
                break;
        }

        RebuildSlide(); // updates handles, grid, and slide image in one shot
        if (_dragKind == HandleKind.Move)
            UpdateCrosshairs();
        else
            HideCrosshairs();
        e.Handled = true;
    }

    void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_dragging)
        {
            _dragging = false;
            HideCrosshairs();
            e.Pointer.Capture(null);
            RebuildSlide();
            e.Handled = true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void StartDrag(HandleKind kind, Point pt)
    {
        _vm?.BeginLayerEdit();
        _dragging   = true;
        _dragKind   = kind;
        _dragOrigin = pt;
        var l = _vm!.SelectedLayer!;
        _origX = l.X; _origY = l.Y; _origW = l.Width; _origH = l.Height;

        if (kind == HandleKind.Rotate)
        {
            var ir   = GetImageRect();
            float cx = (float)(ir.X + (_origX + _origW / 2) * ir.Width);
            float cy = (float)(ir.Y + (_origY + _origH / 2) * ir.Height);
            _rotDragAngle0  = Math.Atan2(pt.Y - cy, pt.X - cx) * 180.0 / Math.PI;
            _rotDragOrigDeg = l.RotationDegrees;
        }
    }

    HandleKind HitTestHandle(Point pt)
    {
        for (int i = 0; i < 8; i++)
        {
            if (!_handles[i].IsVisible) continue;
            double hx = Canvas.GetLeft(_handles[i]), hy = Canvas.GetTop(_handles[i]);
            if (pt.X >= hx && pt.X <= hx + HandleSize && pt.Y >= hy && pt.Y <= hy + HandleSize)
                return (HandleKind)i;
        }
        return HandleKind.None;
    }

    // ── Inline text editing ───────────────────────────────────────────────────

    void OnDoubleTapped(object? sender, TappedEventArgs e)
    {
        if (_vm is null) return;
        var pt = e.GetPosition(_overlay);
        var (nx, ny) = ToNorm(pt);

        SlideLayer? hit = null;
        if (_vm.EditingSlide is { } slide)
        {
            foreach (var l in slide.Layers.OrderByDescending(l => l.ZOrder))
            {
                if (!l.Locked && l.Type == LayerType.Text &&
                    nx >= l.X && nx <= l.X + l.Width && ny >= l.Y && ny <= l.Y + l.Height)
                { hit = l; break; }
            }
        }
        if (hit is null) return;

        _vm.SelectedLayer = hit;
        BeginInlineEdit(hit);
        e.Handled = true;
    }

    void BeginInlineEdit(SlideLayer layer)
    {
        CommitInlineEdit();

        _inlineLayer = layer;
        _vm?.BeginLayerEdit();

        var ir = GetImageRect();
        double x  = ir.X + layer.X * ir.Width;
        double y  = ir.Y + layer.Y * ir.Height;
        double w  = layer.Width  * ir.Width;
        double h  = layer.Height * ir.Height;
        double fs = Math.Max(8, layer.FontSize * ir.Height);

        var box = new TextBox
        {
            Text             = layer.Text,
            AcceptsReturn    = true,
            TextWrapping     = TextWrapping.Wrap,
            Width            = w,
            Height           = h,
            FontSize         = fs,
            FontFamily       = string.IsNullOrEmpty(layer.FontFamily)
                                   ? FontFamily.Default
                                   : new FontFamily(layer.FontFamily),
            Background       = new SolidColorBrush(Color.FromArgb(200, 15, 15, 15)),
            Foreground       = Brushes.White,
            CaretBrush       = Brushes.White,
            SelectionBrush   = new SolidColorBrush(Color.FromArgb(120, 59, 130, 246)),
            BorderBrush      = new SolidColorBrush(Color.FromRgb(59, 130, 246)),
            BorderThickness  = new Thickness(2),
            Padding          = new Thickness(4),
            VerticalAlignment        = Avalonia.Layout.VerticalAlignment.Top,
            VerticalContentAlignment = layer.TextVAlign switch
            {
                TextVAlign.Bottom => Avalonia.Layout.VerticalAlignment.Bottom,
                TextVAlign.Middle => Avalonia.Layout.VerticalAlignment.Center,
                _                 => Avalonia.Layout.VerticalAlignment.Top,
            },
            TextAlignment    = layer.TextHAlign switch
            {
                TextHAlign.Left  => TextAlignment.Left,
                TextHAlign.Right => TextAlignment.Right,
                _                => TextAlignment.Center,
            },
        };

        Canvas.SetLeft(box, x);
        Canvas.SetTop(box, y);

        box.KeyDown   += OnInlineKeyDown;
        box.LostFocus += OnInlineLostFocus;

        _overlay.Children.Add(box);
        _inlineBox = box;

        Dispatcher.UIThread.Post(() => { box.Focus(); box.SelectAll(); });
    }

    void OnInlineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            CancelInlineEdit();
            e.Handled = true;
        }
    }

    void OnInlineLostFocus(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        CommitInlineEdit();
    }

    void CommitInlineEdit()
    {
        if (_inlineCommitting || _inlineBox is null || _inlineLayer is null) return;
        _inlineCommitting = true;
        string text = _inlineBox.Text ?? string.Empty;
        RemoveInlineBox();
        _inlineLayer.Text = text;
        _vm?.NotifySlideChanged();
        RebuildSlide();
        _inlineLayer      = null;
        _inlineCommitting = false;
    }

    void CancelInlineEdit()
    {
        if (_inlineCommitting) return;
        _inlineCommitting = true;
        RemoveInlineBox();
        _inlineLayer      = null;
        _inlineCommitting = false;
        RebuildSlide();
    }

    void RemoveInlineBox()
    {
        if (_inlineBox is null) return;
        _inlineBox.KeyDown   -= OnInlineKeyDown;
        _inlineBox.LostFocus -= OnInlineLostFocus;
        _overlay.Children.Remove(_inlineBox);
        _inlineBox = null;
    }

    public void Dispose()
    {
        _animTimer.Stop();
        CommitInlineEdit();
        foreach (var s in _subs) s.Dispose();
        if (_vm is not null) _vm.SlideContentChanged -= OnSlideContentChanged;
    }
}
