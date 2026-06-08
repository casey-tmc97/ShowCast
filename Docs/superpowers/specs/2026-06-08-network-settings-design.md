# Network Settings — Design Spec
**Date:** 2026-06-08
**Branch:** feature/network-settings

## Goal

Expose ShowCast to Bitfocus Companion via a persistent TCP connection using a JSON-per-line protocol. Companion can trigger page, rundown, audio, scheduler, and output actions, and receive live state feedback for button indicators.

---

## Architecture

Three new components:

### `Core/NetworkSettings.cs`
New class added to `AppSettings`. Persists via existing `ShowFile` JSON serialization.

```csharp
public class NetworkSettings
{
    public bool TcpEnabled { get; set; } = false;
    public int TcpPort { get; set; } = 5100;
    public string TcpPassword { get; set; } = "";
    public string BindAdapterName { get; set; } = ""; // empty = first available
}
```

### `Core/CompanionServer.cs`
Owns a `TcpListener` bound to the selected adapter IP and port. Manages multiple concurrent client sessions. Each session runs an auth state machine (unauthenticated → authenticated). Dispatches authenticated commands to `MainViewModel`. Pushes state JSON to all authenticated clients on state changes.

Lifecycle:
- `Start()` — binds listener, begins accepting clients
- `Stop()` — closes all connections, disposes listener
- `Restart()` — Stop + Start; called by `MainViewModel` when settings are applied
- Exposes a reactive `ServerStatus` property (`Stopped | Listening | Error(msg)`) consumed by the settings dialog

`MainViewModel` owns the `CompanionServer` instance, starts it on show load if `TcpEnabled`, and stops it on dispose.

### `Views/NetworkSettingsDialog.axaml` + `ViewModels/NetworkSettingsViewModel.cs`
Modal dialog following the `ScreenConfigDialog` / `AudioSettingsDialog` pattern. Added to the Settings menu as "Network".

---

## Settings UI

```
┌─ Network Settings ──────────────────────────────┐
│                                                  │
│  TCP Remote Control                              │
│  ┌──────────────────────────────────────────┐   │
│  │  Enabled       [ toggle ]                │   │
│  │  Adapter       [ Ethernet (192.168.1.42) ▼] │
│  │  Port          [ 5100        ]            │   │
│  │  Password      [ ••••••••    ]            │   │
│  └──────────────────────────────────────────┘   │
│                                                  │
│  Status: ● Listening on port 5100  (or ○ Off)   │
│                                                  │
│                        [ Cancel ]  [ Apply ]     │
└──────────────────────────────────────────────────┘
```

- **Adapter dropdown** — populated from active `NetworkInterface` entries with an assigned IPv4 address; label format `Name (IP)`. Always shown; single-item if only one adapter. Persisted by adapter name; resolved to IP at server start.
- **Port field** — validates integer 1024–65535; shows inline error if invalid.
- **Password field** — masked text box; empty = no authentication required.
- **Status indicator** — live reactive binding to `CompanionServer.ServerStatus`; shows `● Listening on port N`, `○ Stopped`, or `⚠ Error: <msg>`.
- **Apply** — saves to `AppSettings`, calls `MainViewModel.RestartCompanionServer()`.

---

## Protocol

All messages are UTF-8 JSON, one object per line (`\n`).

### Auth handshake
Must complete before any command is accepted.

```jsonc
// client → server
{"type":"auth","password":"secret"}

// server → client
{"type":"auth_ok"}
{"type":"auth_fail"}
```

If `TcpPassword` is empty, any `auth` message succeeds.

### Commands (client → server, post-auth)

| Message | Description |
|---|---|
| `{"type":"page_live","pageId":"<guid>"}` | Take a specific page live |
| `{"type":"page_advance"}` | Advance to next page |
| `{"type":"page_back"}` | Go back one page |
| `{"type":"page_clear"}` | Clear the live output |
| `{"type":"rundown_next"}` | Advance rundown position |
| `{"type":"rundown_goto","index":3}` | Jump to rundown item by index |
| `{"type":"audio_play","id":"<guid>"}` | Trigger an audio cue |
| `{"type":"audio_stop"}` | Stop all audio |
| `{"type":"scheduler_start"}` | Start the scheduler |
| `{"type":"scheduler_stop"}` | Stop the scheduler |
| `{"type":"output_blank","outputId":"<guid>"}` | Blank a specific output |
| `{"type":"output_unblank","outputId":"<guid>"}` | Unblank a specific output |
| `{"type":"get_state"}` | Request a full state snapshot |

### Ack (server → client)

```jsonc
{"type":"ack","cmd":"page_live","status":"ok"}
{"type":"ack","cmd":"page_live","status":"error","message":"Page not found"}
```

### State push (server → all authenticated clients)

Sent whenever ShowCast state changes, and in response to `get_state`.

```jsonc
{
  "type": "state",
  "page":      {"id":"<guid>","name":"Welcome"},
  "rundown":   {"pos":2,"total":10,"currentName":"Item 2"},
  "audio":     {"playing":true,"trackName":"Intro Music"},
  "scheduler": {"running":false},
  "outputs":   [{"id":"<guid>","name":"Main","blanked":false}]
}
```

---

## Error Handling

| Scenario | Behavior |
|---|---|
| Port already in use | `CompanionServer` catches `SocketException`, sets `ServerStatus.Error`; surfaced in dialog status line and MainWindow status bar |
| Client disconnects | Session cleaned up silently; no effect on ShowCast |
| Invalid JSON from client | Server sends `{"type":"error","message":"Invalid message"}`; connection stays open |
| Command fails (e.g. bad ID) | `ack` with `"status":"error"` and human-readable message |
| Adapter goes away | Socket error caught; `ServerStatus.Error` set; user re-applies settings to restart |
| ShowCast shutdown | `MainViewModel.Dispose` calls `CompanionServer.Stop()`; all client connections closed cleanly |

---

## Out of Scope

- Web-based remote UI (no HTTP server)
- Stage display
- mDNS/Bonjour discovery
- Official Companion module (Companion's Generic TCP module is sufficient to start)
