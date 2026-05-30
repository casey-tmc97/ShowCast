using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using ShowCast.ViewModels;
using System;

namespace ShowCast.Views;

public partial class PageEditorOverlay : UserControl
{
    public PageEditorOverlay()
    {
        InitializeComponent();
        TheInspector.SetCanvas(TheCanvas);
    }

    MainViewModel? VM => DataContext as MainViewModel;

    Key? _nudgeKey;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        if (VM is null) return;
        // Sync ComboBox to current ShowGrid / GridSpacing state
        GridSizeBox.SelectedIndex = !VM.ShowGrid ? 0 : VM.GridSpacing switch
        {
            12.5 => 1,
            25   => 2,
            50   => 3,
            75   => 4,
            100  => 5,
            _    => 0
        };
    }

    void OnGridSizeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VM is null) return;
        switch (GridSizeBox.SelectedIndex)
        {
            case 0:  VM.ShowGrid = false; VM.SnapToGrid = false;           break;  // None — also disable snap
            case 1:  VM.ShowGrid = true;  VM.GridSpacing = 12.5;           break;
            case 2:  VM.ShowGrid = true;  VM.GridSpacing =  25;            break;
            case 3:  VM.ShowGrid = true;  VM.GridSpacing =  50;            break;
            case 4:  VM.ShowGrid = true;  VM.GridSpacing =  75;            break;
            default: VM.ShowGrid = true;  VM.GridSpacing = 100;            break;
        }
    }

    void OnBack(object? sender, RoutedEventArgs e)           => VM?.CloseEditor();
    void OnAddText(object? sender, RoutedEventArgs e)        => VM?.AddTextLayer();
    void OnAddShape(object? sender, RoutedEventArgs e)       => VM?.AddShapeLayer();
    void OnUndo(object? sender, RoutedEventArgs e)           => VM?.Undo();
    void OnRedo(object? sender, RoutedEventArgs e)           => VM?.Redo();
    void OnDeleteLayer(object? sender, RoutedEventArgs e)    => VM?.DeleteLayer(VM.SelectedLayer!);
    void OnPreviewAnimation(object? sender, RoutedEventArgs e) => TheCanvas.PreviewAnimation();

    async void OnAddImage(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var tl = TopLevel.GetTopLevel(this);
        if (tl is null) return;

        var files = await tl.StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title           = "Select Image",
            AllowMultiple   = false,
            FileTypeFilter  = new[]
            {
                new FilePickerFileType("Images")
                {
                    Patterns = new[] { "*.png", "*.jpg", "*.jpeg", "*.bmp", "*.gif", "*.webp", "*.tiff", "*.tif" }
                },
                new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
            }
        });

        var path = files.FirstOrDefault()?.Path.LocalPath;
        if (!string.IsNullOrEmpty(path))
            VM.AddImageLayer(path);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);

        // While the inline canvas text editor is active, let it handle all keys.
        if (TheCanvas.IsInlineEditing) return;

        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control);

        if (ctrl && e.Key == Key.Z)         { VM?.Undo();  e.Handled = true; return; }
        if (ctrl && e.Key == Key.Y)         { VM?.Redo();  e.Handled = true; return; }
        if (ctrl && e.Key == Key.D)         { VM?.DuplicateLayer(VM?.SelectedLayer); e.Handled = true; return; }

        if (e.Key == Key.Delete || e.Key == Key.Back)
        {
            if (VM?.SelectedLayer is { } layer) VM.DeleteLayer(layer);
            e.Handled = true;
        }
        else if (e.Key == Key.Escape)
        {
            VM?.CloseEditor();
            e.Handled = true;
        }

        if (VM?.SelectedLayer is { } moveLayer && e.KeyModifiers == KeyModifiers.None)
        {
            float stepX = VM.SnapToGrid && VM.GridSpacing > 0
                ? (float)(VM.GridSpacing / 1920.0)
                : 1f / 1920f;
            float stepY = VM.SnapToGrid && VM.GridSpacing > 0
                ? (float)(VM.GridSpacing / 1080.0)
                : 1f / 1080f;
            switch (e.Key)
            {
                case Key.Left:
                    if (_nudgeKey != e.Key) { VM.BeginLayerEdit(); _nudgeKey = e.Key; }
                    moveLayer.X = Math.Clamp(moveLayer.X - stepX, 0f, 1f - moveLayer.Width);
                    VM.NotifySlideChanged();
                    e.Handled = true;
                    return;
                case Key.Right:
                    if (_nudgeKey != e.Key) { VM.BeginLayerEdit(); _nudgeKey = e.Key; }
                    moveLayer.X = Math.Clamp(moveLayer.X + stepX, 0f, 1f - moveLayer.Width);
                    VM.NotifySlideChanged();
                    e.Handled = true;
                    return;
                case Key.Up:
                    if (_nudgeKey != e.Key) { VM.BeginLayerEdit(); _nudgeKey = e.Key; }
                    moveLayer.Y = Math.Clamp(moveLayer.Y - stepY, 0f, 1f - moveLayer.Height);
                    VM.NotifySlideChanged();
                    e.Handled = true;
                    return;
                case Key.Down:
                    if (_nudgeKey != e.Key) { VM.BeginLayerEdit(); _nudgeKey = e.Key; }
                    moveLayer.Y = Math.Clamp(moveLayer.Y + stepY, 0f, 1f - moveLayer.Height);
                    VM.NotifySlideChanged();
                    e.Handled = true;
                    return;
            }
        }
    }

    protected override void OnKeyUp(KeyEventArgs e)
    {
        base.OnKeyUp(e);
        if (_nudgeKey == e.Key) _nudgeKey = null;
    }
}
