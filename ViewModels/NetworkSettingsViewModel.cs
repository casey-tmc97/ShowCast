using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

public class NetworkSettingsViewModel : ReactiveObject
{
    readonly MainViewModel _main;

    public NetworkSettingsViewModel(MainViewModel main)
    {
        _main = main;

        var net = main.ShowFile.Settings.Network;
        TcpEnabled      = net.TcpEnabled;
        TcpPort         = net.TcpPort;
        TcpPassword     = net.TcpPassword;

        RefreshAdapters();

        // Select stored adapter by name; fall back to first
        int stored = Adapters.IndexOf(Adapters.FirstOrDefault(a => a.Name == net.BindAdapterName) ?? Adapters.FirstOrDefault()!);
        SelectedAdapterIndex = stored >= 0 ? stored : 0;
    }

    // ── Properties ────────────────────────────────────────────────────────────

    bool _tcpEnabled;
    public bool TcpEnabled
    {
        get => _tcpEnabled;
        set => this.RaiseAndSetIfChanged(ref _tcpEnabled, value);
    }

    int _tcpPort;
    public int TcpPort
    {
        get => _tcpPort;
        set => this.RaiseAndSetIfChanged(ref _tcpPort, value);
    }

    string _tcpPassword = "";
    public string TcpPassword
    {
        get => _tcpPassword;
        set => this.RaiseAndSetIfChanged(ref _tcpPassword, value);
    }

    public List<AdapterEntry> Adapters { get; } = new();

    int _selectedAdapterIndex;
    public int SelectedAdapterIndex
    {
        get => _selectedAdapterIndex;
        set => this.RaiseAndSetIfChanged(ref _selectedAdapterIndex, value);
    }

    public AdapterEntry? SelectedAdapter =>
        _selectedAdapterIndex >= 0 && _selectedAdapterIndex < Adapters.Count
            ? Adapters[_selectedAdapterIndex] : null;

    string _portError = "";
    public string PortError
    {
        get => _portError;
        set => this.RaiseAndSetIfChanged(ref _portError, value);
    }

    // ── Apply ─────────────────────────────────────────────────────────────────

    public bool Validate()
    {
        if (TcpPort < 1024 || TcpPort > 65535)
        {
            PortError = "Port must be 1024–65535";
            return false;
        }
        PortError = "";
        return true;
    }

    public void Apply()
    {
        if (!Validate()) return;

        var net = _main.ShowFile.Settings.Network;
        net.TcpEnabled      = TcpEnabled;
        net.TcpPort         = TcpPort;
        net.TcpPassword     = TcpPassword;
        net.BindAdapterName = SelectedAdapter?.Name ?? "";

        _main.RestartCompanionServer();
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    void RefreshAdapters()
    {
        Adapters.Clear();
        foreach (var nic in NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up &&
                        n.NetworkInterfaceType != NetworkInterfaceType.Loopback))
        {
            var ip = nic.GetIPProperties().UnicastAddresses
                .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (ip is not null)
                Adapters.Add(new AdapterEntry(nic.Name, ip.ToString()));
        }
        if (Adapters.Count == 0)
            Adapters.Add(new AdapterEntry("", "No adapters found"));
    }
}

public record AdapterEntry(string Name, string IpAddress)
{
    public string Display => string.IsNullOrEmpty(IpAddress)
        ? Name
        : $"{Name} ({IpAddress})";
}
