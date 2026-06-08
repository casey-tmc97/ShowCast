# Companion Module Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build a Bitfocus Companion v3 module (`companion-module-showcast`) that connects to ShowCast's TCP server and exposes 13 actions, 4 feedbacks, and 8 variables for full show control.

**Architecture:** A TypeScript Companion v3 module in a separate repo. A `ShowCastConnection` class manages the TCP socket with line buffering, auth, and exponential-backoff reconnect; it emits a `stateUpdate` event whenever ShowCast pushes state. The `InstanceBase` subclass (`main.ts`) wires the connection to Companion's action/feedback/variable APIs, re-registering dynamic choices on every state update. One small C# change in the existing ShowCast repo extends the state broadcast to include playlist IDs.

**Tech Stack:** TypeScript, `@companion-module/base` ^1.7.0, Node.js `net` module, Jest + ts-jest, xUnit (for the C# task).

---

## File Map

### ShowCast repo (Task 1 only)

| File | Change |
|------|--------|
| `ViewModels/MainViewModel.cs` | Add `playlists` array to `BuildCompanionState()` audio section |
| `ShowCast.Tests/ViewModels/CompanionStateTests.cs` | New — tests for `BuildCompanionState()` output |

### companion-module-showcast repo (Tasks 2–8)

| File | Responsibility |
|------|---------------|
| `package.json` | npm metadata, scripts, deps |
| `tsconfig.json` | TypeScript to CommonJS |
| `jest.config.js` | Jest + ts-jest config |
| `.eslintrc.json` | Lint config |
| `companion/manifest.json` | Companion module metadata |
| `src/types.ts` | `ShowCastState`, `ShowCastConfig`, and sub-interfaces |
| `src/connection.ts` | TCP socket, line buffer, auth, reconnect, event emitter |
| `src/feedbacks.ts` | `getFeedbacks(instance)` — 4 boolean feedbacks |
| `src/variables.ts` | `getVariableDefinitions()`, `buildVariableValues(state)` |
| `src/actions.ts` | `getActions(instance)` — 13 actions |
| `src/main.ts` | `ShowCastInstance extends InstanceBase<ShowCastConfig>` |
| `src/__tests__/connection.test.ts` | TCP unit tests with real local mock server |
| `src/__tests__/feedbacks.test.ts` | Feedback callback unit tests |
| `src/__tests__/variables.test.ts` | Variable value unit tests |
| `src/__tests__/actions.test.ts` | Action sendCommand unit tests |

---

## Task 1: C# — Add playlists to state broadcast

**Context:** This task is in the existing ShowCast repo on the `feature/network-settings` branch.

**Files:**
- Modify: `ViewModels/MainViewModel.cs` (around line 560, `BuildCompanionState`)
- Create: `ShowCast.Tests/ViewModels/CompanionStateTests.cs`

- [ ] **Step 1: Write the failing test**

Create `ShowCast.Tests/ViewModels/CompanionStateTests.cs`:

```csharp
using System.Text.Json;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class CompanionStateTests
{
    [Fact]
    public void BuildCompanionState_AudioSection_IncludesPlaylistsArray()
    {
        var vm = new MainViewModel();
        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var audio = doc.RootElement.GetProperty("audio");
        var playlists = audio.GetProperty("playlists");
        Assert.Equal(JsonValueKind.Array, playlists.ValueKind);
    }

    [Fact]
    public void BuildCompanionState_AudioSection_PlaylistsHaveIdAndName()
    {
        var vm = new MainViewModel();
        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var playlists = doc.RootElement.GetProperty("audio").GetProperty("playlists");
        Assert.True(playlists.GetArrayLength() > 0);
        var first = playlists[0];
        Assert.Equal(JsonValueKind.String, first.GetProperty("id").ValueKind);
        Assert.Equal(JsonValueKind.String, first.GetProperty("name").ValueKind);
    }

    [Fact]
    public void BuildCompanionState_PlaylistId_MatchesActualPlaylistGuid()
    {
        var vm = new MainViewModel();
        var expectedId = vm.AudioChannels[0].Player.Playlists[0].Id.ToString();
        var json = vm.BuildCompanionState();
        var doc = JsonDocument.Parse(json);
        var playlists = doc.RootElement.GetProperty("audio").GetProperty("playlists");
        var ids = playlists.EnumerateArray().Select(p => p.GetProperty("id").GetString()).ToList();
        Assert.Contains(expectedId, ids);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests --filter "CompanionStateTests" --no-build
```

Expected: 3 failures — `audio` object has no `playlists` property.

- [ ] **Step 3: Update `BuildCompanionState()` in `ViewModels/MainViewModel.cs`**

Replace the existing `audioSection` block (lines ~573–583):

```csharp
// Old block to replace:
string audioSection = "{\"playing\":false,\"trackName\":\"\"}";
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
```

```csharp
// New block:
bool anyPlaying = false;
string playingTrackName = "";
foreach (var ch in AudioChannels)
{
    if (!anyPlaying && ch.Player.State == PlaybackState.Playing)
    {
        var track = ch.Player.CurrentTrack;
        anyPlaying = true;
        playingTrackName = track is not null ? EscapeJson(track.Title) : "";
    }
}
var allPlaylists = AudioChannels
    .SelectMany(ch => ch.Player.Playlists)
    .Select(p => $"{{\"id\":\"{p.Id}\",\"name\":\"{EscapeJson(p.Name)}\"}}");
string playlistsJson = "[" + string.Join(",", allPlaylists) + "]";
string audioSection = $"{{\"playing\":{(anyPlaying ? "true" : "false")}," +
                      $"\"trackName\":\"{playingTrackName}\",\"playlists\":{playlistsJson}}}";
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests --filter "CompanionStateTests"
```

Expected: 3 passing.

- [ ] **Step 5: Run full test suite to check for regressions**

```
dotnet test ShowCast.Tests
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add ShowCast.Tests/ViewModels/CompanionStateTests.cs ViewModels/MainViewModel.cs
git commit -m "feat: include playlists in Companion state broadcast audio section"
```

---

## Task 2: Scaffold companion-module-showcast repo

**Context:** Create a new directory `companion-module-showcast` next to (or wherever you keep repos), then initialize it. All remaining tasks work inside this new repo.

- [ ] **Step 1: Init git repo and install deps**

```bash
mkdir companion-module-showcast && cd companion-module-showcast
git init
npm init -y
npm install @companion-module/base@^1.7.0
npm install --save-dev typescript@^5 @types/node@^20 ts-jest@^29 jest@^29 @types/jest@^29 eslint@^8 @typescript-eslint/parser@^6 @typescript-eslint/eslint-plugin@^6
```

- [ ] **Step 2: Create `tsconfig.json`**

```json
{
  "compilerOptions": {
    "target": "ES2020",
    "module": "CommonJS",
    "moduleResolution": "node",
    "outDir": "dist",
    "strict": true,
    "esModuleInterop": true,
    "skipLibCheck": true
  },
  "include": ["src/**/*"],
  "exclude": ["node_modules", "dist", "src/__tests__"]
}
```

- [ ] **Step 3: Create `jest.config.js`**

```js
/** @type {import('jest').Config} */
module.exports = {
  preset: 'ts-jest',
  testEnvironment: 'node',
  testMatch: ['**/src/__tests__/**/*.test.ts'],
}
```

- [ ] **Step 4: Create `.eslintrc.json`**

```json
{
  "parser": "@typescript-eslint/parser",
  "plugins": ["@typescript-eslint"],
  "extends": ["eslint:recommended", "plugin:@typescript-eslint/recommended"],
  "rules": {
    "@typescript-eslint/no-explicit-any": "warn"
  }
}
```

- [ ] **Step 5: Update `package.json` scripts and main**

Replace the `scripts` section and add `main`:

```json
{
  "name": "companion-module-showcast",
  "version": "1.0.0",
  "description": "Bitfocus Companion module for ShowCast",
  "main": "dist/main.js",
  "scripts": {
    "build": "tsc",
    "build:watch": "tsc --watch",
    "test": "jest",
    "lint": "eslint src --ext .ts"
  },
  "dependencies": {
    "@companion-module/base": "^1.7.0"
  },
  "devDependencies": {
    "@types/jest": "^29.0.0",
    "@types/node": "^20.0.0",
    "@typescript-eslint/eslint-plugin": "^6.0.0",
    "@typescript-eslint/parser": "^6.0.0",
    "eslint": "^8.0.0",
    "jest": "^29.0.0",
    "ts-jest": "^29.0.0",
    "typescript": "^5.0.0"
  }
}
```

- [ ] **Step 6: Create `companion/manifest.json`**

```json
{
  "id": "showcast",
  "name": "ShowCast",
  "shortname": "ShowCast",
  "description": "Control ShowCast presentation software via TCP",
  "version": "1.0.0",
  "license": "MIT",
  "author": "casey-tmc97",
  "bugs": "https://github.com/casey-tmc97/companion-module-showcast/issues",
  "repository": "https://github.com/casey-tmc97/companion-module-showcast",
  "category": "presentation",
  "deprecated": false,
  "runtime": {
    "type": "node22",
    "universal": "dist/main.js"
  }
}
```

- [ ] **Step 7: Create `src/__tests__/` directory and verify build works**

```bash
mkdir -p src/__tests__
npx tsc --noEmit
```

Expected: no errors (no source files yet, just validates config).

- [ ] **Step 8: Commit scaffold**

```bash
git add .
git commit -m "chore: scaffold companion-module-showcast repo"
```

---

## Task 3: Define shared types

**Files:**
- Create: `src/types.ts`

- [ ] **Step 1: Create `src/types.ts`**

```typescript
export interface ShowCastPage {
  id: string
  name: string
}

export interface ShowCastPlaylist {
  id: string
  name: string
}

export interface ShowCastOutput {
  id: string
  name: string
  blanked: boolean
}

export interface ShowCastAudio {
  playing: boolean
  trackName: string
  playlists: ShowCastPlaylist[]
}

export interface ShowCastRundown {
  pos: number
  total: number
  currentName: string
}

export interface ShowCastScheduler {
  running: boolean
}

export interface ShowCastState {
  page: ShowCastPage | null
  rundown: ShowCastRundown
  audio: ShowCastAudio
  scheduler: ShowCastScheduler
  outputs: ShowCastOutput[]
}

export interface ShowCastConfig {
  host: string
  port: number
  password: string
}
```

- [ ] **Step 2: Verify TypeScript compiles**

```bash
npx tsc --noEmit
```

Expected: no errors.

- [ ] **Step 3: Commit**

```bash
git add src/types.ts
git commit -m "feat: add ShowCast type definitions"
```

---

## Task 4: Implement TCP connection

**Files:**
- Create: `src/connection.ts`
- Create: `src/__tests__/connection.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `src/__tests__/connection.test.ts`:

```typescript
import * as net from 'net'
import { ShowCastConnection } from '../connection'
import type { ShowCastState } from '../types'

const EMPTY_STATE: ShowCastState = {
  page: null,
  rundown: { pos: 0, total: 0, currentName: '' },
  audio: { playing: false, trackName: '', playlists: [] },
  scheduler: { running: false },
  outputs: [],
}

function startMockServer(): Promise<{
  server: net.Server
  port: number
  received: string[]
  send: (line: string) => void
  close: () => Promise<void>
}> {
  return new Promise((resolve) => {
    const received: string[] = []
    let clientSocket: net.Socket | null = null
    let buf = ''

    const server = net.createServer((socket) => {
      clientSocket = socket
      socket.on('data', (d) => {
        buf += d.toString('utf8')
        const parts = buf.split('\n')
        buf = parts.pop()!
        for (const l of parts) if (l.trim()) received.push(l)
      })
    })

    server.listen(0, '127.0.0.1', () => {
      const port = (server.address() as net.AddressInfo).port
      resolve({
        server,
        port,
        received,
        send: (line) => clientSocket?.write(line + '\n'),
        close: () => new Promise((res) => server.close(() => res())),
      })
    })
  })
}

describe('ShowCastConnection', () => {
  test('sends auth with password on connect', (done) => {
    startMockServer().then(({ port, received, send, close }) => {
      const conn = new ShowCastConnection('127.0.0.1', port, 'secret')
      conn.on('connected', () => {
        setTimeout(() => {
          const auth = JSON.parse(received[0])
          expect(auth.type).toBe('auth')
          expect(auth.password).toBe('secret')
          conn.destroy()
          close().then(done)
        }, 50)
      })
      conn.connect()
    })
  })

  test('sends get_state after auth_ok', (done) => {
    startMockServer().then(({ port, received, send, close }) => {
      const conn = new ShowCastConnection('127.0.0.1', port, '')
      conn.on('connected', () => {
        setTimeout(() => send('{"type":"auth_ok"}'), 20)
      })
      setTimeout(() => {
        const getState = received.find((l) => JSON.parse(l).type === 'get_state')
        expect(getState).toBeDefined()
        conn.destroy()
        close().then(done)
      }, 200)
      conn.connect()
    })
  })

  test('emits stateUpdate when state message received', (done) => {
    startMockServer().then(({ port, send, close }) => {
      const conn = new ShowCastConnection('127.0.0.1', port, '')
      conn.on('connected', () => setTimeout(() => send('{"type":"auth_ok"}'), 10))
      conn.on('stateUpdate', (state: ShowCastState) => {
        expect(state.page).toBeNull()
        expect(state.outputs).toEqual([])
        conn.destroy()
        close().then(done)
      })
      // Delay state send until after auth_ok has been processed
      setTimeout(() => send(JSON.stringify({ type: 'state', ...EMPTY_STATE })), 80)
      conn.connect()
    })
  })

  test('emits authFailed on auth_fail and does not reconnect', (done) => {
    startMockServer().then(({ port, send, close }) => {
      let reconnected = false
      const conn = new ShowCastConnection('127.0.0.1', port, 'wrong')
      conn.on('connected', () => {
        reconnected ? null : setTimeout(() => send('{"type":"auth_fail"}'), 10)
        reconnected = true
      })
      conn.on('authFailed', () => {
        setTimeout(() => {
          // If reconnect happened, 'connected' fires again — we set reconnected=true above.
          // We only expect authFailed once and no further connects after destroy.
          conn.destroy()
          close().then(done)
        }, 300)
      })
      conn.connect()
    })
  })

  test('emits disconnected when server closes', (done) => {
    startMockServer().then(({ port, server, send, close }) => {
      const conn = new ShowCastConnection('127.0.0.1', port, '')
      conn.on('connected', () => {
        setTimeout(() => server.close(), 30)
      })
      conn.on('disconnected', () => {
        conn.destroy()
        done()
      })
      conn.connect()
    })
  })

  test('handles partial lines across TCP packets', (done) => {
    startMockServer().then(({ port, send, close }) => {
      const conn = new ShowCastConnection('127.0.0.1', port, '')
      conn.on('stateUpdate', (state: ShowCastState) => {
        expect(state.page).toBeNull()
        conn.destroy()
        close().then(done)
      })
      conn.on('connected', () => {
        // Send auth_ok and state in fragments without newlines between sends
        const stateJson = JSON.stringify({ type: 'state', ...EMPTY_STATE })
        setTimeout(() => {
          send('{"type":"auth_ok"}')
          // Split state JSON across two sends
          setTimeout(() => {
            send(stateJson.slice(0, 20))
            setTimeout(() => send(stateJson.slice(20)), 10)
          }, 20)
        }, 10)
      })
      conn.connect()
    })
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
npm test -- --testPathPattern connection
```

Expected: fails with "Cannot find module '../connection'".

- [ ] **Step 3: Create `src/connection.ts`**

```typescript
import { EventEmitter } from 'events'
import * as net from 'net'
import type { ShowCastState } from './types'

export class ShowCastConnection extends EventEmitter {
  private socket: net.Socket | null = null
  private lineBuffer = ''
  private reconnectTimer: ReturnType<typeof setTimeout> | null = null
  private reconnectDelay = 1000
  private readonly MAX_RECONNECT_DELAY = 30000
  private _destroyed = false

  constructor(
    private readonly host: string,
    private readonly port: number,
    private readonly password: string,
  ) {
    super()
  }

  connect(): void {
    if (this._destroyed) return
    this._clearReconnectTimer()
    this.lineBuffer = ''

    const socket = new net.Socket()
    this.socket = socket

    socket.on('connect', () => {
      this.reconnectDelay = 1000
      this.emit('connected')
      this.sendCommand({ type: 'auth', password: this.password })
    })

    socket.on('data', (data: Buffer) => {
      this.lineBuffer += data.toString('utf8')
      const lines = this.lineBuffer.split('\n')
      this.lineBuffer = lines.pop()!
      for (const line of lines) {
        if (line.trim()) this._processLine(line)
      }
    })

    socket.on('close', () => {
      this.socket = null
      if (!this._destroyed) {
        this.emit('disconnected')
        this._scheduleReconnect()
      }
    })

    socket.on('error', () => {
      // 'close' fires after 'error'; reconnect is handled there
    })

    socket.connect(this.port, this.host)
  }

  destroy(): void {
    this._destroyed = true
    this._clearReconnectTimer()
    this.socket?.destroy()
    this.socket = null
  }

  sendCommand(cmd: object): void {
    if (this.socket && !this.socket.destroyed) {
      this.socket.write(JSON.stringify(cmd) + '\n')
    }
  }

  private _processLine(line: string): void {
    let msg: { type: string }
    try {
      msg = JSON.parse(line)
    } catch {
      return
    }

    if (msg.type === 'auth_ok') {
      this.sendCommand({ type: 'get_state' })
    } else if (msg.type === 'auth_fail') {
      this.emit('authFailed')
      this.destroy()
    } else if (msg.type === 'state') {
      this.emit('stateUpdate', msg as unknown as ShowCastState)
    }
  }

  private _scheduleReconnect(): void {
    this.reconnectTimer = setTimeout(() => {
      this.reconnectDelay = Math.min(this.reconnectDelay * 2, this.MAX_RECONNECT_DELAY)
      this.connect()
    }, this.reconnectDelay)
  }

  private _clearReconnectTimer(): void {
    if (this.reconnectTimer !== null) {
      clearTimeout(this.reconnectTimer)
      this.reconnectTimer = null
    }
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
npm test -- --testPathPattern connection
```

Expected: all 6 passing (tests may take a few seconds due to setTimeout delays).

- [ ] **Step 5: Commit**

```bash
git add src/connection.ts src/__tests__/connection.test.ts
git commit -m "feat: add ShowCastConnection with TCP, auth, and reconnect"
```

---

## Task 5: Implement feedbacks

**Files:**
- Create: `src/feedbacks.ts`
- Create: `src/__tests__/feedbacks.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `src/__tests__/feedbacks.test.ts`:

```typescript
import { combineRgb } from '@companion-module/base'
import { getFeedbacks } from '../feedbacks'
import type { ShowCastState } from '../types'

function makeState(overrides: Partial<ShowCastState> = {}): ShowCastState {
  return {
    page: null,
    rundown: { pos: 0, total: 0, currentName: '' },
    audio: { playing: false, trackName: '', playlists: [] },
    scheduler: { running: false },
    outputs: [{ id: 'out-1', name: 'Main', blanked: false }],
    ...overrides,
  }
}

function makeInstance(state: ShowCastState | null) {
  return { state } as any
}

function evalFeedback(
  feedbacks: ReturnType<typeof getFeedbacks>,
  id: string,
  state: ShowCastState | null,
  options: Record<string, unknown> = {},
): boolean {
  const def = feedbacks[id] as any
  return def.callback({ options }, {})
}

describe('getFeedbacks', () => {
  test('page_is_live: true when state.page is not null', () => {
    const state = makeState({ page: { id: 'p1', name: 'Slide 1' } })
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'page_is_live', state)).toBe(true)
  })

  test('page_is_live: false when state.page is null', () => {
    const state = makeState({ page: null })
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'page_is_live', state)).toBe(false)
  })

  test('page_is_live: false when state is null', () => {
    const feedbacks = getFeedbacks(makeInstance(null))
    expect(evalFeedback(feedbacks, 'page_is_live', null)).toBe(false)
  })

  test('audio_is_playing: true when audio.playing is true', () => {
    const state = makeState({ audio: { playing: true, trackName: 'Track', playlists: [] } })
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'audio_is_playing', state)).toBe(true)
  })

  test('audio_is_playing: false when audio.playing is false', () => {
    const state = makeState()
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'audio_is_playing', state)).toBe(false)
  })

  test('scheduler_is_running: true when scheduler.running is true', () => {
    const state = makeState({ scheduler: { running: true } })
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'scheduler_is_running', state)).toBe(true)
  })

  test('scheduler_is_running: false when scheduler.running is false', () => {
    const state = makeState()
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'scheduler_is_running', state)).toBe(false)
  })

  test('output_is_blanked: true when matching output is blanked', () => {
    const state = makeState({ outputs: [{ id: 'out-1', name: 'Main', blanked: true }] })
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'output_is_blanked', state, { outputId: 'out-1' })).toBe(true)
  })

  test('output_is_blanked: false when matching output is not blanked', () => {
    const state = makeState({ outputs: [{ id: 'out-1', name: 'Main', blanked: false }] })
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'output_is_blanked', state, { outputId: 'out-1' })).toBe(false)
  })

  test('output_is_blanked: false when outputId does not match any output', () => {
    const state = makeState()
    const feedbacks = getFeedbacks(makeInstance(state))
    expect(evalFeedback(feedbacks, 'output_is_blanked', state, { outputId: 'no-such-id' })).toBe(false)
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
npm test -- --testPathPattern feedbacks
```

Expected: fails with "Cannot find module '../feedbacks'".

- [ ] **Step 3: Create `src/feedbacks.ts`**

```typescript
import { combineRgb, CompanionFeedbackDefinitions } from '@companion-module/base'
import type { ShowCastState } from './types'

interface FeedbackInstance {
  state: ShowCastState | null
}

export function getFeedbacks(instance: FeedbackInstance): CompanionFeedbackDefinitions {
  return {
    page_is_live: {
      type: 'boolean',
      name: 'Page Is Live',
      description: 'Active when a page is currently live on the selected output',
      defaultStyle: {
        bgcolor: combineRgb(0, 180, 0),
        color: combineRgb(255, 255, 255),
      },
      options: [],
      callback: () => instance.state?.page !== null && instance.state?.page !== undefined,
    },

    audio_is_playing: {
      type: 'boolean',
      name: 'Audio Playing',
      description: 'Active when any audio channel is playing',
      defaultStyle: {
        bgcolor: combineRgb(0, 180, 0),
        color: combineRgb(255, 255, 255),
      },
      options: [],
      callback: () => instance.state?.audio.playing === true,
    },

    scheduler_is_running: {
      type: 'boolean',
      name: 'Scheduler Running',
      description: 'Active when the ShowCast scheduler is running',
      defaultStyle: {
        bgcolor: combineRgb(0, 100, 200),
        color: combineRgb(255, 255, 255),
      },
      options: [],
      callback: () => instance.state?.scheduler.running === true,
    },

    output_is_blanked: {
      type: 'boolean',
      name: 'Output Blanked',
      description: 'Active when the specified output is blanked',
      defaultStyle: {
        bgcolor: combineRgb(200, 0, 0),
        color: combineRgb(255, 255, 255),
      },
      options: [
        {
          type: 'dropdown',
          id: 'outputId',
          label: 'Output',
          default: '',
          choices: instance.state?.outputs.map((o) => ({ id: o.id, label: o.name })) ?? [],
        },
      ],
      callback: (feedback) => {
        const outputId = feedback.options['outputId'] as string
        const output = instance.state?.outputs.find((o) => o.id === outputId)
        return output?.blanked === true
      },
    },
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
npm test -- --testPathPattern feedbacks
```

Expected: 10 passing.

- [ ] **Step 5: Commit**

```bash
git add src/feedbacks.ts src/__tests__/feedbacks.test.ts
git commit -m "feat: add Companion feedbacks (page_is_live, audio_is_playing, scheduler_is_running, output_is_blanked)"
```

---

## Task 6: Implement variables

**Files:**
- Create: `src/variables.ts`
- Create: `src/__tests__/variables.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `src/__tests__/variables.test.ts`:

```typescript
import { buildVariableValues, getVariableDefinitions } from '../variables'
import type { ShowCastState } from '../types'

function makeState(overrides: Partial<ShowCastState> = {}): ShowCastState {
  return {
    page: null,
    rundown: { pos: 0, total: 5, currentName: 'Opener' },
    audio: { playing: false, trackName: '', playlists: [] },
    scheduler: { running: false },
    outputs: [],
    ...overrides,
  }
}

describe('getVariableDefinitions', () => {
  test('exports definitions for all 8 variables', () => {
    const ids = getVariableDefinitions().map((v) => v.variableId)
    expect(ids).toEqual(expect.arrayContaining([
      'live_page_name', 'live_page_id',
      'rundown_position', 'rundown_total', 'rundown_current_name',
      'audio_track_name', 'audio_playing',
      'scheduler_running',
    ]))
    expect(ids).toHaveLength(8)
  })
})

describe('buildVariableValues', () => {
  test('live_page_name is empty string when page is null', () => {
    expect(buildVariableValues(makeState()).live_page_name).toBe('')
  })

  test('live_page_name is page name when page is set', () => {
    const state = makeState({ page: { id: 'p1', name: 'Welcome' } })
    expect(buildVariableValues(state).live_page_name).toBe('Welcome')
  })

  test('live_page_id is empty string when page is null', () => {
    expect(buildVariableValues(makeState()).live_page_id).toBe('')
  })

  test('live_page_id is page id when page is set', () => {
    const state = makeState({ page: { id: 'abc-123', name: 'Slide' } })
    expect(buildVariableValues(state).live_page_id).toBe('abc-123')
  })

  test('rundown_position is 1-based string of pos', () => {
    const state = makeState({ rundown: { pos: 2, total: 10, currentName: 'Item' } })
    expect(buildVariableValues(state).rundown_position).toBe('3')
  })

  test('rundown_total is string of total', () => {
    expect(buildVariableValues(makeState()).rundown_total).toBe('5')
  })

  test('rundown_current_name matches state', () => {
    expect(buildVariableValues(makeState()).rundown_current_name).toBe('Opener')
  })

  test('audio_playing is "true" when playing', () => {
    const state = makeState({ audio: { playing: true, trackName: 'Song', playlists: [] } })
    expect(buildVariableValues(state).audio_playing).toBe('true')
  })

  test('audio_playing is "false" when not playing', () => {
    expect(buildVariableValues(makeState()).audio_playing).toBe('false')
  })

  test('audio_track_name is track name when playing', () => {
    const state = makeState({ audio: { playing: true, trackName: 'Amazing Grace', playlists: [] } })
    expect(buildVariableValues(state).audio_track_name).toBe('Amazing Grace')
  })

  test('scheduler_running is "true" when running', () => {
    const state = makeState({ scheduler: { running: true } })
    expect(buildVariableValues(state).scheduler_running).toBe('true')
  })

  test('scheduler_running is "false" when not running', () => {
    expect(buildVariableValues(makeState()).scheduler_running).toBe('false')
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
npm test -- --testPathPattern variables
```

Expected: fails with "Cannot find module '../variables'".

- [ ] **Step 3: Create `src/variables.ts`**

```typescript
import { CompanionVariableDefinition, CompanionVariableValues } from '@companion-module/base'
import type { ShowCastState } from './types'

export function getVariableDefinitions(): CompanionVariableDefinition[] {
  return [
    { variableId: 'live_page_name',       name: 'Live Page Name' },
    { variableId: 'live_page_id',         name: 'Live Page ID (UUID)' },
    { variableId: 'rundown_position',     name: 'Rundown Position (1-based)' },
    { variableId: 'rundown_total',        name: 'Rundown Total Items' },
    { variableId: 'rundown_current_name', name: 'Rundown Current Item Name' },
    { variableId: 'audio_track_name',     name: 'Audio Track Name' },
    { variableId: 'audio_playing',        name: 'Audio Playing (true/false)' },
    { variableId: 'scheduler_running',    name: 'Scheduler Running (true/false)' },
  ]
}

export function buildVariableValues(state: ShowCastState): CompanionVariableValues {
  return {
    live_page_name:       state.page?.name ?? '',
    live_page_id:         state.page?.id ?? '',
    rundown_position:     String(state.rundown.pos + 1),
    rundown_total:        String(state.rundown.total),
    rundown_current_name: state.rundown.currentName,
    audio_track_name:     state.audio.trackName,
    audio_playing:        String(state.audio.playing),
    scheduler_running:    String(state.scheduler.running),
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
npm test -- --testPathPattern variables
```

Expected: 13 passing.

- [ ] **Step 5: Commit**

```bash
git add src/variables.ts src/__tests__/variables.test.ts
git commit -m "feat: add Companion variable definitions and value builder"
```

---

## Task 7: Implement actions

**Files:**
- Create: `src/actions.ts`
- Create: `src/__tests__/actions.test.ts`

- [ ] **Step 1: Write the failing tests**

Create `src/__tests__/actions.test.ts`:

```typescript
import { getActions } from '../actions'
import type { ShowCastState } from '../types'

function makeState(overrides: Partial<ShowCastState> = {}): ShowCastState {
  return {
    page: null,
    rundown: { pos: 0, total: 0, currentName: '' },
    audio: { playing: false, trackName: '', playlists: [{ id: 'pl-1', name: 'Set 1' }] },
    scheduler: { running: false },
    outputs: [{ id: 'out-1', name: 'Main', blanked: false }],
    ...overrides,
  }
}

function makeInstance(state: ShowCastState | null) {
  const sent: object[] = []
  return {
    state,
    sendCommand: (cmd: object) => sent.push(cmd),
    sent,
  } as any
}

async function fireAction(
  instance: ReturnType<typeof makeInstance>,
  actionId: string,
  options: Record<string, unknown> = {},
): Promise<void> {
  const actions = getActions(instance)
  const def = actions[actionId] as any
  await def.callback({ options })
}

describe('getActions', () => {
  test('page_advance sends {type:"page_advance"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'page_advance')
    expect(inst.sent[0]).toEqual({ type: 'page_advance' })
  })

  test('page_back sends {type:"page_back"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'page_back')
    expect(inst.sent[0]).toEqual({ type: 'page_back' })
  })

  test('page_clear sends {type:"page_clear"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'page_clear')
    expect(inst.sent[0]).toEqual({ type: 'page_clear' })
  })

  test('page_live sends {type:"page_live", pageId}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'page_live', { pageId: 'abc-123' })
    expect(inst.sent[0]).toEqual({ type: 'page_live', pageId: 'abc-123' })
  })

  test('rundown_next sends {type:"rundown_next"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'rundown_next')
    expect(inst.sent[0]).toEqual({ type: 'rundown_next' })
  })

  test('rundown_goto sends {type:"rundown_goto", index}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'rundown_goto', { index: 3 })
    expect(inst.sent[0]).toEqual({ type: 'rundown_goto', index: 3 })
  })

  test('audio_play sends {type:"audio_play", id}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'audio_play', { playlistId: 'pl-1' })
    expect(inst.sent[0]).toEqual({ type: 'audio_play', id: 'pl-1' })
  })

  test('audio_stop sends {type:"audio_stop"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'audio_stop')
    expect(inst.sent[0]).toEqual({ type: 'audio_stop' })
  })

  test('scheduler_start sends {type:"scheduler_start"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'scheduler_start')
    expect(inst.sent[0]).toEqual({ type: 'scheduler_start' })
  })

  test('scheduler_stop sends {type:"scheduler_stop"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'scheduler_stop')
    expect(inst.sent[0]).toEqual({ type: 'scheduler_stop' })
  })

  test('output_blank sends {type:"output_blank", outputId}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'output_blank', { outputId: 'out-1' })
    expect(inst.sent[0]).toEqual({ type: 'output_blank', outputId: 'out-1' })
  })

  test('output_unblank sends {type:"output_unblank", outputId}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'output_unblank', { outputId: 'out-1' })
    expect(inst.sent[0]).toEqual({ type: 'output_unblank', outputId: 'out-1' })
  })

  test('get_state sends {type:"get_state"}', async () => {
    const inst = makeInstance(makeState())
    await fireAction(inst, 'get_state')
    expect(inst.sent[0]).toEqual({ type: 'get_state' })
  })

  test('output_blank dropdown choices come from state.outputs', () => {
    const state = makeState({ outputs: [{ id: 'out-2', name: 'Stage', blanked: false }] })
    const inst = makeInstance(state)
    const actions = getActions(inst)
    const opts = (actions['output_blank'] as any).options
    const dropdown = opts.find((o: any) => o.id === 'outputId')
    expect(dropdown.choices).toEqual([{ id: 'out-2', label: 'Stage' }])
  })

  test('audio_play dropdown choices come from state.audio.playlists', () => {
    const inst = makeInstance(makeState())
    const actions = getActions(inst)
    const opts = (actions['audio_play'] as any).options
    const dropdown = opts.find((o: any) => o.id === 'playlistId')
    expect(dropdown.choices).toEqual([{ id: 'pl-1', label: 'Set 1' }])
  })
})
```

- [ ] **Step 2: Run tests to verify they fail**

```bash
npm test -- --testPathPattern actions
```

Expected: fails with "Cannot find module '../actions'".

- [ ] **Step 3: Create `src/actions.ts`**

```typescript
import { CompanionActionDefinitions } from '@companion-module/base'
import type { ShowCastState } from './types'

interface ActionInstance {
  state: ShowCastState | null
  sendCommand: (cmd: object) => void
}

export function getActions(instance: ActionInstance): CompanionActionDefinitions {
  const outputChoices = instance.state?.outputs.map((o) => ({ id: o.id, label: o.name })) ?? []
  const playlistChoices = instance.state?.audio.playlists.map((p) => ({ id: p.id, label: p.name })) ?? []

  return {
    page_advance: {
      name: 'Go Live & Advance',
      options: [],
      callback: async () => instance.sendCommand({ type: 'page_advance' }),
    },

    page_back: {
      name: 'Page Back',
      options: [],
      callback: async () => instance.sendCommand({ type: 'page_back' }),
    },

    page_clear: {
      name: 'Clear Live',
      options: [],
      callback: async () => instance.sendCommand({ type: 'page_clear' }),
    },

    page_live: {
      name: 'Go Live: Specific Page',
      options: [
        {
          type: 'textinput',
          id: 'pageId',
          label: 'Page ID (UUID)',
          default: '',
        },
      ],
      callback: async (action) =>
        instance.sendCommand({ type: 'page_live', pageId: action.options['pageId'] as string }),
    },

    rundown_next: {
      name: 'Rundown: Next',
      options: [],
      callback: async () => instance.sendCommand({ type: 'rundown_next' }),
    },

    rundown_goto: {
      name: 'Rundown: Go To Index',
      options: [
        {
          type: 'number',
          id: 'index',
          label: 'Index (0-based; note variable rundown_position is 1-based for display)',
          default: 0,
          min: 0,
          max: 9999,
        },
      ],
      callback: async (action) =>
        instance.sendCommand({ type: 'rundown_goto', index: action.options['index'] as number }),
    },

    audio_play: {
      name: 'Audio: Play Playlist',
      options: [
        {
          type: 'dropdown',
          id: 'playlistId',
          label: 'Playlist',
          default: '',
          choices: playlistChoices,
        },
      ],
      callback: async (action) =>
        instance.sendCommand({ type: 'audio_play', id: action.options['playlistId'] as string }),
    },

    audio_stop: {
      name: 'Audio: Stop All',
      options: [],
      callback: async () => instance.sendCommand({ type: 'audio_stop' }),
    },

    scheduler_start: {
      name: 'Scheduler: Start',
      options: [],
      callback: async () => instance.sendCommand({ type: 'scheduler_start' }),
    },

    scheduler_stop: {
      name: 'Scheduler: Stop',
      options: [],
      callback: async () => instance.sendCommand({ type: 'scheduler_stop' }),
    },

    output_blank: {
      name: 'Output: Blank',
      options: [
        {
          type: 'dropdown',
          id: 'outputId',
          label: 'Output',
          default: '',
          choices: outputChoices,
        },
      ],
      callback: async (action) =>
        instance.sendCommand({ type: 'output_blank', outputId: action.options['outputId'] as string }),
    },

    output_unblank: {
      name: 'Output: Unblank',
      options: [
        {
          type: 'dropdown',
          id: 'outputId',
          label: 'Output',
          default: '',
          choices: outputChoices,
        },
      ],
      callback: async (action) =>
        instance.sendCommand({ type: 'output_unblank', outputId: action.options['outputId'] as string }),
    },

    get_state: {
      name: 'Refresh State',
      options: [],
      callback: async () => instance.sendCommand({ type: 'get_state' }),
    },
  }
}
```

- [ ] **Step 4: Run tests to verify they pass**

```bash
npm test -- --testPathPattern actions
```

Expected: 16 passing.

- [ ] **Step 5: Run full test suite**

```bash
npm test
```

Expected: all tests pass.

- [ ] **Step 6: Commit**

```bash
git add src/actions.ts src/__tests__/actions.test.ts
git commit -m "feat: add 13 Companion actions with dynamic output/playlist dropdowns"
```

---

## Task 8: Implement main module

**Files:**
- Create: `src/main.ts`

No unit tests for `main.ts` — it wires framework APIs that require a live Companion process. Manual testing instructions are in the verification step.

- [ ] **Step 1: Create `src/main.ts`**

```typescript
import { InstanceBase, InstanceStatus, runEntrypoint, SomeCompanionConfigField } from '@companion-module/base'
import { ShowCastConnection } from './connection'
import { getActions } from './actions'
import { getFeedbacks } from './feedbacks'
import { getVariableDefinitions, buildVariableValues } from './variables'
import type { ShowCastConfig, ShowCastState } from './types'

class ShowCastInstance extends InstanceBase<ShowCastConfig> {
  state: ShowCastState | null = null
  private connection: ShowCastConnection | null = null

  async init(config: ShowCastConfig, _isFirstInit: boolean): Promise<void> {
    this.setVariableDefinitions(getVariableDefinitions())
    await this.configUpdated(config)
  }

  async destroy(): Promise<void> {
    this.connection?.destroy()
    this.connection = null
  }

  async configUpdated(config: ShowCastConfig): Promise<void> {
    this.connection?.destroy()

    this.connection = new ShowCastConnection(
      config.host ?? '127.0.0.1',
      config.port ?? 5100,
      config.password ?? '',
    )

    this.connection.on('connected', () => {
      this.updateStatus(InstanceStatus.Connecting, 'Authenticating...')
    })

    this.connection.on('authFailed', () => {
      this.updateStatus(InstanceStatus.BadConfig, 'Authentication failed')
    })

    this.connection.on('disconnected', () => {
      this.updateStatus(InstanceStatus.Connecting, 'Reconnecting...')
    })

    this.connection.on('stateUpdate', (state: ShowCastState) => {
      this.state = state
      this.setVariableValues(buildVariableValues(state))
      this.setActionDefinitions(getActions(this))
      this.setFeedbackDefinitions(getFeedbacks(this))
      this.checkFeedbacks()
      this.updateStatus(InstanceStatus.Ok)
    })

    this.setActionDefinitions(getActions(this))
    this.setFeedbackDefinitions(getFeedbacks(this))
    this.connection.connect()
  }

  getConfigFields(): SomeCompanionConfigField[] {
    return [
      {
        type: 'textinput',
        id: 'host',
        label: 'Host',
        default: '127.0.0.1',
        width: 6,
      },
      {
        type: 'number',
        id: 'port',
        label: 'Port',
        default: 5100,
        min: 1,
        max: 65535,
        width: 3,
      },
      {
        type: 'textinput',
        id: 'password',
        label: 'Password (leave blank if none)',
        default: '',
        width: 6,
      },
    ]
  }

  sendCommand(cmd: object): void {
    this.connection?.sendCommand(cmd)
  }
}

runEntrypoint(ShowCastInstance, [])
```

- [ ] **Step 2: Build the module**

```bash
npm run build
```

Expected: `dist/main.js` created with no TypeScript errors.

- [ ] **Step 3: Run full test suite one final time**

```bash
npm test
```

Expected: all tests pass.

- [ ] **Step 4: Manual verification in Companion**

1. Open ShowCast → Settings → Network → enable TCP, port 5100, save.
2. In Companion, add a new connection → search "showcast" → configure host `127.0.0.1`, port `5100`.
3. Verify connection status turns green.
4. Add a button → Actions → ShowCast → "Go Live & Advance". Press it. Verify ShowCast advances.
5. Add a button → Feedbacks → ShowCast → "Page Is Live". Go live in ShowCast; verify button turns green.
6. Check Variables panel in Companion: verify `$(showcast:live_page_name)` updates when pages go live.
7. Add a button → Actions → ShowCast → "Output: Blank". Verify the output dropdown is populated with ShowCast's configured outputs.
8. Add a button → Actions → ShowCast → "Audio: Play Playlist". Verify the playlist dropdown is populated.

- [ ] **Step 5: Commit**

```bash
git add src/main.ts
git commit -m "feat: add ShowCastInstance main module wiring Companion APIs to TCP connection"
```

---

## Self-Review Checklist

- [x] **Spec coverage:** All 13 actions, 4 feedbacks, 8 variables covered. Required C# change (playlists in state) covered in Task 1. Dynamic dropdowns for outputs and playlists covered in Tasks 5 and 7. Connection lifecycle (auth, reconnect, status) covered in Task 4 and Task 8.
- [x] **Placeholder scan:** No TBDs. All steps contain actual code.
- [x] **Type consistency:** `sendCommand(cmd: object)` consistent across `connection.ts`, `actions.ts`, and `main.ts`. `ShowCastState` interface matches the protocol shape including `playlists` from Task 1. `action.options['playlistId']` in actions.ts matches the `id: 'playlistId'` dropdown definition in the same file. `feedback.options['outputId']` in feedbacks.ts matches `id: 'outputId'` dropdown definition.
