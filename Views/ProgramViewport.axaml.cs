using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.Linq;
using Avalonia.Controls;
using Avalonia.VisualTree;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

/// <summary>
/// Shows one WebView2PreviewControl per output.
/// Page navigation is handled entirely by the LivePage binding — no Skia render loop needed.
/// </summary>
public partial class ProgramViewport : UserControl, IDisposable
{
    MainViewModel? _vm;
    readonly List<IDisposable>                    _subs     = new();
    readonly ObservableCollection<OutputPreviewItem> _previews = new();

    public ProgramViewport()
    {
        InitializeComponent();
        OutputGrid.ItemsSource = _previews;
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        Teardown();
        _vm = DataContext as MainViewModel;
        if (_vm is null) return;
        _vm.OutputStates.CollectionChanged += OnOutputsChanged;
        _vm.OutputConfigsChanged           += OnOutputConfigsChanged;
        _subs.Add(_vm.WhenAnyValue(x => x.SelectedOutput).Subscribe(UpdateSelection));
        _subs.Add(_vm.WhenAnyValue(x => x.IsEditorOpen)
            .Subscribe(open => SetPreviewNativeVisible(!open)));
        RebuildPreviews();
    }

    void SetPreviewNativeVisible(bool visible)
    {
        foreach (var ctrl in this.GetVisualDescendants().OfType<WebView2PreviewControl>())
            ctrl.SetNativeVisible(visible);
    }

    void OnOutputsChanged(object? sender, NotifyCollectionChangedEventArgs e) => RebuildPreviews();
    void OnOutputConfigsChanged() { foreach (var item in _previews) item.RaiseConfigChanged(); }

    void RebuildPreviews()
    {
        _previews.Clear();
        if (_vm is null) return;
        foreach (var output in _vm.OutputStates)
            _previews.Add(new OutputPreviewItem(output));
        UpdateSelection(_vm.SelectedOutput);
    }

    void UpdateSelection(OutputState? selected)
    {
        foreach (var item in _previews)
            item.IsSelected = item.Output == selected;
    }

    public void OnOutputTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_vm is null) return;
        var src = e.Source as Control;
        while (src is not null)
        {
            if (src.DataContext is OutputPreviewItem item)
            { _vm.SelectedOutput = item.Output; return; }
            src = src.Parent as Control;
        }
    }

    void Teardown()
    {
        if (_vm is not null)
        {
            _vm.OutputStates.CollectionChanged -= OnOutputsChanged;
            _vm.OutputConfigsChanged           -= OnOutputConfigsChanged;
        }
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();
        _previews.Clear();
        _vm = null;
    }

    public void Dispose() => Teardown();
}
