# Bitfocus Companion Module — ShowCast

**Date:** 2026-06-08
**Branch context:** feature/network-settings (TCP server already merged to master)

---

## Overview

A Bitfocus Companion v3 module that connects to ShowCast's TCP server and exposes actions, feedbacks, and variables for full show control from a Companion panel (Stream Deck, X-keys, web buttons, etc.).

The module lives in a separate GitHub repository: `companion-module-showcast`.

---

## Protocol Summary

ShowCast's TCP server (port 5100 by default) uses newline-delimited JSON.

**Auth handshake:**
```
→ {"type":"auth","password":"..."}
← {"type":"auth_ok"}          // or {"type":"auth_fail"}
```

**Commands:** client sends a JSON object with a `type` field; server responds with an ack then always broadcasts the full state.

**State push** (after every command and on any internal ShowCast state change):
```json
{
  "type": "state",
  "page": {"id": "...", "name": "..."} | null,
  "rundown": {"pos": 0, "total": 5, "currentName": "..."},
  "audio": {
    "playing": true,
    "trackName": "...",
    "playlists": [{"id": "...", "name": "..."}]
  },
  "scheduler": {"running": true},
  "outputs": [{"id": "...", "name": "...", "blanked": false}]
}
```

> **Required C# change:** `BuildCompanionState()` in `MainViewModel.cs` must add `playlists: [{id, name}]` inside the `audio` object. This is the only ShowCast change required by this module.

---

## Architecture

### Tech Stack

- **Language:** TypeScript
- **SDK:** `@companion-module/base` (Companion v3)
- **Transport:** Node.js built-in `net` module
- **Build:** standard Companion v3 toolchain (`tsc`, ESLint, `companion/manifest.json`)

### Repository Layout

```
companion-module-showcast/
├── src/
│   ├── main.ts         — InstanceBase subclass; init/config/destroy orchestration
│   ├── connection.ts   — TCP socket, line buffer, auth handshake, reconnect loop
│   ├── actions.ts      — Action definitions and send logic
│   ├── feedbacks.ts    — Feedback definitions and evaluation
│   ├── variables.ts    — Variable definitions and update logic
│   └── types.ts        — ShowCastState interface and shared types
├── companion/
│   └── manifest.json   — Module metadata (id, name, version, etc.)
├── package.json
├── tsconfig.json
└── .eslintrc.json
```

### Module Config Fields

| Field | Type | Default | Description |
|-------|------|---------|-------------|
| `host` | string | `127.0.0.1` | ShowCast host |
| `port` | number | `5100` | TCP port |
| `password` | string | `""` | Auth password (empty = no auth) |

### Connection Lifecycle

1. On config save (or module init): open TCP to `host:port`
2. Send `{"type":"auth","password":"<password>"}` immediately on connect
3. On `auth_ok`: send `{"type":"get_state"}` to prime initial state; set instance status → **OK**
4. On state message: update all variables, re-check all feedbacks, refresh dynamic choices
5. On disconnect: set status → **Connecting**; retry with exponential backoff, capped at 30 s
6. On `auth_fail`: set status → **Error** ("Authentication failed"); do not retry until config changes

All inbound data is accumulated in a line buffer; complete lines are parsed as JSON before processing.

---

## Actions

All 13 TCP commands are exposed as actions. Dynamic dropdowns (outputs, playlists) are populated from the most recent state push and refreshed on every subsequent push.

| Action ID | Label | Options |
|-----------|-------|---------|
| `page_advance` | Go Live & Advance | — |
| `page_back` | Page Back | — |
| `page_clear` | Clear Live | — |
| `page_live` | Go Live: Specific Page | **Page ID** — text field (UUID); user copies from ShowCast right-click menu |
| `rundown_next` | Rundown: Next | — |
| `rundown_goto` | Rundown: Go To Index | **Index** — number field (0-based) |
| `audio_play` | Audio: Play Playlist | **Playlist** — dynamic dropdown populated from `state.audio.playlists` |
| `audio_stop` | Audio: Stop All | — |
| `scheduler_start` | Scheduler: Start | — |
| `scheduler_stop` | Scheduler: Stop | — |
| `output_blank` | Output: Blank | **Output** — dynamic dropdown populated from `state.outputs` |
| `output_unblank` | Output: Unblank | **Output** — dynamic dropdown populated from `state.outputs` |
| `get_state` | Refresh State | — |

Every action sends its command over the TCP connection. ShowCast always responds with an ack + full state push, so variables and feedbacks update automatically without extra polling.

---

## Feedbacks

Four boolean-style feedbacks. Each re-evaluates on every state push. Foreground and background colors are user-configurable per feedback instance.

| Feedback ID | Label | Condition | Default style |
|-------------|-------|-----------|---------------|
| `page_is_live` | Page Is Live | `state.page !== null` | bg green |
| `audio_is_playing` | Audio Playing | `state.audio.playing === true` | bg green |
| `scheduler_is_running` | Scheduler Running | `state.scheduler.running === true` | bg blue |
| `output_is_blanked` | Output Blanked | selected output's `blanked === true` | bg red |

`output_is_blanked` includes a dynamic dropdown option to select which output to monitor (same list as the blank/unblank actions).

---

## Variables

Eight variables updated on every state push. Embedded in button labels with `$(showcast:variable_name)`.

| Variable ID | Description | Example value |
|-------------|-------------|---------------|
| `live_page_name` | Name of the current live page; empty when none | `"Welcome Slide"` |
| `live_page_id` | UUID of the current live page; empty when none | `"abc-123..."` |
| `rundown_position` | Current rundown position, 1-based | `"3"` |
| `rundown_total` | Total items in the rundown | `"12"` |
| `rundown_current_name` | Name of the currently selected rundown item | `"Service Opening"` |
| `audio_track_name` | Currently playing track name; empty when stopped | `"How Great Thou Art"` |
| `audio_playing` | Whether audio is playing | `"true"` / `"false"` |
| `scheduler_running` | Whether the scheduler is active | `"true"` / `"false"` |

---

## Out of Scope

- Button presets (can be added later as a follow-on)
- `page_live` UUID right-click copy in ShowCast (tracked separately)
- Any Companion v2 compatibility
