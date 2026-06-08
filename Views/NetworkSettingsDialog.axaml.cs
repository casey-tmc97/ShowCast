using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ShowCast.Core;
using ShowCast.ViewModels;

namespace ShowCast.Views;

public partial class NetworkSettingsDialog : Window
{
    readonly NetworkSettingsViewModel _vm;
    readonly MainViewModel            _main;

    public NetworkSettingsDialog(MainViewModel main)
    {
        InitializeComponent();

        _main = main;
        _vm   = new NetworkSettingsViewModel(main);

        // ItemsSource must be set before DataContext so the SelectedIndex binding
        // fires with items already present, otherwise the initial selection is lost.
        AdapterCombo.ItemsSource = _vm.Adapters.Select(a => a.Display).ToList();
        DataContext = _vm;

        // Subscribe to CompanionServer status changes and show current state
        if (main.Companion is not null)
        {
            main.Companion.StatusChanged += OnServerStatusChanged;
            UpdateStatusText(main.Companion.Status, main.Companion.ErrorMessage);
        }
        else
        {
            UpdateStatusText(ServerStatus.Stopped, null);
        }
    }

    void OnServerStatusChanged(object? sender, ServerStatus status)
    {
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
            UpdateStatusText(status, (sender as CompanionServer)?.ErrorMessage));
    }

    void UpdateStatusText(ServerStatus status, string? error)
    {
        StatusText.Text = status switch
        {
            ServerStatus.Listening => $"● Listening on port {_vm.TcpPort}",
            ServerStatus.Error     => $"⚠ Error: {error}",
            _                      => "○ Stopped"
        };
    }

    void OnApply(object? sender, RoutedEventArgs e) { _vm.Apply(); Close(); }

    void OnCancel(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_main.Companion is not null)
            _main.Companion.StatusChanged -= OnServerStatusChanged;
        base.OnClosed(e);
    }
}
