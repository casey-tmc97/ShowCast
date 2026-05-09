using Avalonia.Controls;
using Avalonia.Interactivity;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class PageListPanel : UserControl
{
    public PageListPanel() => InitializeComponent();

    MainViewModel? VM => DataContext as MainViewModel;

    void OnClear(object? sender, RoutedEventArgs e) => VM?.ClearLive();
}
