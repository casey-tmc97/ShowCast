using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class BottomBar : UserControl
{
    public BottomBar() => InitializeComponent();

    MainViewModel? VM => DataContext as MainViewModel;

    void OnAddSlide(object? sender, RoutedEventArgs e) => VM?.AddSlide();

    void OnGridView(object? sender, RoutedEventArgs e)
    {
        if (VM is not null) VM.ViewMode = PageViewMode.Grid;
    }

    void OnTableView(object? sender, RoutedEventArgs e)
    {
        if (VM is not null) VM.ViewMode = PageViewMode.Table;
    }
}
