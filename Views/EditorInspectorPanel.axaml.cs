using System;
using System.Collections.Generic;
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
    EditorCanvas? _canvas;
    SlideLayer? _displayedLayer;

    static readonly string[] _systemFonts =
        SKFontManager.Default.GetFontFamilies().OrderBy(f => f).ToArray();

    public EditorInspectorPanel()
    {
        InitializeComponent();
        FontFamilyBox.ItemsSource = _systemFonts;
    }

    MainViewModel? VM => DataContext as MainViewModel;

    public void SetCanvas(EditorCanvas canvas)
    {
        if (_canvas is not null)
            _canvas.SpanFormatChanged -= OnSpanFormatChanged;
        _canvas = canvas;
        _canvas.SpanFormatChanged += OnSpanFormatChanged;
    }

    void OnSpanFormatChanged(SpanFormatInfo info)
    {
        TextColorRow.IsVisible = false;
        SpanColorRow.IsVisible = true;
        _loading = true;
        try
        {
            BoldBtn.IsChecked       = info.Bold;
            ItalicBtn.IsChecked     = info.Italic;
            UnderlineBtn.IsChecked  = info.Underline;
            StrikeBtn.IsChecked     = info.Strikethrough;
            FontSizeBox.Text = info.FontSize.HasValue
                ? ((int)(info.FontSize.Value * VH)).ToString()
                : VM?.SelectedLayer is { } l ? (l.FontSize * VH).ToString("F0") : "";
            if (info.FontFamily is not null)
                FontFamilyBox.SelectedItem = info.FontFamily;
            BaselineBox.Text = info.Baseline.HasValue
                ? info.Baseline.Value.ToString("F1")
                : VM?.SelectedLayer is { } l2 ? l2.Baseline.ToString("F1") : "0";
            KerningBox.Text = info.Kerning.HasValue
                ? info.Kerning.Value.ToString("F1")
                : VM?.SelectedLayer is { } l3 ? l3.Kerning.ToString("F1") : "0";
            if (info.Color.HasValue)
                SpanColorPicker.Value = info.Color.Value;
        }
        finally { _loading = false; }
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();

        TextColorPicker.ColorChanged    -= OnTextColorChanged;
        TextStrokePicker.ColorChanged   -= OnTextStrokeColorChanged;
        FillColorPicker.ColorChanged    -= OnFillColorChanged;
        FillStrokePicker.ColorChanged   -= OnFillStrokeColorChanged;
        SpanColorPicker.ColorChanged    -= OnSpanColorChanged;

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        TextColorPicker.ColorChanged    += OnTextColorChanged;
        TextStrokePicker.ColorChanged   += OnTextStrokeColorChanged;
        FillColorPicker.ColorChanged    += OnFillColorChanged;
        FillStrokePicker.ColorChanged   += OnFillStrokeColorChanged;
        SpanColorPicker.ColorChanged    += OnSpanColorChanged;

        _subs.Add(vm.WhenAnyValue(x => x.SelectedLayer).Subscribe(LoadLayer));
    }

    // ── Load layer ────────────────────────────────────────────────────────────

    // Each Flush* method saves typed-but-uncommitted values to _displayedLayer.
    // They are called at the top of LoadLayer, before fields are overwritten, because
    // Avalonia fires PointerPressed (which triggers LoadLayer via SelectedLayer change)
    // BEFORE LostFocus fires on the previously focused field.

    void FlushLayerNameField()
    {
        if (_displayedLayer is null) return;
        var newName = LayerNameBox.Text?.Trim();
        if (!string.IsNullOrEmpty(newName) && newName != _displayedLayer.Name)
        {
            _displayedLayer.Name = newName;
            VM?.NotifySlideChanged();
        }
    }

    void FlushTextLayerFields()
    {
        if (_displayedLayer is not { Type: LayerType.Text } layer) return;

        float? newFs  = (float.TryParse(FontSizeBox.Text,       out float fs)  && fs  > 0   && Math.Abs(fs  / VH - layer.FontSize)   > 0.0001f) ? fs  / VH     : (float?)null;
        float? newSw  = (float.TryParse(TextStrokeWidthBox.Text, out float sw)  && sw  >= 0  && Math.Abs(sw  - layer.StrokeWidth)      > 0.001f)  ? sw           : (float?)null;
        float? newBl  = (float.TryParse(BaselineBox.Text,        out float bl)              && Math.Abs(bl  - layer.Baseline)          > 0.001f)  ? bl           : (float?)null;
        float? newKr  = (float.TryParse(KerningBox.Text,         out float kr)              && Math.Abs(kr  - layer.Kerning)           > 0.001f)  ? kr           : (float?)null;

        if (newFs == null && newSw == null && newBl == null && newKr == null) return;

        VM?.BeginLayerEdit();
        if (newFs != null) layer.FontSize    = newFs.Value;
        if (newSw != null) layer.StrokeWidth = newSw.Value;
        if (newBl != null) layer.Baseline    = newBl.Value;
        if (newKr != null) layer.Kerning     = newKr.Value;
        VM?.NotifySlideChanged();
    }

    void FlushShapeLayerFields()
    {
        if (_displayedLayer is not { } layer) return;
        if (layer.Type is not (LayerType.Background or LayerType.Shape)) return;

        float? newCr  = (float.TryParse(CornerRadiusBox.Text,   out float cr)  && cr  >= 0  && Math.Abs(cr  - layer.CornerRadius)     > 0.001f)  ? cr           : (float?)null;
        float? newSw  = (float.TryParse(FillStrokeWidthBox.Text, out float sw)  && sw  >= 0  && Math.Abs(sw  - layer.StrokeWidth)      > 0.001f)  ? sw           : (float?)null;

        if (newCr == null && newSw == null) return;

        VM?.BeginLayerEdit();
        if (newCr != null) layer.CornerRadius = newCr.Value;
        if (newSw != null) layer.StrokeWidth  = newSw.Value;
        VM?.NotifySlideChanged();
    }

    void FlushTransformFields()
    {
        if (_displayedLayer is null) return;
        var layer = _displayedLayer;

        float? newX   = (float.TryParse(LayerXBox.Text,   out float x)            && Math.Abs(Math.Clamp(x / VW, 0f, 1f)        - layer.X)               > 0.0001f) ? Math.Clamp(x / VW, 0f, 1f)        : (float?)null;
        float? newY   = (float.TryParse(LayerYBox.Text,   out float y)            && Math.Abs(Math.Clamp(y / VH, 0f, 1f)        - layer.Y)               > 0.0001f) ? Math.Clamp(y / VH, 0f, 1f)        : (float?)null;
        float? newW   = (float.TryParse(LayerWBox.Text,   out float w) && w > 0   && Math.Abs(Math.Clamp(w / VW, 0.01f, 1f)     - layer.Width)           > 0.0001f) ? Math.Clamp(w / VW, 0.01f, 1f)     : (float?)null;
        float? newH   = (float.TryParse(LayerHBox.Text,   out float h) && h > 0   && Math.Abs(Math.Clamp(h / VH, 0.01f, 1f)     - layer.Height)          > 0.0001f) ? Math.Clamp(h / VH, 0.01f, 1f)     : (float?)null;
        float? newRot = (float.TryParse(LayerRotBox.Text, out float r)            && Math.Abs(r - layer.RotationDegrees)                                  > 0.001f)  ? r                                  : (float?)null;

        if (newX == null && newY == null && newW == null && newH == null && newRot == null) return;

        VM?.BeginLayerEdit();
        if (newX   != null) layer.X                 = newX.Value;
        if (newY   != null) layer.Y                 = newY.Value;
        if (newW   != null) layer.Width             = newW.Value;
        if (newH   != null) layer.Height            = newH.Value;
        if (newRot != null) layer.RotationDegrees   = newRot.Value;
        VM?.NotifySlideChanged();
    }

    void FlushAnimationFields()
    {
        if (_displayedLayer is null) return;
        var layer = _displayedLayer;

        int? newEd   = (int.TryParse(EntryDurationBox.Text, out int v1) && v1 >= 0 && v1 != layer.EntryDurationMs) ? v1 : (int?)null;
        int? newEdl  = (int.TryParse(EntryDelayBox.Text,    out int v2) && v2 >= 0 && v2 != layer.EntryDelayMs)   ? v2 : (int?)null;
        int? newHd   = (int.TryParse(HoldDurationBox.Text,  out int v3) && v3 >= 0 && v3 != layer.HoldDurationMs) ? v3 : (int?)null;
        int? newExd  = (int.TryParse(ExitDurationBox.Text,  out int v4) && v4 >= 0 && v4 != layer.ExitDurationMs) ? v4 : (int?)null;
        int? newExdl = (int.TryParse(ExitDelayBox.Text,     out int v5) && v5 >= 0 && v5 != layer.ExitDelayMs)    ? v5 : (int?)null;

        if (newEd == null && newEdl == null && newHd == null && newExd == null && newExdl == null) return;

        VM?.BeginLayerEdit();
        if (newEd   != null) layer.EntryDurationMs = newEd.Value;
        if (newEdl  != null) layer.EntryDelayMs    = newEdl.Value;
        if (newHd   != null) layer.HoldDurationMs  = newHd.Value;
        if (newExd  != null) layer.ExitDurationMs  = newExd.Value;
        if (newExdl != null) layer.ExitDelayMs     = newExdl.Value;
        VM?.NotifySlideChanged();
    }

    void LoadLayer(SlideLayer? layer)
    {
        FlushLayerNameField();
        FlushTransformFields();
        FlushAnimationFields();
        FlushTextLayerFields();
        FlushShapeLayerFields();
        _displayedLayer = layer;
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
            ImageSection.IsVisible     = false;
            FillSection.IsVisible      = false;
            TextColorRow.IsVisible     = true;
            SpanColorRow.IsVisible     = false;

            if (layer is null)
            {
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
                    UnderlineBtn.IsChecked  = layer.Underline;
                    StrikeBtn.IsChecked     = layer.Strikethrough;
                    BaselineBox.Text        = layer.Baseline.ToString("F1");
                    KerningBox.Text         = layer.Kerning.ToString("F1");
                    SpanColorPicker.Value   = layer.Color;
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
        bool changed = false;
        if (float.TryParse(LayerXBox.Text,   out float x) && Math.Abs(Math.Clamp(x / VW, 0f, 1f)    - layer.X)             > 0.0001f) changed = true;
        if (float.TryParse(LayerYBox.Text,   out float y) && Math.Abs(Math.Clamp(y / VH, 0f, 1f)    - layer.Y)             > 0.0001f) changed = true;
        if (float.TryParse(LayerWBox.Text,   out float w) && w > 0 && Math.Abs(Math.Clamp(w / VW, 0.01f, 1f) - layer.Width)  > 0.0001f) changed = true;
        if (float.TryParse(LayerHBox.Text,   out float h) && h > 0 && Math.Abs(Math.Clamp(h / VH, 0.01f, 1f) - layer.Height) > 0.0001f) changed = true;
        if (float.TryParse(LayerRotBox.Text, out float r) && Math.Abs(r - layer.RotationDegrees)                             > 0.001f)  changed = true;
        if (!changed) return;
        VM.BeginLayerEdit();
        if (float.TryParse(LayerXBox.Text,   out x))  layer.X               = Math.Clamp(x / VW, 0f, 1f);
        if (float.TryParse(LayerYBox.Text,   out y))  layer.Y               = Math.Clamp(y / VH, 0f, 1f);
        if (float.TryParse(LayerWBox.Text,   out w) && w > 0) layer.Width   = Math.Clamp(w / VW, 0.01f, 1f);
        if (float.TryParse(LayerHBox.Text,   out h) && h > 0) layer.Height  = Math.Clamp(h / VH, 0.01f, 1f);
        if (float.TryParse(LayerRotBox.Text, out r))  layer.RotationDegrees = r;
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
        if (FontFamilyBox.SelectedItem is not string fam || string.IsNullOrEmpty(fam)) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(fontFamily: fam);
            VM!.NotifySlideChanged();
            return;
        }

        VM!.BeginLayerEdit();
        layer.FontFamily = fam;
        VM!.NotifySlideChanged();
    }

    void OnFontSizeLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (!float.TryParse(FontSizeBox.Text, out float px) || px <= 0) return;
        float normalized = px / VH;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(fontSize: normalized);
            VM!.NotifySlideChanged();
            return;
        }

        VM!.BeginLayerEdit();
        layer.FontSize = normalized;
        VM!.NotifySlideChanged();
    }

    void OnTextColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        VM.BeginLayerEdit();
        layer.Color = color;
        // Clear per-span color overrides so the new layer color applies everywhere.
        foreach (var span in layer.Spans) span.Color = null;
        VM.NotifySlideChanged();
    }

    void OnStyleClick(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(
                bold:          sender == BoldBtn      ? BoldBtn.IsChecked      : null,
                italic:        sender == ItalicBtn    ? ItalicBtn.IsChecked    : null,
                underline:     sender == UnderlineBtn ? UnderlineBtn.IsChecked : null,
                strikethrough: sender == StrikeBtn    ? StrikeBtn.IsChecked    : null);
            VM!.NotifySlideChanged();
            return;
        }

        VM!.BeginLayerEdit();
        if (sender == BoldBtn)      layer.Bold          = BoldBtn.IsChecked      == true;
        if (sender == ItalicBtn)    layer.Italic        = ItalicBtn.IsChecked    == true;
        if (sender == UnderlineBtn) layer.Underline     = UnderlineBtn.IsChecked == true;
        if (sender == StrikeBtn)    layer.Strikethrough = StrikeBtn.IsChecked    == true;
        VM!.NotifySlideChanged();
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
        if (float.TryParse(TextStrokeWidthBox.Text, out float w) && w >= 0 && Math.Abs(w - layer.StrokeWidth) > 0.001f)
        { VM.BeginLayerEdit(); layer.StrokeWidth = w; VM.NotifySlideChanged(); }
    }

    void OnBaselineLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (!float.TryParse(BaselineBox.Text, out float val)) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(baseline: val);
            VM!.NotifySlideChanged();
            return;
        }

        VM!.BeginLayerEdit();
        layer.Baseline = val;
        VM!.NotifySlideChanged();
    }

    void OnKerningLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;
        if (!float.TryParse(KerningBox.Text, out float val)) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(kerning: val);
            VM!.NotifySlideChanged();
            return;
        }

        VM!.BeginLayerEdit();
        layer.Kerning = val;
        VM!.NotifySlideChanged();
    }

    void OnSpanColorChanged(SKColor color)
    {
        if (_loading || VM?.SelectedLayer is not { Type: LayerType.Text } layer) return;

        var editor = _canvas?.ActiveTextEditor;
        if (editor is not null)
        {
            editor.ApplyFormat(color: color);
            VM!.NotifySlideChanged();
            return;
        }

        VM!.BeginLayerEdit();
        layer.Color = color;
        VM!.NotifySlideChanged();
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
        if (float.TryParse(CornerRadiusBox.Text, out float r) && r >= 0 && Math.Abs(r - layer.CornerRadius) > 0.001f)
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
        if (float.TryParse(FillStrokeWidthBox.Text, out float w) && w >= 0 && Math.Abs(w - layer.StrokeWidth) > 0.001f)
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
        if (int.TryParse(EntryDurationBox.Text, out int ms) && ms >= 0 && ms != layer.EntryDurationMs)
        { VM.BeginLayerEdit(); layer.EntryDurationMs = ms; VM.NotifySlideChanged(); }
    }

    void OnEntryDelayLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(EntryDelayBox.Text, out int ms) && ms >= 0 && ms != layer.EntryDelayMs)
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
        if (int.TryParse(HoldDurationBox.Text, out int ms) && ms >= 0 && ms != layer.HoldDurationMs)
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
        if (int.TryParse(ExitDurationBox.Text, out int ms) && ms >= 0 && ms != layer.ExitDurationMs)
        { VM.BeginLayerEdit(); layer.ExitDurationMs = ms; VM.NotifySlideChanged(); }
    }

    void OnExitDelayLostFocus(object? sender, RoutedEventArgs e)
    {
        if (_loading || VM?.SelectedLayer is not { } layer) return;
        if (int.TryParse(ExitDelayBox.Text, out int ms) && ms >= 0 && ms != layer.ExitDelayMs)
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
        else if (tb == BaselineBox)         OnBaselineLostFocus(tb, null!);
        else if (tb == KerningBox)          OnKerningLostFocus(tb, null!);
        e.Handled = true;
    }

}
