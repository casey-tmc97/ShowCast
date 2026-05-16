using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class ItemsPanel : UserControl
{
    const string PkgDragKey   = "ShowCast.PackageReorder";
    const double DragThreshold = 4.0;

    ListBoxItem? _dragOverItem;

    // Package reorder drag state
    Package? _draggingPkg;
    Point    _pkgDragStart;

    public ItemsPanel()
    {
        InitializeComponent();

        ItemList.AddHandler(PointerPressedEvent, OnPkgPointerPressed, RoutingStrategies.Tunnel);
        ItemList.AddHandler(PointerMovedEvent,   OnPkgPointerMoved,   RoutingStrategies.Tunnel);
        ItemList.AddHandler(PointerReleasedEvent, OnPkgPointerReleased, RoutingStrategies.Tunnel);

        ItemList.AddHandler(DragDrop.DragOverEvent,  OnItemDragOver);
        ItemList.AddHandler(DragDrop.DragLeaveEvent, OnItemDragLeave);
        ItemList.AddHandler(DragDrop.DropEvent,      OnItemDrop);
    }

    MainViewModel? VM => DataContext as MainViewModel;

    void OnItemSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (ItemList.SelectedItem is Package package && VM is not null)
            VM.LoadPackageToSelectedOutput(package);
    }

    async void OnAddShow(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new NewShowDialog(VM.Shows, VM.ShowFile.Rundowns, VM.SelectedShow);
        var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);
        if (result is not null)
        {
            var show = result.NewShowFolderName is not null
                ? VM.AddShow(result.NewShowFolderName)
                : result.TargetShow!;
            var rundown = result.NewRundownName is not null
                ? VM.AddRundown(result.NewRundownName)
                : result.TargetRundown;
            VM.AddPackageToShow(result.Name, show, rundown);
        }
    }

    // ── Package reorder drag initiation ──────────────────────────────────────

    void OnPkgPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (VM is null || !VM.ShowingRundown) return;
        if (!e.GetCurrentPoint(ItemList).Properties.IsLeftButtonPressed) return;
        _draggingPkg = FindItemDataContext<Package>(e.Source);
        _pkgDragStart = e.GetPosition(ItemList);
    }

    async void OnPkgPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_draggingPkg is null) return;
        if (!e.GetCurrentPoint(ItemList).Properties.IsLeftButtonPressed)
        {
            _draggingPkg = null;
            return;
        }

        var pos = e.GetPosition(ItemList);
        if (System.Math.Abs(pos.X - _pkgDragStart.X) < DragThreshold &&
            System.Math.Abs(pos.Y - _pkgDragStart.Y) < DragThreshold)
            return;

        var pkg = _draggingPkg;
        _draggingPkg = null;

        var data = new DataObject();
        data.Set(PkgDragKey, pkg);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);

        ClearDragOver();
    }

    void OnPkgPointerReleased(object? sender, PointerReleasedEventArgs e) => _draggingPkg = null;

    // ── Drag-over / drop ──────────────────────────────────────────────────────

    void OnItemDragOver(object? sender, DragEventArgs e)
    {
        bool isPkg  = e.Data.Contains(PkgDragKey);
        bool isPage = e.Data.Contains("ShowCast.Page");

        if (!isPkg && !isPage) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Move;

        var item = FindListBoxItem(e.Source as Visual);
        if (item != _dragOverItem)
        {
            ClearDragOver();
            _dragOverItem = item;
            if (_dragOverItem is not null) _dragOverItem.Classes.Add("page-drop-target");
        }
        e.Handled = true;
    }

    void OnItemDragLeave(object? sender, DragEventArgs e) => ClearDragOver();

    void OnItemDrop(object? sender, DragEventArgs e)
    {
        ClearDragOver();

        if (e.Data.Contains(PkgDragKey))
        {
            var src = e.Data.Get(PkgDragKey) as Package;
            var tgt = FindItemDataContext<Package>(e.Source);
            if (src is null || tgt is null || src == tgt || VM is null) return;

            int from = VM.PackageItems.IndexOf(src);
            int to   = VM.PackageItems.IndexOf(tgt);
            VM.MoveRundownEntry(from, to);
            e.Handled = true;
            return;
        }

        if (e.Data.Contains("ShowCast.Page"))
        {
            var pvm     = e.Data.Get("ShowCast.Page") as PageViewModel;
            var package = FindItemDataContext<Package>(e.Source);
            if (pvm is null || package is null || VM is null) return;
            VM.MovePageToPackage(pvm, package);
            e.Handled = true;
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void ClearDragOver()
    {
        if (_dragOverItem is not null) _dragOverItem.Classes.Remove("page-drop-target");
        _dragOverItem = null;
    }

    ListBoxItem? FindListBoxItem(Visual? v)
    {
        while (v is not null)
        {
            if (v is ListBoxItem item) return item;
            v = v.GetVisualParent();
        }
        return null;
    }

    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;
        var package = FindItemDataContext<Package>(e.Source);
        if (package is null) return;

        var idx = VM.PackageItems.IndexOf(package);
        if (idx >= 0) VM.SelectedPackageItemIndex = idx;

        var menu = new ContextMenu();

        if (VM.ShowingRundown)
        {
            var moveUp = new MenuItem { Header = "Move Up" };
            moveUp.Click += (_, _) => { if (idx >= 0) { VM.SelectedPackageItemIndex = idx; VM.MoveRundownItem(-1); } };
            var moveDown = new MenuItem { Header = "Move Down" };
            moveDown.Click += (_, _) => { if (idx >= 0) { VM.SelectedPackageItemIndex = idx; VM.MoveRundownItem(1); } };
            menu.Items.Add(moveUp);
            menu.Items.Add(moveDown);
            menu.Items.Add(new Separator());
        }

        var remove = new MenuItem { Header = "Remove" };
        remove.Click += (_, _) =>
        {
            if (VM.ShowingShow)
                VM.RemovePackageFromShow(package);
            else if (VM.ShowingRundown && idx >= 0)
                VM.RemovePackageFromRundown(idx);
        };
        menu.Items.Add(remove);

        menu.Open(e.Source as Control ?? (Control)sender!);
        e.Handled = true;
    }

    static T? FindItemDataContext<T>(object? source) where T : class
    {
        var vis = source as Visual;
        while (vis is not null)
        {
            if (vis is Control c && c.DataContext is T t) return t;
            vis = vis.GetVisualParent();
        }
        return null;
    }
}
