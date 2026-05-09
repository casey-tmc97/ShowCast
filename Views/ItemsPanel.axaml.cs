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
    ListBoxItem? _dragOverItem;

    public ItemsPanel()
    {
        InitializeComponent();
        ItemList.AddHandler(DragDrop.DragOverEvent,  OnPageDragOver);
        ItemList.AddHandler(DragDrop.DragLeaveEvent, OnPageDragLeave);
        ItemList.AddHandler(DragDrop.DropEvent,      OnPageDrop);
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

    // ── Page cross-package drag-drop ──────────────────────────────────────────

    void OnPageDragOver(object? sender, DragEventArgs e)
    {
        if (!e.Data.Contains("ShowCast.Page")) { e.DragEffects = DragDropEffects.None; return; }
        e.DragEffects = DragDropEffects.Move;

        var item = FindListBoxItem(e.Source as Visual);
        if (item != _dragOverItem)
        {
            if (_dragOverItem is not null) _dragOverItem.Classes.Remove("page-drop-target");
            _dragOverItem = item;
            if (_dragOverItem is not null) _dragOverItem.Classes.Add("page-drop-target");
        }
        e.Handled = true;
    }

    void OnPageDragLeave(object? sender, DragEventArgs e)
    {
        if (_dragOverItem is not null) _dragOverItem.Classes.Remove("page-drop-target");
        _dragOverItem = null;
    }

    void OnPageDrop(object? sender, DragEventArgs e)
    {
        if (_dragOverItem is not null) _dragOverItem.Classes.Remove("page-drop-target");
        _dragOverItem = null;

        if (!e.Data.Contains("ShowCast.Page")) return;
        var pvm     = e.Data.Get("ShowCast.Page") as PageViewModel;
        var package = FindItemDataContext<Package>(e.Source);
        if (pvm is null || package is null || VM is null) return;

        VM.MovePageToPackage(pvm, package);
        e.Handled = true;
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
