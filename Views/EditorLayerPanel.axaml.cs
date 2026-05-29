using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Data.Converters;
using Avalonia.Interactivity;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class EditorLayerPanel : UserControl
{
    public static readonly FuncValueConverter<LayerType, string> TypeBadge =
        new(t => t switch
        {
            LayerType.Background => "BG",
            LayerType.Text       => "T",
            LayerType.Image      => "IMG",
            LayerType.Shape      => "SHP",
            LayerType.Clock      => "CLK",
            _                    => "?"
        });

    public static readonly FuncValueConverter<bool, string> LockIcon =
        new(locked => locked ? "🔒" : "○");

    public static readonly FuncValueConverter<bool, Avalonia.Media.IBrush> VisibilityBrush =
        new(visible => visible
            ? new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#22cc66"))
            : new Avalonia.Media.SolidColorBrush(Avalonia.Media.Color.Parse("#cc3333")));

    readonly List<IDisposable> _subs = new();
    bool _syncingSelection;

    public EditorLayerPanel() => InitializeComponent();

    MainViewModel? VM => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        _subs.Add(vm.WhenAnyValue(x => x.SelectedLayer).Subscribe(layer =>
        {
            _syncingSelection = true;
            LayerList.SelectedItem = layer;
            _syncingSelection = false;
        }));
    }

    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection || VM is null) return;
        VM.SelectedLayer = LayerList.SelectedItem as SlideLayer;
    }

    void OnEyeClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SlideLayer layer)
            VM?.ToggleLayerVisibility(layer);
    }

    void OnLockClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is not SlideLayer layer || VM is null) return;
        VM.BeginLayerEdit();
        layer.Locked = !layer.Locked;
        // Force list refresh so lock icon updates
        var saved = VM.SelectedLayer;
        VM.RefreshEditorLayersPublic();
        VM.SelectedLayer = saved;
    }

    void OnDeleteClick(object? sender, RoutedEventArgs e)
    {
        if ((sender as Button)?.Tag is SlideLayer layer)
            VM?.DeleteLayer(layer);
    }

    void OnMoveUp(object? sender, RoutedEventArgs e)    => VM?.MoveLayerUp(VM.SelectedLayer!);
    void OnMoveDown(object? sender, RoutedEventArgs e)   => VM?.MoveLayerDown(VM.SelectedLayer!);
    void OnDuplicate(object? sender, RoutedEventArgs e)  => VM?.DuplicateLayer(VM.SelectedLayer);
    void OnBringFront(object? sender, RoutedEventArgs e) => VM?.MoveLayerToFront(VM.SelectedLayer!);
    void OnSendBack(object? sender, RoutedEventArgs e)   => VM?.MoveLayerToBack(VM.SelectedLayer!);
}
