using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace ShowCast.Core;

public enum ServerStatus { Stopped, Listening, Error }

public record CompanionCommand(string Type, JsonElement Raw);

public class CompanionServer : IDisposable
{
    public ServerStatus Status       { get; private set; } = ServerStatus.Stopped;
    public string?      ErrorMessage { get; private set; }

    public event EventHandler<ServerStatus>?     StatusChanged;
    public event EventHandler<CompanionCommand>? CommandReceived;

    readonly object        _clientsLock = new();
    readonly List<Session> _clients     = new();
    TcpListener?             _listener;
    CancellationTokenSource? _cts;
    string _password = "";

    // ── Public API ────────────────────────────────────────────────────────────

    public void Start(NetworkSettings settings)
    {
        Stop();
        _password = settings.TcpPassword ?? "";

        var bindAddress = ResolveBindAddress(settings.BindAdapterName);
        _cts      = new CancellationTokenSource();
        _listener = new TcpListener(bindAddress, settings.TcpPort);

        try
        {
            _listener.Start();
        }
        catch (SocketException ex)
        {
            SetStatus(ServerStatus.Error, ex.Message);
            _cts.Dispose();
            _cts = null;
            return;
        }

        SetStatus(ServerStatus.Listening);
        _ = AcceptLoopAsync(_cts.Token);
    }

    public void Stop()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _cts = null;

        _listener?.Stop();
        _listener = null;

        lock (_clientsLock)
        {
            foreach (var s in _clients) s.Close();
            _clients.Clear();
        }

        SetStatus(ServerStatus.Stopped);
    }

    public void Restart(NetworkSettings settings) { Stop(); Start(settings); }

    public void PushState(string json)
    {
        string line = json.TrimEnd('\n') + "\n";
        List<Session> targets;
        lock (_clientsLock)
            targets = _clients.Where(c => c.IsAuthenticated).ToList();
        foreach (var s in targets) s.Send(line);
    }

    public void Dispose() => Stop();

    // ── Accept loop ───────────────────────────────────────────────────────────

    async Task AcceptLoopAsync(CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var tcp     = await _listener!.AcceptTcpClientAsync(token);
                var session = new Session(tcp);
                lock (_clientsLock) _clients.Add(session);
                _ = HandleSessionAsync(session, token);
            }
            catch (OperationCanceledException) { break; }
            catch (SocketException ex) when (!token.IsCancellationRequested)
            {
                SetStatus(ServerStatus.Error, ex.Message);
                break;
            }
        }
    }

    async Task HandleSessionAsync(Session session, CancellationToken token)
    {
        try
        {
            // StreamReader(stream, encoding, detectBom, bufferSize, leaveOpen)
            using var reader = new StreamReader(session.GetStream(), Encoding.UTF8,
                detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
            string? line;
            while (!token.IsCancellationRequested &&
                   (line = await reader.ReadLineAsync(token)) is not null)
            {
                ProcessLine(session, line);
            }
        }
        catch { }
        finally
        {
            session.Close();
            lock (_clientsLock) _clients.Remove(session);
        }
    }

    void ProcessLine(Session session, string line)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(line); }
        catch
        {
            session.Send("{\"type\":\"error\",\"message\":\"Invalid JSON\"}\n");
            return;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                session.Send("{\"type\":\"error\",\"message\":\"Missing type field\"}\n");
                return;
            }

            string type = typeEl.GetString() ?? "";

            if (!session.IsAuthenticated)
            {
                if (type == "auth")
                {
                    string pw = doc.RootElement.TryGetProperty("password", out var pwEl)
                        ? pwEl.GetString() ?? "" : "";
                    if (_password == "" || pw == _password)
                    {
                        session.IsAuthenticated = true;
                        session.Send("{\"type\":\"auth_ok\"}\n");
                    }
                    else
                    {
                        session.Send("{\"type\":\"auth_fail\"}\n");
                    }
                }
                else
                {
                    session.Send("{\"type\":\"error\",\"message\":\"Not authenticated\"}\n");
                }
                return;
            }

            // Authenticated — raise event with a cloned (document-independent) element.
            CommandReceived?.Invoke(this, new CompanionCommand(type, doc.RootElement.Clone()));
        }
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    static IPAddress ResolveBindAddress(string adapterName)
    {
        if (string.IsNullOrEmpty(adapterName)) return IPAddress.Any;

        var adapter = NetworkInterface.GetAllNetworkInterfaces()
            .Where(n => n.OperationalStatus == OperationalStatus.Up)
            .FirstOrDefault(n => n.Name == adapterName);

        return adapter?.GetIPProperties().UnicastAddresses
            .FirstOrDefault(a => a.Address.AddressFamily == AddressFamily.InterNetwork)
            ?.Address ?? IPAddress.Any;
    }

    void SetStatus(ServerStatus s, string? msg = null)
    {
        Status       = s;
        ErrorMessage = msg;
        StatusChanged?.Invoke(this, s);
    }

    // ── Session ───────────────────────────────────────────────────────────────

    sealed class Session
    {
        readonly TcpClient    _tcp;
        readonly Stream       _stream;
        readonly StreamWriter _writer;
        readonly object       _writeLock = new();
        int _closed; // 0 = open, 1 = closed; use Interlocked for atomic close

        public bool IsAuthenticated { get; set; }

        public Session(TcpClient tcp)
        {
            _tcp    = tcp;
            _stream = tcp.GetStream();
            _writer = new StreamWriter(_stream, Encoding.UTF8, bufferSize: -1, leaveOpen: true) { AutoFlush = true };
        }

        public Stream GetStream() => _stream;

        public void Send(string line)
        {
            if (_closed != 0) return;
            lock (_writeLock)
            {
                if (_closed != 0) return;
                try { _writer.Write(line); }
                catch { }
            }
        }

        public void Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0) return;
            try { _writer.Dispose(); } catch { }
            try { _tcp.Close(); }     catch { }
        }
    }
}
