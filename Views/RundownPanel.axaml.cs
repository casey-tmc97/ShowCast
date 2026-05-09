using System;
using System.Collections.Generic;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ReactiveUI;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class RundownPanel : UserControl
{
    readonly List<IDisposable> _subs = new();
    bool _syncing;

    public RundownPanel() => InitializeComponent();

    MainViewModel? VM => DataContext as MainViewModel;

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        foreach (var s in _subs) s.Dispose();
        _subs.Clear();

        var vm = DataContext as MainViewModel;
        if (vm is null) return;

        // Keep list selection in sync when SelectedRundown changes from outside
        _subs.Add(vm.WhenAnyValue(x => x.SelectedRundown).Subscribe(rd =>
        {
            _syncing = true;
            PlaylistTreeItem? match = null;
            if (rd is not null)
                foreach (var item in vm.RundownTree)
                    if (item.Rundown == rd) { match = item; break; }
            TreeList.SelectedItem = match;
            _syncing = false;
        }));
    }

    // ── Selection ─────────────────────────────────────────────────────────────

    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncing || VM is null) return;
        if (TreeList.SelectedItem is not PlaylistTreeItem item) return;

        if (item.IsFolder)
        {
            // Toggle folder without changing rundown selection
            VM.ToggleFolderExpanded(item.Folder!);
            // Keep the previously selected rundown row highlighted if still visible
            _syncing = true;
            PlaylistTreeItem? match = null;
            if (VM.SelectedRundown is not null)
                foreach (var ti in VM.RundownTree)
                    if (ti.Rundown == VM.SelectedRundown) { match = ti; break; }
            TreeList.SelectedItem = match;
            _syncing = false;
        }
        else
        {
            VM.SelectedRundown = item.Rundown;
        }
    }

    // ── Add buttons ───────────────────────────────────────────────────────────

    async void OnAddRundown(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new TextInputDialog("New Rundown", "Name:");
        var name = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);
        if (!string.IsNullOrWhiteSpace(name))
        {
            // If a folder row is currently selected, add inside that folder
            Guid? folderId = (TreeList.SelectedItem as PlaylistTreeItem)?.Folder?.Id;
            VM.AddRundown(name.Trim(), folderId);
        }
    }

    async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new TextInputDialog("New Folder", "Name:");
        var name = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);
        if (!string.IsNullOrWhiteSpace(name))
        {
            // Nest inside an already-selected folder if applicable
            Guid? parentId = (TreeList.SelectedItem as PlaylistTreeItem)?.Folder?.Id;
            VM.AddRundownFolder(name.Trim(), parentId);
        }
    }

    // ── Context menu ──────────────────────────────────────────────────────────

    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;
        var item = FindTreeItem(e.Source as Avalonia.Visual);
        if (item is null) return;

        var menu = new ContextMenu();

        if (item.IsFolder)
        {
            var folder = item.Folder!;

            var addRundownHere = new MenuItem { Header = "Add Rundown Here" };
            addRundownHere.Click += async (_, _) =>
            {
                var dlg = new TextInputDialog("New Rundown", "Name:");
                var name = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
                if (!string.IsNullOrWhiteSpace(name))
                    VM.AddRundown(name.Trim(), folder.Id);
            };

            var renameFolder = new MenuItem { Header = "Rename Folder…" };
            renameFolder.Click += async (_, _) =>
            {
                var dlg = new TextInputDialog("Rename Folder", "Name:", folder.Name);
                var name = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
                if (!string.IsNullOrWhiteSpace(name))
                    VM.RenameRundownFolder(folder, name.Trim());
            };

            var deleteFolder = new MenuItem { Header = "Delete Folder" };
            deleteFolder.Click += (_, _) => VM.RemoveRundownFolder(folder);

            menu.Items.Add(addRundownHere);
            menu.Items.Add(new Separator());
            menu.Items.Add(renameFolder);
            menu.Items.Add(deleteFolder);
        }
        else
        {
            var rd = item.Rundown!;
            VM.SelectedRundown = rd;

            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) =>
            {
                var dlg = new TextInputDialog("Rename Rundown", "Name:", rd.Name);
                var name = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
                if (!string.IsNullOrWhiteSpace(name))
                    VM.RenameRundown(rd, name.Trim());
            };

            var removeItem = new MenuItem { Header = "Delete" };
            removeItem.Click += (_, _) => VM.RemoveSelectedRundown();

            menu.Items.Add(renameItem);
            menu.Items.Add(new Separator());
            menu.Items.Add(removeItem);
        }

        menu.Open(e.Source as Control ?? (Control)sender!);
        e.Handled = true;
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static PlaylistTreeItem? FindTreeItem(Avalonia.Visual? vis)
    {
        while (vis is not null)
        {
            if (vis is Control c && c.DataContext is PlaylistTreeItem item) return item;
            vis = vis.GetVisualParent();
        }
        return null;
    }
}
