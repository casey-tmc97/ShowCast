using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class ShowPanel : UserControl
{
    public ShowPanel() => InitializeComponent();

    MainViewModel? VM => DataContext as MainViewModel;

    async void OnAddFolder(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new TextInputDialog("New Folder", "Folder name:");
        var name = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);
        if (!string.IsNullOrWhiteSpace(name))
            VM.AddShow(name.Trim());
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

    void OnContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;
        var show = FindItemDataContext<Show>(e.Source);
        if (show is null) return;
        VM.SelectedShow = show;

        var removeItem = new MenuItem { Header = "Remove" };
        removeItem.Click += (_, _) => VM.RemoveSelectedShow();
        var menu = new ContextMenu();
        menu.Items.Add(removeItem);
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
