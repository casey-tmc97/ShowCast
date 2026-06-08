# Network Settings (Companion TCP) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Expose ShowCast to Bitfocus Companion via a persistent TCP JSON-per-line server, with a settings dialog to configure port, adapter, and password.

**Architecture:** A `CompanionServer` class in `Core/` owns the `TcpListener` and client sessions. It raises a `CommandReceived` event that `MainViewModel` handles (dispatched to the UI thread). `MainViewModel` calls `CompanionServer.PushState()` after state changes. Settings are stored in a new `NetworkSettings` class added to `AppSettings`. The `NetworkSettingsDialog` follows the exact pattern of `AudioSettingsDialog`.

**Tech Stack:** .NET 8, `System.Net.Sockets.TcpListener`, `System.Text.Json`, xunit, ReactiveUI, Avalonia

---

## File Map

**Create:**
- `Core/NetworkSettings.cs` — persisted TCP config (port, password, adapter name)
- `Core/CompanionServer.cs` — TcpListener, session management, auth, state broadcast
- `ViewModels/NetworkSettingsViewModel.cs` — adapter enumeration, reactive properties
- `Views/NetworkSettingsDialog.axaml` — settings UI
- `Views/NetworkSettingsDialog.axaml.cs` — dialog code-behind
- `ShowCast.Tests/Core/CompanionServerTests.cs` — unit tests

**Modify:**
- `Core/AppSettings.cs` — add `NetworkSettings Network` property
- `Core/OutputState.cs` — add `Blank()` / `Unblank()` methods
- `ViewModels/MainViewModel.cs` — server lifecycle, command dispatch, state builder
- `Views/MainWindow.axaml` — add "Network" menu item
- `Views/MainWindow.axaml.cs` — add `OnNetworkSettings()` handler

---

## Task 1: NetworkSettings model

**Files:**
- Create: `Core/NetworkSettings.cs`
- Modify: `Core/AppSettings.cs:44-46`
- Test: `ShowCast.Tests/Core/CompanionServerTests.cs`

- [ ] **Step 1: Write the failing test**

Create `ShowCast.Tests/Core/CompanionServerTests.cs`:

```csharp
using System.Text.Json;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class CompanionServerTests
{
    [Fact]
    public void NetworkSettings_Defaults_AreCorrect()
    {
        var s = new NetworkSettings();
        Assert.False(s.TcpEnabled);
        Assert.Equal(5100, s.TcpPort);
        Assert.Equal("", s.TcpPassword);
        Assert.Equal("", s.BindAdapterName);
    }

    [Fact]
    public void AppSettings_ContainsNetworkSettings()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.Network);
        Assert.IsType<NetworkSettings>(settings.Network);
    }
}
```

- [ ] **Step 2: Run test to confirm it fails**

```
dotnet test ShowCast.Tests --filter "CompanionServerTests" -v minimal
```

Expected: FAIL — `NetworkSettings` not defined.

- [ ] **Step 3: Create `Core/NetworkSettings.cs`**

```csharp
namespace ShowCast.Core;

public class NetworkSettings
{
    public bool   TcpEnabled       { get; set; } = false;
    public int    TcpPort          { get; set; } = 5100;
    public string TcpPassword      { get; set; } = "";
    public string BindAdapterName  { get; set; } = "";
}
```

- [ ] **Step 4: Add `Network` property to `Core/AppSettings.cs`**

Add after the `AudioDestinations` line (line 45):

```csharp
    // Network / remote control
    public NetworkSettings Network { get; set; } = new();
```

- [ ] **Step 5: Run tests to confirm they pass**

```
dotnet test ShowCast.Tests --filter "CompanionServerTests" -v minimal
```

Expected: PASS (2 tests).

- [ ] **Step 6: Commit**

```
git add Core/NetworkSettings.cs Core/AppSettings.cs ShowCast.Tests/Core/CompanionServerTests.cs
git commit -m "feat: add NetworkSettings model to AppSettings"
```

---

## Task 2: OutputState Blank / Unblank

**Files:**
- Modify: `Core/OutputState.cs:83-90`
- Test: `ShowCast.Tests/Core/CompanionServerTests.cs` (add tests here)

- [ ] **Step 1: Write failing tests** — append to `ShowCast.Tests/Core/CompanionServerTests.cs`:

```csharp
    [Fact]
    public void OutputState_Blank_ClearsLivePage()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);

        state.Blank();

        Assert.Null(state.LivePage);
    }

    [Fact]
    public void OutputState_Unblank_RestoresLivePage()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);
        state.Blank();

        state.Unblank();

        Assert.Equal(page, state.LivePage);
        Assert.Equal(0, state.LivePageIndex);
    }

    [Fact]
    public void OutputState_Unblank_WithoutPriorBlank_DoesNothing()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);

        state.Unblank(); // must not throw

        Assert.Null(state.LivePage);
    }
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test ShowCast.Tests --filter "CompanionServerTests" -v minimal
```

Expected: FAIL — `OutputState.Blank()` not defined.

- [ ] **Step 3: Add `Blank()` and `Unblank()` to `Core/OutputState.cs`**

Add after the `Clear()` method (after line 90):

```csharp
    Page? _preBlankPage;
    int   _preBlankPageIndex;

    public void Blank()
    {
        _preBlankPage      = LivePage;
        _preBlankPageIndex = LivePageIndex;
        LivePage           = null;
        LivePageIndex      = -1;
    }

    public void Unblank()
    {
        if (_preBlankPage is null) return;
        LivePage           = _preBlankPage;
        LivePageIndex      = _preBlankPageIndex;
        _preBlankPage      = null;
    }
```

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test ShowCast.Tests --filter "CompanionServerTests" -v minimal
```

Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```
git add Core/OutputState.cs ShowCast.Tests/Core/CompanionServerTests.cs
git commit -m "feat: add Blank/Unblank to OutputState"
```

---

## Task 3: CompanionServer

**Files:**
- Create: `Core/CompanionServer.cs`
- Test: `ShowCast.Tests/Core/CompanionServerTests.cs` (append tests)

- [ ] **Step 1: Write failing tests** — append to `ShowCast.Tests/Core/CompanionServerTests.cs`:

```csharp
    [Fact]
    public async Task CompanionServer_Start_StatusBecomesListening()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15100 };

        server.Start(settings);
        await Task.Delay(50);

        Assert.Equal(ServerStatus.Listening, server.Status);
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_AuthWithCorrectPassword_Succeeds()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15101, TcpPassword = "secret" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15101);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":"secret"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("auth_ok", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_AuthWithWrongPassword_Fails()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15102, TcpPassword = "secret" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15102);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":"wrong"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("auth_fail", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_NoPassword_AnyAuthSucceeds()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15103, TcpPassword = "" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15103);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":"anything"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("auth_ok", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_CommandBeforeAuth_ReturnsError()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15104, TcpPassword = "secret" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15104);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"page_advance"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("error", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_PushState_DeliveredToAuthenticatedClient()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15105, TcpPassword = "" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15105);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":""}""");
        await reader.ReadLineAsync(); // consume auth_ok

        server.PushState("""{"type":"state","page":{"name":"Test"}}""");
        string? pushed = await reader.ReadLineAsync();

        Assert.Contains("state", pushed ?? "");
        server.Stop();
    }
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test ShowCast.Tests --filter "CompanionServerTests" -v minimal
```

Expected: FAIL — `CompanionServer` not defined.

- [ ] **Step 3: Create `Core/CompanionServer.cs`**

```csharp
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

    public event EventHandler<ServerStatus>?    StatusChanged;
    public event EventHandler<CompanionCommand>? CommandReceived;

    readonly object        _clientsLock = new();
    readonly List<Session> _clients     = new();
    TcpListener?           _listener;
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
            using var reader = new StreamReader(session.GetStream(), Encoding.UTF8, leaveOpen: true);
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
            session.Send("""{"type":"error","message":"Invalid JSON"}""" + "\n");
            return;
        }

        using (doc)
        {
            if (!doc.RootElement.TryGetProperty("type", out var typeEl))
            {
                session.Send("""{"type":"error","message":"Missing type field"}""" + "\n");
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
                        session.Send("""{"type":"auth_ok"}""" + "\n");
                    }
                    else
                    {
                        session.Send("""{"type":"auth_fail"}""" + "\n");
                    }
                }
                else
                {
                    session.Send("""{"type":"error","message":"Not authenticated"}""" + "\n");
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
        readonly TcpClient   _tcp;
        readonly Stream      _stream;
        readonly StreamWriter _writer;
        bool _closed;

        public bool IsAuthenticated { get; set; }

        public Session(TcpClient tcp)
        {
            _tcp    = tcp;
            _stream = tcp.GetStream();
            _writer = new StreamWriter(_stream, Encoding.UTF8, leaveOpen: true) { AutoFlush = true };
        }

        public Stream GetStream() => _stream;

        public void Send(string line)
        {
            if (_closed) return;
            try { _writer.Write(line); }
            catch { }
        }

        public void Close()
        {
            if (_closed) return;
            _closed = true;
            try { _writer.Dispose(); } catch { }
            try { _tcp.Close(); }    catch { }
        }
    }
}
```

- [ ] **Step 4: Run tests to confirm they pass**

```
dotnet test ShowCast.Tests --filter "CompanionServerTests" -v minimal
```

Expected: PASS (all 11 tests).

- [ ] **Step 5: Commit**

```
git add Core/CompanionServer.cs ShowCast.Tests/Core/CompanionServerTests.cs
git commit -m "feat: add CompanionServer with TCP listener, auth, and state broadcast"
```

---

## Task 4: MainViewModel — command dispatch and state builder

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

This task wires `CompanionServer` into `MainViewModel`. All server methods are called on background threads, so every handler marshals to the UI thread via `Dispatcher.UIThread.InvokeAsync`.

- [ ] **Step 1: Add server field and lifecycle methods**

In `MainViewModel.cs`, locate the field declarations section (near `_schedulerTimer`) and add:

```csharp
    // ── Companion TCP server ──────────────────────────────────────────────────

    CompanionServer? _companion;
```

Add these three methods anywhere in `MainViewModel.cs` (suggested: after `StopAllAjaSenders`):

```csharp
    public void StartCompanionServer()
    {
        var settings = _showFile.Settings.Network;
        if (!settings.TcpEnabled) return;

        _companion ??= new CompanionServer();
        _companion.CommandReceived -= OnCompanionCommand;
        _companion.CommandReceived += OnCompanionCommand;
        _companion.Start(settings);
    }

    public void StopCompanionServer()
    {
        _companion?.Stop();
    }

    public void RestartCompanionServer()
    {
        if (_companion is null)
        {
            StartCompanionServer();
            return;
        }
        _companion.CommandReceived -= OnCompanionCommand;
        _companion.CommandReceived += OnCompanionCommand;

        var settings = _showFile.Settings.Network;
        if (settings.TcpEnabled)
            _companion.Restart(settings);
        else
            _companion.Stop();
    }
```

- [ ] **Step 2: Add `OnCompanionCommand` dispatcher** — add after `RestartCompanionServer`:

```csharp
    void OnCompanionCommand(object? sender, CompanionCommand cmd)
    {
        // Always marshal to UI thread — server fires on a background task.
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() => DispatchCompanionCommand(cmd));
    }

    void DispatchCompanionCommand(CompanionCommand cmd)
    {
        string ack;
        try
        {
            ack = ExecuteCompanionCommand(cmd);
        }
        catch (Exception ex)
        {
            ack = $"{{\"type\":\"ack\",\"cmd\":\"{cmd.Type}\",\"status\":\"error\",\"message\":\"{ex.Message}\"}}";
        }
        _companion?.PushState(ack);
        _companion?.PushState(BuildCompanionState());
    }

    string ExecuteCompanionCommand(CompanionCommand cmd)
    {
        string Ok()    => $"{{\"type\":\"ack\",\"cmd\":\"{cmd.Type}\",\"status\":\"ok\"}}";
        string Err(string msg) => $"{{\"type\":\"ack\",\"cmd\":\"{cmd.Type}\",\"status\":\"error\",\"message\":\"{msg}\"}}";

        switch (cmd.Type)
        {
            case "page_advance":
                GoLiveAndAdvance();
                return Ok();

            case "page_back":
            {
                if (SelectedOutput is null) return Err("No output selected");
                var pkg = SelectedOutput.ActivePackage;
                if (pkg is null) return Err("No active package");
                int idx = SelectedOutput.LivePageIndex - 1;
                if (idx < 0) return Err("Already at first page");
                var page = pkg.Pages[idx];
                SelectedOutput.GoLive(page, idx, NextTransitionType, NextTransitionDuration, 0.5f);
                UpdateIsLiveFlags();
                return Ok();
            }

            case "page_clear":
                ClearLive();
                return Ok();

            case "page_live":
            {
                if (!cmd.Raw.TryGetProperty("pageId", out var pidEl) ||
                    !Guid.TryParse(pidEl.GetString(), out var pageId))
                    return Err("Missing or invalid pageId");

                var page = _showFile.Shows
                    .SelectMany(s => s.Packages)
                    .SelectMany(p => p.Pages)
                    .FirstOrDefault(p => p.Id == pageId);
                if (page is null) return Err("Page not found");

                var pkg = _showFile.Shows
                    .SelectMany(s => s.Packages)
                    .FirstOrDefault(p => p.Pages.Contains(page));
                if (pkg is null || SelectedOutput is null) return Err("Output unavailable");

                SelectedOutput.ActivePackage = pkg;
                int idx = pkg.Pages.IndexOf(page);
                SelectedOutput.GoLive(page, idx, NextTransitionType, NextTransitionDuration, 0.5f);
                UpdateIsLiveFlags();
                return Ok();
            }

            case "rundown_next":
            {
                int next = SelectedPackageItemIndex + 1;
                if (next >= PackageItems.Count) return Err("Already at last rundown item");
                SelectedPackageItemIndex = next;
                return Ok();
            }

            case "rundown_goto":
            {
                if (!cmd.Raw.TryGetProperty("index", out var idxEl))
                    return Err("Missing index");
                int idx = idxEl.GetInt32();
                if (idx < 0 || idx >= PackageItems.Count) return Err("Index out of range");
                SelectedPackageItemIndex = idx;
                return Ok();
            }

            case "audio_play":
            {
                if (!cmd.Raw.TryGetProperty("id", out var idEl) ||
                    !Guid.TryParse(idEl.GetString(), out var playlistId))
                    return Err("Missing or invalid id");

                foreach (var ch in AudioChannels)
                {
                    var playlist = ch.Player.Playlists.FirstOrDefault(p => p.Id == playlistId);
                    if (playlist is not null)
                    {
                        ch.Player.SelectedPlaylist = playlist;
                        ch.Player.Play();
                        return Ok();
                    }
                }
                return Err("Playlist not found");
            }

            case "audio_stop":
                foreach (var ch in AudioChannels) ch.Player.Stop();
                return Ok();

            case "scheduler_start":
                StartSchedulerTimer();
                return Ok();

            case "scheduler_stop":
                _schedulerTimer?.Stop();
                _schedulerTimer?.Dispose();
                _schedulerTimer = null;
                return Ok();

            case "output_blank":
            {
                if (!cmd.Raw.TryGetProperty("outputId", out var oidEl) ||
                    !Guid.TryParse(oidEl.GetString(), out var outputId))
                    return Err("Missing or invalid outputId");
                var output = OutputStates.FirstOrDefault(o => o.Config.Id == outputId);
                if (output is null) return Err("Output not found");
                output.Blank();
                UpdateIsLiveFlags();
                return Ok();
            }

            case "output_unblank":
            {
                if (!cmd.Raw.TryGetProperty("outputId", out var oidEl) ||
                    !Guid.TryParse(oidEl.GetString(), out var outputId))
                    return Err("Missing or invalid outputId");
                var output = OutputStates.FirstOrDefault(o => o.Config.Id == outputId);
                if (output is null) return Err("Output not found");
                output.Unblank();
                UpdateIsLiveFlags();
                return Ok();
            }

            case "get_state":
                return Ok(); // state is always broadcast after ack by DispatchCompanionCommand

            default:
                return Err($"Unknown command: {cmd.Type}");
        }
    }
```

- [ ] **Step 3: Add `BuildCompanionState()`** — add after `ExecuteCompanionCommand`:

```csharp
    public string BuildCompanionState()
    {
        // Live page
        var livePage = SelectedOutput?.LivePage;
        string pageSection = livePage is not null
            ? $"{{\"id\":\"{livePage.Id}\",\"name\":\"{EscapeJson(livePage.Name)}\"}}"
            : "null";

        // Rundown
        var rd = SelectedRundown;
        int pos   = SelectedPackageItemIndex;
        int total = PackageItems.Count;
        string rdName = (pos >= 0 && pos < PackageItems.Count)
            ? EscapeJson(PackageItems[pos].Name) : "";
        string rundownSection = $"{{\"pos\":{pos},\"total\":{total},\"currentName\":\"{rdName}\"}}";

        // Audio (first playing channel)
        string audioSection = "null";
        foreach (var ch in AudioChannels)
        {
            if (ch.Player.State == PlaybackState.Playing)
            {
                var track = ch.Player.CurrentTrack;
                string name = track is not null ? EscapeJson(track.Title) : "";
                audioSection = $"{{\"playing\":true,\"trackName\":\"{name}\"}}";
                break;
            }
        }
        if (audioSection == "null")
            audioSection = "{\"playing\":false,\"trackName\":\"\"}";

        // Scheduler
        bool schedulerRunning = _schedulerTimer is not null;
        string schedulerSection = $"{{\"running\":{(schedulerRunning ? "true" : "false")}}}";

        // Outputs array
        var outputParts = OutputStates.Select(o =>
            $"{{\"id\":\"{o.Config.Id}\",\"name\":\"{EscapeJson(o.Config.Name)}\",\"blanked\":{(o.LivePage == null ? "true" : "false")}}}");
        string outputsSection = "[" + string.Join(",", outputParts) + "]";

        return $"{{\"type\":\"state\",\"page\":{pageSection},\"rundown\":{rundownSection}," +
               $"\"audio\":{audioSection},\"scheduler\":{schedulerSection},\"outputs\":{outputsSection}}}\n";
    }

    static string EscapeJson(string? s) =>
        (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n");
```

- [ ] **Step 4: Hook server into `RebuildFromShowFile()`**

In `RebuildFromShowFile()`, find the three existing `Stop*Senders()` calls (around line 166). Add after `StopAllAjaSenders()`:

```csharp
        StopCompanionServer();
```

Find the end of `RebuildFromShowFile()` where `StartNdiFor`, `StartBlackmagicFor`, `StartAjaFor` are called. Add after those three loops:

```csharp
        StartCompanionServer();
```

- [ ] **Step 5: Hook server into `MainViewModel()` constructor**

Find the constructor `public MainViewModel()` (around line 2005). After `StartSchedulerTimer();` add:

```csharp
        StartCompanionServer();
```

- [ ] **Step 6: Build to check for compile errors**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: Build succeeds with 0 errors. Fix any errors before continuing.

- [ ] **Step 7: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: wire CompanionServer into MainViewModel with full command dispatch"
```

---

## Task 5: NetworkSettingsViewModel

**Files:**
- Create: `ViewModels/NetworkSettingsViewModel.cs`

- [ ] **Step 1: Create `ViewModels/NetworkSettingsViewModel.cs`**

```csharp
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

        // Select stored adapter
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

    // ── Live server status (from CompanionServer) ─────────────────────────────

    string _statusText = "Stopped";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
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
```

- [ ] **Step 2: Build to check for compile errors**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: 0 errors.

- [ ] **Step 3: Commit**

```
git add ViewModels/NetworkSettingsViewModel.cs
git commit -m "feat: add NetworkSettingsViewModel with adapter enumeration"
```

---

## Task 6: NetworkSettingsDialog

**Files:**
- Create: `Views/NetworkSettingsDialog.axaml`
- Create: `Views/NetworkSettingsDialog.axaml.cs`

- [ ] **Step 1: Create `Views/NetworkSettingsDialog.axaml`**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="ShowCast.Views.NetworkSettingsDialog"
        Title="Network Settings"
        Width="420" Height="320"
        MinWidth="360" MinHeight="280"
        CanResize="False"
        WindowStartupLocation="CenterOwner"
        Background="#2d2d2d">

    <DockPanel>

        <!-- Footer -->
        <Border DockPanel.Dock="Bottom" Background="#1e1e1e" Padding="12,8"
                BorderBrush="#555555" BorderThickness="0,1,0,0">
            <StackPanel Orientation="Horizontal" Spacing="8" HorizontalAlignment="Right">
                <Button Content="Cancel" Click="OnCancel"
                        Classes="btn-secondary" Width="80"/>
                <Button Content="Apply"  Click="OnApply"
                        Classes="btn-primary"    Width="80"/>
            </StackPanel>
        </Border>

        <!-- Status bar -->
        <Border DockPanel.Dock="Bottom" Background="#1a1a1a" Padding="16,6"
                BorderBrush="#444444" BorderThickness="0,1,0,0">
            <TextBlock x:Name="StatusText"
                       FontSize="12" Foreground="#aaaaaa"/>
        </Border>

        <!-- Body -->
        <ScrollViewer HorizontalScrollBarVisibility="Disabled"
                      VerticalScrollBarVisibility="Auto">
            <Border Padding="20,16">
                <StackPanel Spacing="16">

                    <!-- Section header -->
                    <TextBlock Text="TCP REMOTE CONTROL" FontSize="10" FontWeight="Bold"
                               Foreground="#888888" LetterSpacing="1"/>

                    <!-- Enabled -->
                    <Grid ColumnDefinitions="140,*">
                        <TextBlock Grid.Column="0" Text="Enabled"
                                   VerticalAlignment="Center" Foreground="White" FontSize="13"/>
                        <ToggleSwitch Grid.Column="1"
                                      IsChecked="{Binding TcpEnabled}"
                                      OnContent="" OffContent=""
                                      VerticalAlignment="Center"/>
                    </Grid>

                    <!-- Adapter -->
                    <Grid ColumnDefinitions="140,*">
                        <TextBlock Grid.Column="0" Text="Adapter"
                                   VerticalAlignment="Center" Foreground="White" FontSize="13"/>
                        <ComboBox Grid.Column="1"
                                  x:Name="AdapterCombo"
                                  SelectedIndex="{Binding SelectedAdapterIndex}"
                                  HorizontalAlignment="Stretch"
                                  Background="#3a3a3a" Foreground="White"/>
                    </Grid>

                    <!-- Port -->
                    <StackPanel Spacing="4">
                        <Grid ColumnDefinitions="140,*">
                            <TextBlock Grid.Column="0" Text="Port"
                                       VerticalAlignment="Center" Foreground="White" FontSize="13"/>
                            <NumericUpDown Grid.Column="1"
                                          Value="{Binding TcpPort}"
                                          Minimum="1024" Maximum="65535"
                                          Increment="1" FormatString="0"
                                          Height="34"
                                          Background="#3a3a3a" Foreground="White"/>
                        </Grid>
                        <TextBlock Text="{Binding PortError}"
                                   Foreground="#ff6666" FontSize="11"
                                   IsVisible="{Binding PortError, Converter={x:Static StringConverters.IsNotNullOrEmpty}}"/>
                    </StackPanel>

                    <!-- Password -->
                    <Grid ColumnDefinitions="140,*">
                        <TextBlock Grid.Column="0" Text="Password"
                                   VerticalAlignment="Center" Foreground="White" FontSize="13"/>
                        <TextBox Grid.Column="1"
                                 Text="{Binding TcpPassword}"
                                 PasswordChar="●"
                                 Watermark="Leave blank for no auth"
                                 Background="#3a3a3a" Foreground="White"
                                 BorderBrush="#555555" FontSize="13" Height="34"
                                 VerticalContentAlignment="Center"/>
                    </Grid>

                </StackPanel>
            </Border>
        </ScrollViewer>

    </DockPanel>
</Window>
```

- [ ] **Step 2: Create `Views/NetworkSettingsDialog.axaml.cs`**

```csharp
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

        _main        = main;
        _vm          = new NetworkSettingsViewModel(main);
        DataContext  = _vm;

        // Populate adapter combo display strings
        AdapterCombo.ItemsSource = _vm.Adapters.Select(a => a.Display).ToList();

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

    void OnApply(object? sender, RoutedEventArgs e) => _vm.Apply();

    void OnCancel(object? sender, RoutedEventArgs e) => Close();

    protected override void OnClosed(EventArgs e)
    {
        if (_main.Companion is not null)
            _main.Companion.StatusChanged -= OnServerStatusChanged;
        base.OnClosed(e);
    }
}
```

- [ ] **Step 3: Expose `Companion` property on `MainViewModel`**

In `MainViewModel.cs`, add a public accessor for the companion server (alongside the `_companion` field):

```csharp
    public CompanionServer? Companion => _companion;
```

- [ ] **Step 4: Build to check for compile errors**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: 0 errors. Fix any `using` or namespace issues before continuing.

- [ ] **Step 5: Commit**

```
git add Views/NetworkSettingsDialog.axaml Views/NetworkSettingsDialog.axaml.cs ViewModels/MainViewModel.cs
git commit -m "feat: add NetworkSettingsDialog and expose Companion accessor"
```

---

## Task 7: MainWindow wiring

**Files:**
- Modify: `Views/MainWindow.axaml:29-33`
- Modify: `Views/MainWindow.axaml.cs`

- [ ] **Step 1: Add "Network" menu item to `Views/MainWindow.axaml`**

Find the Settings submenu (lines 29–33):

```xml
                        <MenuItem Header="Settings">
                            <MenuItem Header="Screens"  Click="OnScreenConfig"/>
                            <MenuItem Header="Audio"    Click="OnAudioSettings"/>
                            <MenuItem Header="Schedule" Click="OnScheduler"/>
                        </MenuItem>
```

Replace with:

```xml
                        <MenuItem Header="Settings">
                            <MenuItem Header="Screens"  Click="OnScreenConfig"/>
                            <MenuItem Header="Audio"    Click="OnAudioSettings"/>
                            <MenuItem Header="Schedule" Click="OnScheduler"/>
                            <MenuItem Header="Network"  Click="OnNetworkSettings"/>
                        </MenuItem>
```

- [ ] **Step 2: Add `OnNetworkSettings` handler to `Views/MainWindow.axaml.cs`**

Find the `OnScheduler` handler (around line 212) and add after it:

```csharp
    async void OnNetworkSettings(object? sender, RoutedEventArgs e)
    {
        if (VM is null) return;
        var dialog = new NetworkSettingsDialog(VM);
        await dialog.ShowDialog(this);
    }
```

- [ ] **Step 3: Build to check for compile errors**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: 0 errors.

- [ ] **Step 4: Commit**

```
git add Views/MainWindow.axaml Views/MainWindow.axaml.cs
git commit -m "feat: add Network Settings menu item and dialog handler"
```

---

## Task 8: State push hooks

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

After applying commands, state is already pushed in `DispatchCompanionCommand`. This task adds reactive hooks so that state changes *outside* of Companion commands (e.g., operator clicks) are also broadcast to connected Companion clients.

- [ ] **Step 1: Add `PushStateToCompanion()` helper**

In `MainViewModel.cs`, add after `BuildCompanionState()`:

```csharp
    void PushStateToCompanion() => _companion?.PushState(BuildCompanionState());
```

- [ ] **Step 2: Hook into `GoLive` / `ClearLive` call sites**

Find `GoLive()` method (around line 543). At the end of the method body, add:

```csharp
        PushStateToCompanion();
```

Find `ClearLive()` (around line 689). Change it to:

```csharp
    public void ClearLive() { StopPageTimer(); SelectedOutput?.Clear(); UpdateIsLiveFlags(); PushStateToCompanion(); }
```

- [ ] **Step 3: Hook into audio state changes**

Find `FirePageAudioTrigger` or wherever audio channels' state changes. Instead of hooking deep into audio events, subscribe to `AudioChannels` `CollectionChanged` and each channel's `Player.WhenAnyValue` in the constructor area.

Add at the end of `StartCompanionServer()`:

```csharp
        // Subscribe to audio state changes to push Companion state updates
        foreach (var ch in AudioChannels)
            ch.Player.WhenAnyValue(p => p.State).Subscribe(_ => PushStateToCompanion());
```

- [ ] **Step 4: Build**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: 0 errors.

- [ ] **Step 5: Run all tests**

```
dotnet test ShowCast.Tests -v minimal
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: push Companion state on GoLive, ClearLive, and audio state changes"
```

---

## Self-Review Checklist (run before opening PR)

- [ ] `NetworkSettings` defaults match spec (port 5100, empty password, disabled)
- [ ] `AppSettings.Network` serializes/deserializes correctly through `ShowFileSerializer`
- [ ] `CompanionServer` binds to `IPAddress.Any` when `BindAdapterName` is empty
- [ ] Auth with empty password accepts any client (no-auth mode)
- [ ] All 12 command types return an `ack` message
- [ ] `get_state` returns a full state object
- [ ] `output_blank` / `output_unblank` round-trip correctly (test manually)
- [ ] Dialog status indicator updates when server starts/stops
- [ ] Server stops on `RebuildFromShowFile` and restarts if `TcpEnabled`
- [ ] All existing tests still pass: `dotnet test ShowCast.Tests -v minimal`
