using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.Engine;
using ShowCast.ViewModels;
using SkiaSharp;

namespace ShowCast.Views;

record TimerBindingOption(Guid? Id, string Label)
{
    public override string ToString() => Label;
}

public partial class EditorInspectorPanel : UserControl
{
    const float VW = 1920f;
    const float VH = 1080f;

    readonly List<IDisposable> _subs = new();
    bool _loading;
    SlideLayer? _currentLayer;

    static readonly string[] _systemFonts =
        SKFontManager.Default.GetFontFamilies().OrderBy(f => f).ToArray();

    public EditorInspectorPanel()
    {
        InitializeComponent();
        FontFamilyBox.ItemsSource = _systemFonts;
    }

    MainViewModel? VM => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();

        TextColorPicker.ColorChanged    -= OnTextColorChanged;
        TextStrokePicker.ColorChanged   -= OnTextStrokeColorChanged;
        FillColorPicker.ColorChanged    -= OnFillColorChanged;
        FillStrokePicker.ColorChanged   -= OnFillStrokeColorChanged;

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        TextColorPicker.ColorChanged    += OnTextColorChanged;
        TextStrokePicker.ColorChanged   += OnTextStrokeColorChanged;
        FillColorPicker.ColorChanged    += OnFillColorChanged;
        FillStrokePicker.ColorChanged   += OnFillStrokeColorChanged;

        _subs.Add(vm.WhenAnyValue(x => x.SelectedLayer).Subscribe(LoadLayer));
    }

    // ── Load layer ────────────────────────────────────────────────────────────

    void LoadLayer(SlideLayer? layer)
    {
        _loading = true;
        try
        {
            bool hasSel = layer is not null;
            NoSelMsg.IsVisible         = !hasSel;
            CommonSection.IsVisible    = hasSel;
            TransformSection.IsVisible = hasSel;
            AlignSection.IsVisible     = hasSel;
            AnimSection.IsVisible      = hasSel;
            TextSection.IsVisible      = false;
            SpansSection.IsVisible     = false;
            ImageSection.IsVisible     = false;
            FillSection.IsVisible      = false;

            if (layer is null)
            {
                _currentLayer = null;
                return;
            }

            // ── Common ──
            LayerNameBox.Text          = layer.Name;
            OpacitySlider.Value        = layer.Opacity * 100;
            OpacityLabel.Text          = $"{(int)(layer.Opacity * 100)}%";
            BlendModeBox.SelectedIndex = (int)layer.BlendMode;

            // ── Transform ──
            LayerXBox.Text   = (layer.X   * VW).ToString("F0");
            LayerYBox.Text   = (layer.Y   * VH).ToString("F0");
            LayerWBox.Text   = (layer.Width  * VW).ToString("F0");
            LayerHBox.Text   = (layer.Height * VH).ToString("F0");
            LayerRotBox.Text = layer.RotationDegrees.ToString("F1");

            // ── Type-specific ──
            switch (layer.Type)
            {
                case LayerType.Text:
                    TextSection.IsVisible    = true;
                    SpansSection.IsVisible   = true;
                    _currentLayer            = layer;
                    var timerItems = new System.Collections.Generic.List<TimerBindingOption>
                        { new(null, "(None)") };
                    if (VM is not null)
                        timerItems.AddRange(VM.Timers.Select(t => new TimerBindingOption(t.Def.Id, t.Def.Name)));
                    TimerSourceBox.ItemsSource = timerItems;
                    TimerSourceBox.SelectedIndex = layer.TimerBinding is null ? 0
                        : timerItems.FindIndex(i => i.Id == layer.TimerBinding);
                    FontFamilyBox.SelectedItem = layer.FontFamily;
                    FontSizeBox.Text         = (layer.FontSize * VH).ToString("F0");
                    TextColorPicker.Value    = layer.Color;
                    BoldBtn.IsChecked        = layer.Bold;
                    ItalicBtn.IsChecked      = layer.Italic;
                    AlignLeftBtn.IsChecked   = layer.TextHAlign == TextHAlign.Left;
                    AlignCenterBtn.IsChecked = layer.TextHAlign == TextHAlign.Center;
                    AlignRightBtn.IsChecked  = layer.TextHAlign == TextHAlign.Right;
                    VAlignTopBtn.IsChecked   = layer.TextVAlign == TextVAlign.Top;
                    VAlignMidBtn.IsChecked   = layer.TextVAlign == TextVAlign.Middle;
                    VAlignBotBtn.IsChecked   = layer.TextVAlign == TextVAlign.Bottom;
                    TextStrokePicker.Value   = layer.StrokeColor;
                    TextStrokeWidthBox.Text  = layer.StrokeWidth.ToString("F1");
                    RefreshSpans(layer);
                    break;

                case LayerType.Image:
                    ImageSection.IsVisible      = true;
                    ImagePathBox.Text           = layer.AssetPath;
                    ImageFitBox.SelectedIndex   = (int)layer.ImageFit;
                    ImageOpacitySlider.Value    = layer.Opacity * 100;
                    ImageOpacityLabel.Text      = $"{(int)(layer.Opacity * 100)}%";
                    break;

                case LayerType.Background:
                case LayerType.Shape:
                    FillSection.IsVisible   = true;
                    ShapeKindBox.SelectedIndex = (int)layer.ShapeKind;
                    CornerRadiusBox.Text    = layer.CornerRadius.ToString("F0");
                    FillColorPicker.Value   = layer.Color;
                    FillStrokePicker.Value  = layer.StrokeColor;
                    FillStrokeWidthBox.Text = layer.StrokeWidth.ToString("F1");
                    break;
            }

            // ── Animation (all layer types) ──
            EntryAnimBox.SelectedIndex   = (int)layer.EntryAnim;
            EntryDurationBox.Text        = layer.EntryDurationMs.ToString();
            EntryDelayBox.Text           = layer.EntryDelayMs.ToString();
            EntryEasingBox.SelectedIndex = layer.EntryEasing;
            HoldDurationBox.Text         = layer.HoldDurationMs.ToString();
            ExitAnimBox.SelectedIndex    = (int)layer.ExitAnim;
            ExitDurationBox.Text         = layer.ExitDurationMs.ToString();
            ExitDelayBox.Text            = layer.ExitDelayMs.ToString();
            ExitEasingBox.SelectedIndex  = layer.ExitEasing;
        }
        finally
        {
            _loading = false;
        }
    }

    // ── Common ────────────────────────────────────────────────────────────────

    void OnLayerNameLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        layer.Name = LayerNameBox.Text?.Trim() ?? layer.Name;
        VM.NotifySlideChanged();
    }

    void OnLayerNameKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) OnLayerNameLostFocus(sender, null!);
    }

    void OnOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        layer.Opacity     = (float)(e.NewValue / 100.0);
        OpacityLabel.Text = $"{(int)e.NewValue}%";
        VM.NotifySlideChanged();
    }

    void OnBlendModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        layer.BlendMode = (BlendMode)BlendModeBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    // ── Transform ─────────────────────────────────────────────────────────────

    void OnTransformLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        VM.BeginLayerEdit();
        if (float.TryParse(LayerXBox.Text,   out float x))  layer.X               = Math.Clamp(x / VW, 0f, 1f);
        if (float.TryParse(LayerYBox.Text,   out float y))  layer.Y               = Math.Clamp(y / VH, 0f, 1f);
        if (float.TryParse(LayerWBox.Text,   out float w) && w > 0) layer.Width   = Math.Clamp(w / VW, 0.01f, 1f);
        if (float.TryParse(LayerHBox.Text,   out float h) && h > 0) layer.Height  = Math.Clamp(h / VH, 0.01f, 1f);
        if (float.TryParse(LayerRotBox.Text, out float r))  layer.RotationDegrees = r;
        VM.NotifySlideChanged();
    }

    void OnTransformKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter) { OnTransformLostFocus(sender, null!); e.Handled = true; }
    }

    // ── Align to canvas ───────────────────────────────────────────────────────

    void OnAlignCanvas(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedLayer is not { } layer) return;
        var tag = (sender as Button)?.Tag?.ToString();
        VM.AlignLayer(layer, tag ?? string.Empty);
    }

    // ── Text ─────────────────────────────────────────────────────────────────

    void OnTimerSourceChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        var option = TimerSourceBox.SelectedItem as TimerBindingOption;
        VM.BeginLayerEdit();
        layer.TimerBinding = option?.Id;
        VM.NotifySlideChanged();
    }

    void OnFontFamilySelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (FontFamilyBox.SelectedItem is string fam && !string.IsNullOrEmpty(fam))
        {
            VM.BeginLayerEdit();
            layer.FontFamily = fam;
            VM.NotifySlideChanged();
        }
    }

    void OnFontSizeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (float.TryParse(FontSizeBox.Text, out float px) && px > 0)
        { VM.BeginLayerEdit(); layer.FontSize = px / VH; VM.NotifySlideChanged(); }
    }

    void OnTextColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        VM.BeginLayerEdit(); layer.Color = color; VM.NotifySlideChanged();
    }

    void OnStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        VM.BeginLayerEdit();
        if (sender == BoldBtn)   layer.Bold   = BoldBtn.IsChecked   == true;
        if (sender == ItalicBtn) layer.Italic = ItalicBtn.IsChecked == true;
        VM.NotifySlideChanged();
    }

    void OnAlignClick(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        VM.BeginLayerEdit();
        AlignLeftBtn.IsChecked   = sender == AlignLeftBtn;
        AlignCenterBtn.IsChecked = sender == AlignCenterBtn;
        AlignRightBtn.IsChecked  = sender == AlignRightBtn;
        layer.TextHAlign = sender == AlignLeftBtn  ? TextHAlign.Left  :
                           sender == AlignRightBtn ? TextHAlign.Right : TextHAlign.Center;
        VM.NotifySlideChanged();
    }

    void OnVAlignClick(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        VM.BeginLayerEdit();
        VAlignTopBtn.IsChecked = sender == VAlignTopBtn;
        VAlignMidBtn.IsChecked = sender == VAlignMidBtn;
        VAlignBotBtn.IsChecked = sender == VAlignBotBtn;
        layer.TextVAlign = sender == VAlignTopBtn ? TextVAlign.Top :
                           sender == VAlignBotBtn ? TextVAlign.Bottom : TextVAlign.Middle;
        VM.NotifySlideChanged();
    }

    void OnTextStrokeColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        VM.BeginLayerEdit(); layer.StrokeColor = color; VM.NotifySlideChanged();
    }

    void OnTextStrokeWidthLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (float.TryParse(TextStrokeWidthBox.Text, out float w) && w >= 0)
        { VM.BeginLayerEdit(); layer.StrokeWidth = w; VM.NotifySlideChanged(); }
    }

    // ── Fill / Shape ──────────────────────────────────────────────────────────

    void OnFillColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (layer.Type is not (LayerType.Background or LayerType.Shape)) return;
        VM.BeginLayerEdit(); layer.Color = color; VM.NotifySlideChanged();
    }

    void OnShapeKindChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (layer.Type is not (LayerType.Background or LayerType.Shape)) return;
        VM.BeginLayerEdit();
        layer.ShapeKind = (ShapeKind)ShapeKindBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    void OnCornerRadiusLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (float.TryParse(CornerRadiusBox.Text, out float r) && r >= 0)
        { VM.BeginLayerEdit(); layer.CornerRadius = r; VM.NotifySlideChanged(); }
    }

    void OnFillStrokeColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (layer.Type is not (LayerType.Background or LayerType.Shape)) return;
        VM.BeginLayerEdit(); layer.StrokeColor = color; VM.NotifySlideChanged();
    }

    void OnFillStrokeWidthLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (layer.Type is not (LayerType.Background or LayerType.Shape)) return;
        if (float.TryParse(FillStrokeWidthBox.Text, out float w) && w >= 0)
        { VM.BeginLayerEdit(); layer.StrokeWidth = w; VM.NotifySlideChanged(); }
    }

    // ── Image ─────────────────────────────────────────────────────────────────

    async void OnBrowseImage(object? sender, RoutedEventArgs e)
    {
        if (VM?.SelectedLayer is not { Type: LayerType.Image } layer) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title          = "Select Image",
            AllowMultiple  = false,
            FileTypeFilter = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tiff", "*.tif" }
                },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (string.IsNullOrEmpty(path)) return;

        VM.BeginLayerEdit();
        PageRenderer.InvalidateImage(layer.AssetPath); // clear old from cache
        layer.AssetPath  = path;
        ImagePathBox.Text = path;
        VM.NotifySlideChanged();
    }

    void OnImageFitChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Image } layer) return;
        layer.ImageFit = (ImageFit)ImageFitBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    void OnImageOpacityChanged(object? sender, RangeBaseValueChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Image } layer) return;
        layer.Opacity          = (float)(e.NewValue / 100.0);
        ImageOpacityLabel.Text = $"{(int)e.NewValue}%";
        VM.NotifySlideChanged();
    }

    // ── Animation ─────────────────────────────────────────────────────────────

    void OnEntryAnimChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        VM.BeginLayerEdit();
        layer.EntryAnim = (LayerAnimation)EntryAnimBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    void OnEntryDurationLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(EntryDurationBox.Text, out int ms) && ms >= 0)
        { VM.BeginLayerEdit(); layer.EntryDurationMs = ms; VM.NotifySlideChanged(); }
    }

    void OnEntryDelayLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(EntryDelayBox.Text, out int ms) && ms >= 0)
        { VM.BeginLayerEdit(); layer.EntryDelayMs = ms; VM.NotifySlideChanged(); }
    }

    void OnEntryEasingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        VM.BeginLayerEdit();
        layer.EntryEasing = EntryEasingBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    void OnExitEasingChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        VM.BeginLayerEdit();
        layer.ExitEasing = ExitEasingBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    void OnHoldDurationLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(HoldDurationBox.Text, out int ms) && ms >= 0)
        { VM.BeginLayerEdit(); layer.HoldDurationMs = ms; VM.NotifySlideChanged(); }
    }

    void OnExitAnimChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        VM.BeginLayerEdit();
        layer.ExitAnim = (LayerExitAnimation)ExitAnimBox.SelectedIndex;
        VM.NotifySlideChanged();
    }

    void OnExitDurationLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(ExitDurationBox.Text, out int ms) && ms >= 0)
        { VM.BeginLayerEdit(); layer.ExitDurationMs = ms; VM.NotifySlideChanged(); }
    }

    void OnExitDelayLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(ExitDelayBox.Text, out int ms) && ms >= 0)
        { VM.BeginLayerEdit(); layer.ExitDelayMs = ms; VM.NotifySlideChanged(); }
    }

    void OnAnimKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        if      (tb == EntryDurationBox) OnEntryDurationLostFocus(tb, null!);
        else if (tb == EntryDelayBox)    OnEntryDelayLostFocus(tb, null!);
        else if (tb == HoldDurationBox)  OnHoldDurationLostFocus(tb, null!);
        else if (tb == ExitDurationBox)  OnExitDurationLostFocus(tb, null!);
        else if (tb == ExitDelayBox)     OnExitDelayLostFocus(tb, null!);
        e.Handled = true;
    }

    // ── Enter key on single-line inputs ───────────────────────────────────────

    void OnSingleLineKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter || sender is not TextBox tb) return;
        else if (tb == FontSizeBox)         OnFontSizeLostFocus(tb, null!);
        else if (tb == TextStrokeWidthBox)  OnTextStrokeWidthLostFocus(tb, null!);
        else if (tb == CornerRadiusBox)     OnCornerRadiusLostFocus(tb, null!);
        else if (tb == FillStrokeWidthBox)  OnFillStrokeWidthLostFocus(tb, null!);
        e.Handled = true;
    }

    // ── Rich Text Spans ───────────────────────────────────────────────────────

    Control BuildSpanRow(TextSpan span, int index, SlideLayer layer)
    {
        // Text content box
        var textBox = new TextBox
        {
            Text          = span.Text,
            AcceptsReturn = true,
            TextWrapping  = Avalonia.Media.TextWrapping.Wrap,
            MinHeight     = 48,
            MaxHeight     = 120,
            Margin        = new Avalonia.Thickness(0, 0, 0, 4),
        };
        textBox.LostFocus += (_, _) =>
        {
            VM?.BeginLayerEdit();
            span.Text = textBox.Text ?? string.Empty;
            VM?.NotifySlideChanged();
        };

        // Font size override
        var fontSizeBox = new TextBox
        {
            Text      = span.FontSize.HasValue ? ((int)(span.FontSize.Value * VH)).ToString() : "",
            Watermark = "inherit",
            Width     = 70,
            Height    = 28,
        };
        fontSizeBox.LostFocus += (_, _) =>
        {
            VM?.BeginLayerEdit();
            if (string.IsNullOrWhiteSpace(fontSizeBox.Text))
                span.FontSize = null;
            else if (float.TryParse(fontSizeBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out float px) && px > 0)
                span.FontSize = px / VH;
            VM?.NotifySlideChanged();
        };

        // Bold toggle
        var boldBtn = new ToggleButton
        {
            Content    = "B",
            FontWeight = Avalonia.Media.FontWeight.Bold,
            IsChecked  = span.Bold,
            Width = 28, Height = 28,
        };
        boldBtn.Classes.Add("option-toggle");
        boldBtn.IsCheckedChanged += (_, _) =>
        {
            VM?.BeginLayerEdit();
            span.Bold = boldBtn.IsChecked;
            VM?.NotifySlideChanged();
        };

        // Italic toggle
        var italicBtn = new ToggleButton
        {
            Content   = "I",
            FontStyle = Avalonia.Media.FontStyle.Italic,
            IsChecked = span.Italic,
            Width = 28, Height = 28,
        };
        italicBtn.Classes.Add("option-toggle");
        italicBtn.IsCheckedChanged += (_, _) =>
        {
            VM?.BeginLayerEdit();
            span.Italic = italicBtn.IsChecked;
            VM?.NotifySlideChanged();
        };

        // Delete button
        var deleteBtn = new Button
        {
            Content         = "×",
            FontSize        = 15,
            Width           = 28,
            Height          = 28,
            Background      = Avalonia.Media.Brushes.Transparent,
            Foreground      = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#cc4444")),
            BorderThickness = new Avalonia.Thickness(0),
        };
        ToolTip.SetTip(deleteBtn, "Remove span");
        deleteBtn.Click += (_, _) =>
        {
            VM?.BeginLayerEdit();
            layer.Spans.Remove(span);
            RefreshSpans(layer);
            VM?.NotifySlideChanged();
        };

        // Span header row: index label + bold + italic + font size + delete
        var headerRow = new StackPanel
        {
            Orientation = Avalonia.Layout.Orientation.Horizontal,
            Spacing     = 4,
            Margin      = new Avalonia.Thickness(0, 0, 0, 4),
        };
        headerRow.Children.Add(new TextBlock
        {
            Text              = $"Span {index + 1}",
            Foreground        = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#aaaaaa")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize          = 11,
            MinWidth          = 50,
        });
        headerRow.Children.Add(boldBtn);
        headerRow.Children.Add(italicBtn);
        headerRow.Children.Add(new TextBlock
        {
            Text              = "px:",
            Foreground        = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#888888")),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            FontSize          = 11,
        });
        headerRow.Children.Add(fontSizeBox);
        headerRow.Children.Add(deleteBtn);

        var innerPanel = new StackPanel();
        innerPanel.Children.Add(headerRow);
        innerPanel.Children.Add(textBox);

        return new Border
        {
            BorderBrush     = new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#3a3a3a")),
            BorderThickness = new Avalonia.Thickness(0, 0, 0, 1),
            Margin          = new Avalonia.Thickness(0, 0, 0, 8),
            Child           = innerPanel,
        };
    }

    void RefreshSpans(SlideLayer layer)
    {
        SpanList.ItemsSource = null;
        var rows = new List<Control>();
        for (int i = 0; i < layer.Spans.Count; i++)
            rows.Add(BuildSpanRow(layer.Spans[i], i, layer));
        SpanList.ItemsSource = rows;
    }

    void OnAddSpan(object? sender, RoutedEventArgs e)
    {
        if (_currentLayer is null || _currentLayer.Type != LayerType.Text) return;
        VM?.BeginLayerEdit();
        _currentLayer.Spans.Add(new TextSpan { Text = "" });
        RefreshSpans(_currentLayer);
        VM?.NotifySlideChanged();
    }
}
