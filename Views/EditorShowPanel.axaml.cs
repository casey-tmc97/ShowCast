using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using ReactiveUI;
using ShowCast.ViewModels;
using ShowCast.Views;

namespace ShowCast.Views;

public partial class EditorShowPanel : UserControl
{
    readonly List<IDisposable> _subs = new();

    // ── Drag state ────────────────────────────────────────────────────────────
    PageViewModel? _dragging;
    PageViewModel? _dropTarget;

    public EditorShowPanel()
    {
        InitializeComponent();

        // Tunnel so we catch the press even if a child handles it
        SlideList.AddHandler(PointerPressedEvent, OnItemPointerPressed, RoutingStrategies.Tunnel);
        SlideList.AddHandler(PointerMovedEvent,   OnItemPointerMoved,   RoutingStrategies.Tunnel);

        DragDrop.SetAllowDrop(SlideList, true);
        SlideList.AddHandler(DragDrop.DragOverEvent, OnDragOver);
        SlideList.AddHandler(DragDrop.DragLeaveEvent, OnDragLeave);
        SlideList.AddHandler(DragDrop.DropEvent, OnDrop);

        SlideList.AddHandler(ContextRequestedEvent, OnPageContextRequested);
    }

    MainViewModel? VM => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        vm.SlideContentChanged += OnPageContentChanged;
    }

    protected override void OnUnloaded(Avalonia.Interactivity.RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        if (VM is not null) VM.SlideContentChanged -= OnPageContentChanged;
    }

    void OnPageContentChanged()
    {
        var vm = VM;
        if (vm?.EditingPage is null) return;
        foreach (var pvm in vm.EditorPages)
        {
            if (pvm.Model == vm.EditingPage) { pvm.RebuildThumbnail(); break; }
        }
    }

    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VM is null) return;
        if (SlideList.SelectedItem is PageViewModel pvm)
            VM.SwitchEditingPage(pvm);
    }

    // ── Drag initiation ───────────────────────────────────────────────────────

    void OnItemPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(SlideList).Properties.IsLeftButtonPressed) return;
        _dragging = FindPvm(e.Source as Control);
    }

    async void OnItemPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_dragging is null) return;
        if (!e.GetCurrentPoint(SlideList).Properties.IsLeftButtonPressed)
        {
            _dragging = null;
            return;
        }

        var src = _dragging;
        _dragging = null;   // prevent re-entrance

        var data = new DataObject();
        data.Set("page", src);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);

        ClearDropTarget();
    }

    // ── Drag-over / drop ──────────────────────────────────────────────────────

    void OnDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("page"))
        {
            e.DragEffects = DragDropEffects.None;
            return;
        }

        e.DragEffects = DragDropEffects.Move;

        var tgt = FindPvm(e.Source as Control);
        if (tgt != _dropTarget)
        {
            ClearDropTarget();
            if (tgt is not null) tgt.ShowDropIndicator = true;
            _dropTarget = tgt;
        }

        e.Handled = true;
    }

    void OnDragLeave(object? sender, DragEventArgs e) => ClearDropTarget();

    void OnDrop(object? sender, DragEventArgs e)
    {
        ClearDropTarget();
        if (!e.Data.Contains("page")) return;

        var src = e.Data.Get("page") as PageViewModel;
        var tgt = FindPvm(e.Source as Control);

        if (src is not null && tgt is not null && src != tgt)
            VM?.MovePage(src, tgt);

        e.Handled = true;
    }

    // ── Context menu ─────────────────────────────────────────────────────────

    void OnPageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;

        var pvm = FindPvm(e.Source as Control);
        if (pvm is null) return;

        // Select the right-clicked page
        if (SlideList.SelectedItem != pvm)
            SlideList.SelectedItem = pvm;

        var renameItem = new MenuItem { Header = "Rename…" };
        renameItem.Click += async (_, _) =>
        {
            var dlg = new TextInputDialog("Rename Page", "Page name", pvm.Model.Name);
            var name = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
            if (!string.IsNullOrWhiteSpace(name))
                VM.RenamePage(pvm, name.Trim());
        };

        var duplicateItem = new MenuItem { Header = "Duplicate" };
        duplicateItem.Click += (_, _) => VM.DuplicatePage(pvm);

        var deleteItem = new MenuItem { Header = "Delete" };
        deleteItem.Click += (_, _) => VM.RemovePage(pvm);

        var menu = new ContextMenu();
        menu.Items.Add(renameItem);
        menu.Items.Add(duplicateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        menu.Open(e.Source as Control ?? (Control)sender!);
        e.Handled = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void ClearDropTarget()
    {
        if (_dropTarget is not null)
        {
            _dropTarget.ShowDropIndicator = false;
            _dropTarget = null;
        }
    }

    static PageViewModel? FindPvm(Control? c)
    {
        while (c is not null)
        {
            if (c.DataContext is PageViewModel pvm) return pvm;
            c = c.Parent as Control;
        }
        return null;
    }
}
