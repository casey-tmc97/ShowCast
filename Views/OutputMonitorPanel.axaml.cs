using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.Engine;
using ShowCast.ViewModels;
using SkiaSharp;
using System;
using System.Collections.Generic;

namespace ShowCast.Views;

public partial class OutputMonitorPanel : UserControl, IDisposable
{
    private MainViewModel?        _vm;
    private readonly List<IDisposable> _subs = new();

    // Thumbnail render size
    private const int ThumbW = 320;
    private const int ThumbH = 180;

    public OutputMonitorPanel()
    {
        InitializeComponent();
        TimerTextCache.Changed += OnTimerCacheChanged;
    }

    void OnTimerCacheChanged() { if (_vm is not null) UpdateAllCards(); }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();

        _vm = DataContext as MainViewModel;
        if (_vm is null) return;

        OutputList.ItemsSource = _vm.OutputStates;

        // Re-render thumbnails and highlight when selected output or live page changes
        _subs.Add(_vm.WhenAnyValue(x => x.SelectedOutput)
                     .Subscribe(_ => UpdateAllCards()));

        foreach (var state in _vm.OutputStates)
        {
            _subs.Add(state.WhenAnyValue(s => s.LivePage)
                           .Subscribe(_ => UpdateAllCards()));
        }
    }

    void OnOutputCardClicked(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
    {
        if (_vm is null) return;
        if (sender is Button btn && btn.Tag is OutputState state)
            _vm.SelectedOutput = state;
    }

    void UpdateAllCards()
    {
        // Walk the visual tree to update each card's thumbnail and border highlight
        var panel = OutputList.ItemsPanelRoot as Panel;
        if (panel is null) return;

        int i = 0;
        foreach (var child in panel.Children)
        {
            if (i >= _vm!.OutputStates.Count) break;
            var state = _vm.OutputStates[i];
            UpdateCard(child, state);
            i++;
        }
    }

    void UpdateCard(Avalonia.Controls.Control container, OutputState state)
    {
        // Find named elements inside the DataTemplate
        var cardBorder = container.FindDescendantByName<Border>("CardBorder");
        var thumbImage = container.FindDescendantByName<Image>("ThumbImage");

        if (cardBorder is not null)
        {
            bool isSelected = _vm?.SelectedOutput == state;
            cardBorder.BorderThickness = new Thickness(isSelected ? 2 : 0);
            cardBorder.BorderBrush     = isSelected
                ? new SolidColorBrush(Color.FromRgb(42, 130, 218))
                : null;
        }

        if (thumbImage is not null)
            thumbImage.Source = BuildThumbnail(state);
    }

    static Bitmap? BuildThumbnail(OutputState state)
    {
        var page = state.LivePage;
        using var surface = SKSurface.Create(
            new SKImageInfo(ThumbW, ThumbH, SKColorType.Rgba8888));

        if (page is not null)
            PageRenderer.Render(surface.Canvas, page, state.Roles, ThumbW, ThumbH);
        else
        {
            surface.Canvas.Clear(new SKColor(15, 15, 15));
            using var p = new SKPaint
            {
                Color = new SKColor(50, 50, 50), TextSize = 14,
                IsAntialias = true, TextAlign = SKTextAlign.Center
            };
            surface.Canvas.DrawText("No Signal", ThumbW / 2f, ThumbH / 2f + 5, p);
        }

        using var image = surface.Snapshot();
        using var data  = image.Encode(SKEncodedImageFormat.Png, 80);
        return new Bitmap(data.AsStream());
    }

    public void Dispose()
    {
        TimerTextCache.Changed -= OnTimerCacheChanged;
        foreach (var s in _subs) s.Dispose();
    }
}

// Extension to find a named descendant without AXAML x:Name cross-template tricks
file static class VisualExtensions
{
    public static T? FindDescendantByName<T>(this Avalonia.Controls.Control root, string name)
        where T : Avalonia.Controls.Control
    {
        if (root is T t && root.Name == name) return t;
        foreach (var child in Avalonia.VisualTree.VisualExtensions.GetVisualChildren(root))
        {
            if (child is Avalonia.Controls.Control c)
            {
                var found = c.FindDescendantByName<T>(name);
                if (found is not null) return found;
            }
        }
        return null;
    }
}
