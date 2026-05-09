using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ShowCast.Core;   // TextHAlign etc. not used here, but namespace keeps things tidy
using SkiaSharp;

namespace ShowCast.Views;

/// <summary>
/// Compact color picker: swatch trigger + hex text box.
/// Click the swatch to open a popup with Standard palette (Electron-style)
/// and Advanced HSV picker (GIMP/Inkscape-style gradient square + hue strip).
/// </summary>
public class ColorPickerField : UserControl
{
    // ── Palette (ported from Electron ColorPicker.tsx) ────────────────────────
    static readonly string[] Palette =
    {
        "#000000","#696969","#808080","#a9a9a9","#c0c0c0","#d3d3d3","#dcdcdc","#f5f5f5","#ffffff","#bc8f8f","#cd5c5c",
        "#a52a2a","#b22222","#f08080","#800000","#8b0000","#ff0000","#fffafa","#ffe4e1","#fa8072","#ff6347","#e9967a",
        "#ff7f50","#ff4500","#ffa07a","#a0522d","#fff5ee","#d2691e","#8b4513","#f4a460","#ffdab9","#cd853f","#faf0e6",
        "#ffe4c4","#ff8c00","#deb887","#d2b48c","#faebd7","#ffdead","#ffebcd","#ffefd5","#ffe4b5","#ffe4b5","#ffe4b5",
        "#fdf5e6","#fffaf0","#b8860b","#daa520","#fff8dc","#ffd700","#ffd700","#fffacd","#eee8aa","#bdb76b","#f5f5dc",
        "#fafad2","#808000","#ffff00","#ffffe0","#fffff0","#6b8e23","#9acd32","#556b2f","#adff2f","#7fff00","#7cfc00",
        "#8fbc8f","#228b22","#32cd32","#90ee90","#98fb98","#006400","#008000","#008000","#f0fff0","#2e8b57","#3cb371",
        "#008b8b","#00ffff","#05e8e8","#e0ffff","#f0ffff","#00ced1","#5f9ea0","#b0e0e6","#add8e6","#00bfff","#87ceeb",
        "#87cefa","#4682b4","#f0f8ff","#1e90ff","#708090","#778899","#b0c4de","#6495ed","#4169e1","#191970","#e6e6fa",
        "#000080","#00008b","#0000cd","#0000ff","#f8f8ff","#6a5acd","#483d8b","#7b68ee","#9370db","#8a2be2","#4b0082",
        "#9932cc","#9400d3","#ba55d3","#d8bfd8","#dda0dd","#ee82ee","#800080","#8b008b","#ff00ff","#ff00ff","#da70d6",
        "#c71585","#ff1493","#ff69b4","#fff0f5","#db7093","#dc143c","#ffc0cb","#ffb6c1",
    };

    static readonly string[] StandardColors =
    {
        "#ffffff","#808080","#000000",
        "#ff0000","#008000","#0000ff","#ffff00","#ffa500","#800080","#00ffff",
    };

    // ── Dimensions ────────────────────────────────────────────────────────────
    const int GW = 200, GH = 148;   // gradient square
    const int HW = 200, HH = 14;    // hue strip

    // ── HSV state ─────────────────────────────────────────────────────────────
    float _h, _s = 0, _v = 100;     // H 0-360, S 0-100, V 0-100

    // ── UI refs ───────────────────────────────────────────────────────────────
    Border    _swatchBorder = null!;
    TextBox   _hexBox       = null!;
    Popup     _popup        = null!;
    StackPanel _stdContent  = null!;
    StackPanel _advContent  = null!;
    Image     _gradImg      = null!;
    Canvas    _gradOverlay  = null!;
    Ellipse   _gradCursor   = null!;
    Image     _hueImg       = null!;
    Canvas    _hueOverlay   = null!;
    Rectangle _hueCursor    = null!;
    Border    _advPreview   = null!;
    TextBox   _advHexBox    = null!;
    Button    _stdTabBtn    = null!;
    Button    _advTabBtn    = null!;
    bool      _onAdvTab;
    bool      _gradDrag, _hueDrag;

    // ── Public API ────────────────────────────────────────────────────────────
    SKColor _value = SKColors.White;
    public SKColor Value
    {
        get => _value;
        set { _value = value; ApplySkColor(value, fireEvent: false); }
    }
    public event Action<SKColor>? ColorChanged;

    public ColorPickerField()
    {
        BuildUI();
        ApplySkColor(SKColors.White, fireEvent: false);
    }

    // ── UI construction ───────────────────────────────────────────────────────

    void BuildUI()
    {
        // Swatch button
        _swatchBorder = new Border
        {
            Width  = 24, Height = 24,
            CornerRadius  = new CornerRadius(3),
            BorderBrush   = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Thickness(1),
            Background    = Brushes.White
        };
        var swatchBtn = new Button
        {
            Content = _swatchBorder,
            Padding = new Thickness(2),
            Width = 30, Height = 30,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            Cursor          = new Cursor(StandardCursorType.Hand),
            VerticalContentAlignment   = VerticalAlignment.Center,
            HorizontalContentAlignment = HorizontalAlignment.Center
        };
        swatchBtn.Click += OnSwatchClick;

        // Hex text box
        _hexBox = new TextBox
        {
            Text       = "#FFFFFF",
            Background = new SolidColorBrush(Color.Parse("#2a2a2a")),
            Foreground = Brushes.White,
            BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
            FontSize   = 12, Height = 30,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(4, 0, 0, 0)
        };
        _hexBox.LostFocus += OnHexLostFocus;
        _hexBox.KeyDown   += OnHexKeyDown;

        // Trigger row
        var row = new Grid();
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        row.Children.Add(swatchBtn);
        Grid.SetColumn(_hexBox, 1);
        row.Children.Add(_hexBox);

        // Build popup (before adding to tree so PlacementTarget is set)
        BuildPopup(swatchBtn);

        var root = new Grid();
        root.Children.Add(row);
        root.Children.Add(_popup);
        Content = root;
    }

    void BuildPopup(Button anchor)
    {
        // Tab buttons
        _stdTabBtn = MakeTabBtn("Standard");
        _advTabBtn = MakeTabBtn("Advanced");
        _stdTabBtn.Click += (_, _) => ShowTab(false);
        _advTabBtn.Click += (_, _) => ShowTab(true);
        SetTabActive(_stdTabBtn, true);
        SetTabActive(_advTabBtn, false);

        var tabRow = new Grid { Margin = new Thickness(0, 0, 0, 8) };
        tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        tabRow.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        tabRow.Children.Add(_stdTabBtn);
        Grid.SetColumn(_advTabBtn, 1);
        tabRow.Children.Add(_advTabBtn);

        _stdContent = BuildStandardTab();
        _advContent = BuildAdvancedTab();
        _advContent.IsVisible = false;

        var body = new StackPanel();
        body.Children.Add(tabRow);
        body.Children.Add(_stdContent);
        body.Children.Add(_advContent);

        var frame = new Border
        {
            Background      = new SolidColorBrush(Color.Parse("#252525")),
            BorderBrush     = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Thickness(1),
            CornerRadius    = new CornerRadius(6),
            Padding         = new Thickness(10),
            Width           = 244,
            Child           = body
        };

        _popup = new Popup
        {
            PlacementTarget       = anchor,
            Placement             = PlacementMode.Bottom,
            IsLightDismissEnabled = true,
            Child                 = frame
        };
    }

    StackPanel BuildStandardTab()
    {
        var panel = new StackPanel { Spacing = 4 };

        panel.Children.Add(PaletteLabel("Available Colors"));

        var grid = new WrapPanel();
        foreach (var hex in Palette)
            grid.Children.Add(MakeSwatch(hex, 18, () => { ApplyHex(hex); _popup.IsOpen = false; }));
        panel.Children.Add(grid);

        panel.Children.Add(PaletteLabel("Standard Colors"));

        var stdRow = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 3 };
        foreach (var hex in StandardColors)
            stdRow.Children.Add(MakeSwatch(hex, 20, () => { ApplyHex(hex); _popup.IsOpen = false; }));
        panel.Children.Add(stdRow);

        return panel;
    }

    StackPanel BuildAdvancedTab()
    {
        var panel = new StackPanel { Spacing = 6 };

        // ── Gradient square (GIMP-style: S on X, V on Y) ──────────────────
        _gradImg = new Image { Width = GW, Height = GH, Stretch = Stretch.Fill, IsHitTestVisible = false };
        _gradCursor = new Ellipse
        {
            Width = 10, Height = 10,
            Stroke = Brushes.White, StrokeThickness = 2,
            Fill = Brushes.Transparent, IsHitTestVisible = false
        };
        _gradOverlay = new Canvas { Width = GW, Height = GH, Background = Brushes.Transparent };
        _gradOverlay.Children.Add(_gradCursor);
        _gradOverlay.PointerPressed  += OnGradPressed;
        _gradOverlay.PointerMoved    += OnGradMoved;
        _gradOverlay.PointerReleased += OnGradReleased;

        var gradGrid = new Grid { Width = GW, Height = GH };
        gradGrid.Children.Add(_gradImg);
        gradGrid.Children.Add(_gradOverlay);
        panel.Children.Add(gradGrid);

        // ── Hue strip (Inkscape-style rainbow bar) ─────────────────────────
        _hueImg = new Image { Width = HW, Height = HH, Stretch = Stretch.Fill, IsHitTestVisible = false };
        _hueCursor = new Rectangle
        {
            Width = 4, Height = HH + 4,
            Fill = Brushes.White,
            Stroke = new SolidColorBrush(Color.Parse("#333333")),
            StrokeThickness = 1, IsHitTestVisible = false
        };
        _hueOverlay = new Canvas { Width = HW, Height = HH, Background = Brushes.Transparent };
        _hueOverlay.Children.Add(_hueCursor);
        _hueOverlay.PointerPressed  += OnHuePressed;
        _hueOverlay.PointerMoved    += OnHueMoved;
        _hueOverlay.PointerReleased += OnHueReleased;

        var hueGrid = new Grid { Width = HW, Height = HH };
        hueGrid.Children.Add(_hueImg);
        hueGrid.Children.Add(_hueOverlay);
        panel.Children.Add(hueGrid);

        // ── Preview + hex ──────────────────────────────────────────────────
        _advPreview = new Border
        {
            Width = 28, Height = 28, CornerRadius = new CornerRadius(3),
            BorderBrush = new SolidColorBrush(Color.Parse("#555555")),
            BorderThickness = new Thickness(1)
        };
        _advHexBox = new TextBox
        {
            Background  = new SolidColorBrush(Color.Parse("#2a2a2a")),
            Foreground  = Brushes.White, FontSize = 12, Height = 28,
            VerticalContentAlignment = VerticalAlignment.Center,
            Margin = new Thickness(6, 0, 0, 0)
        };
        _advHexBox.LostFocus += OnAdvHexLostFocus;
        _advHexBox.KeyDown   += OnAdvHexKeyDown;

        var footer = new Grid();
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(34) });
        footer.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Star });
        footer.Children.Add(_advPreview);
        Grid.SetColumn(_advHexBox, 1);
        footer.Children.Add(_advHexBox);
        panel.Children.Add(footer);

        return panel;
    }

    // ── Color application ─────────────────────────────────────────────────────

    void ApplyHex(string hex)
    {
        var color = ParseHex(hex);
        RgbToHsv(color, out float h, out float s, out float v);
        SetHsv(h, s, v, fireEvent: true);
    }

    void ApplySkColor(SKColor color, bool fireEvent = true)
    {
        RgbToHsv(color, out float h, out float s, out float v);
        SetHsv(h, s, v, fireEvent: fireEvent);
    }

    void SetHsv(float h, float s, float v, bool rebuildGrad = true, bool fireEvent = true)
    {
        bool hueChanged = Math.Abs(h - _h) > 0.5f;
        _h = Math.Clamp(h, 0, 360);
        _s = Math.Clamp(s, 0, 100);
        _v = Math.Clamp(v, 0, 100);

        var skColor  = SKColor.FromHsv(_h, _s, _v);
        _value       = skColor;
        var avColor  = Color.FromRgb(skColor.Red, skColor.Green, skColor.Blue);
        var brush    = new SolidColorBrush(avColor);
        var hex      = $"#{skColor.Red:X2}{skColor.Green:X2}{skColor.Blue:X2}";

        _swatchBorder.Background = brush;
        _hexBox.Text             = hex;
        if (_advPreview  is not null) _advPreview.Background = brush;
        if (_advHexBox   is not null) _advHexBox.Text        = hex;

        if (_onAdvTab)
        {
            if (rebuildGrad && (hueChanged || _gradImg.Source is null))
                RebuildGradient();
            UpdateCursors();
        }

        if (fireEvent) ColorChanged?.Invoke(skColor);
    }

    // ── Gradient / hue strip rendering ───────────────────────────────────────

    void RebuildGradient()
    {
        using var bmp    = new SKBitmap(GW, GH);
        using var canvas = new SKCanvas(bmp);

        canvas.Clear(SKColor.FromHsv(_h, 100, 100));

        using var wp = new SKPaint();
        wp.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(GW, 0),
            new[] { SKColors.White, new SKColor(255, 255, 255, 0) },
            SKShaderTileMode.Clamp);
        canvas.DrawRect(SKRect.Create(GW, GH), wp);

        using var bp = new SKPaint();
        bp.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(0, GH),
            new[] { new SKColor(0, 0, 0, 0), SKColors.Black },
            SKShaderTileMode.Clamp);
        canvas.DrawRect(SKRect.Create(GW, GH), bp);

        using var img  = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        _gradImg.Source = new Bitmap(data.AsStream());
    }

    void RebuildHueStrip()
    {
        using var bmp    = new SKBitmap(HW, HH);
        using var canvas = new SKCanvas(bmp);
        var hueColors    = new SKColor[7];
        for (int i = 0; i < 7; i++) hueColors[i] = SKColor.FromHsv(i * 60f, 100, 100);

        using var paint = new SKPaint();
        paint.Shader = SKShader.CreateLinearGradient(
            new SKPoint(0, 0), new SKPoint(HW, 0),
            hueColors, SKShaderTileMode.Clamp);
        canvas.DrawRect(SKRect.Create(HW, HH), paint);

        using var img  = SKImage.FromBitmap(bmp);
        using var data = img.Encode(SKEncodedImageFormat.Png, 100);
        _hueImg.Source = new Bitmap(data.AsStream());
    }

    void UpdateCursors()
    {
        Canvas.SetLeft(_gradCursor, _s / 100f * GW - 5);
        Canvas.SetTop (_gradCursor, (1f - _v / 100f) * GH - 5);
        Canvas.SetLeft(_hueCursor,  _h / 360f * HW - 2);
        Canvas.SetTop (_hueCursor,  -2);
    }

    // ── Pointer events: gradient square ──────────────────────────────────────

    void OnGradPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_gradOverlay).Properties.IsLeftButtonPressed) return;
        _gradDrag = true;
        e.Pointer.Capture(_gradOverlay);
        var pt = e.GetPosition(_gradOverlay);
        SetHsv(_h, (float)(pt.X / GW * 100), (float)((1 - pt.Y / GH) * 100), rebuildGrad: false);
        e.Handled = true;
    }

    void OnGradMoved(object? sender, PointerEventArgs e)
    {
        if (!_gradDrag) return;
        var pt = e.GetPosition(_gradOverlay);
        SetHsv(_h,
            (float)Math.Clamp(pt.X / GW * 100, 0, 100),
            (float)Math.Clamp((1 - pt.Y / GH) * 100, 0, 100),
            rebuildGrad: false);
        e.Handled = true;
    }

    void OnGradReleased(object? sender, PointerReleasedEventArgs e)
    {
        _gradDrag = false;
        e.Pointer.Capture(null);
    }

    // ── Pointer events: hue strip ─────────────────────────────────────────────

    void OnHuePressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(_hueOverlay).Properties.IsLeftButtonPressed) return;
        _hueDrag = true;
        e.Pointer.Capture(_hueOverlay);
        var pt = e.GetPosition(_hueOverlay);
        SetHsv((float)(pt.X / HW * 360), _s, _v);
        e.Handled = true;
    }

    void OnHueMoved(object? sender, PointerEventArgs e)
    {
        if (!_hueDrag) return;
        var pt = e.GetPosition(_hueOverlay);
        SetHsv((float)Math.Clamp(pt.X / HW * 360, 0, 360), _s, _v);
        e.Handled = true;
    }

    void OnHueReleased(object? sender, PointerReleasedEventArgs e)
    {
        _hueDrag = false;
        e.Pointer.Capture(null);
    }

    // ── Hex input ─────────────────────────────────────────────────────────────

    void OnHexLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_hexBox.Text))
            ApplyHex(_hexBox.Text);
    }

    void OnHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyHex(_hexBox.Text ?? ""); e.Handled = true; }
    }

    void OnAdvHexLostFocus(object? sender, RoutedEventArgs e)
    {
        if (!string.IsNullOrWhiteSpace(_advHexBox.Text))
            ApplyHex(_advHexBox.Text);
    }

    void OnAdvHexKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { ApplyHex(_advHexBox.Text ?? ""); e.Handled = true; }
    }

    // ── Swatch button / tabs ──────────────────────────────────────────────────

    void OnSwatchClick(object? sender, RoutedEventArgs e)
    {
        if (_popup.IsOpen) { _popup.IsOpen = false; return; }
        _popup.IsOpen = true;
    }

    void ShowTab(bool showAdv)
    {
        _onAdvTab             = showAdv;
        _stdContent.IsVisible = !showAdv;
        _advContent.IsVisible = showAdv;
        SetTabActive(_stdTabBtn, !showAdv);
        SetTabActive(_advTabBtn, showAdv);
        if (showAdv)
        {
            RebuildGradient();
            RebuildHueStrip();
            UpdateCursors();
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static Button MakeTabBtn(string label)
    {
        var btn = new Button
        {
            Content = label,
            HorizontalAlignment        = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            Padding         = new Thickness(0, 5),
            FontSize        = 11,
            Background      = Brushes.Transparent,
            BorderThickness = new Thickness(0, 0, 0, 2),
            BorderBrush     = Brushes.Transparent,
            Foreground      = new SolidColorBrush(Color.Parse("#888888")),
            CornerRadius    = new CornerRadius(0)
        };
        return btn;
    }

    static void SetTabActive(Button btn, bool active)
    {
        btn.BorderBrush = active
            ? new SolidColorBrush(Color.Parse("#3b82f6"))
            : Brushes.Transparent;
        btn.Foreground = active
            ? Brushes.White
            : new SolidColorBrush(Color.Parse("#888888"));
    }

    static Border MakeSwatch(string hex, double size, Action onClick)
    {
        var cell = new Border
        {
            Width  = size, Height = size,
            Margin = new Thickness(1),
            CornerRadius = new CornerRadius(2),
            Background   = new SolidColorBrush(Color.Parse(hex)),
            Cursor       = new Cursor(StandardCursorType.Hand)
        };
        cell.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(cell).Properties.IsLeftButtonPressed) onClick();
        };
        return cell;
    }

    static TextBlock PaletteLabel(string text) => new()
    {
        Text       = text,
        Foreground = new SolidColorBrush(Color.Parse("#888888")),
        FontSize   = 10,
        Margin     = new Thickness(0, 2, 0, 2)
    };

    static SKColor ParseHex(string? hex)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(hex)) return SKColors.White;
            hex = hex.TrimStart('#');
            if (hex.Length == 6)
                return new SKColor(
                    Convert.ToByte(hex[0..2], 16),
                    Convert.ToByte(hex[2..4], 16),
                    Convert.ToByte(hex[4..6], 16));
        }
        catch { }
        return SKColors.White;
    }

    static void RgbToHsv(SKColor c, out float h, out float s, out float v)
    {
        float r = c.Red / 255f, g = c.Green / 255f, b = c.Blue / 255f;
        float max = Math.Max(r, Math.Max(g, b));
        float min = Math.Min(r, Math.Min(g, b));
        float d   = max - min;

        h = 0;
        if (d > 0)
        {
            if (max == r)      h = 60 * ((g - b) / d % 6);
            else if (max == g) h = 60 * ((b - r) / d + 2);
            else               h = 60 * ((r - g) / d + 4);
        }
        if (h < 0) h += 360;

        s = max == 0 ? 0 : d / max * 100;
        v = max * 100;
    }
}
