# Companion Protocol: Page "Go to Next" Timer

**Date:** 2026-07-21

## Problem

The companion module (separate repo `companion-module-showcast`) already exposes `video_remaining`/`audio_remaining`, but there's no way to see the remaining time on a page's "Go to Next" auto-advance timer (`Page.DurationMs` / `LoopToStart`, configured via `GoToNextTimerDialog`). This timer already drives a live countdown in the UI (`TickCountdown()` in `MainViewModel.cs`) but nothing pushes it to companion clients.

## Change

### ShowCast (`ViewModels/MainViewModel.cs`)

Add a `pageTimer` object to `BuildCompanionState()`:

```json
"pageTimer": {"active": true, "remainingMs": 4200, "durationMs": 5000}
```

- `active` is true whenever the live page has a running go-to-next timer — either a plain `DurationMs` countdown, or (for pages whose advance is driven by video length) the video's remaining time. Reuses the same source data as `TickCountdown()` — no second timer.
- `remainingMs`/`durationMs` are 0 when no page timer applies.
- For a video-driven page, this intentionally mirrors `video_remaining` — the video's end *is* the advance trigger.

Also fix a gap: `UpdateMediaTickTimer()`'s `anyPlaying` check only looks at audio/video playback, so a plain image/text page with a `DurationMs` timer and no media never gets a 1Hz `PushStateToCompanion()` tick today. Extend the check to include an active non-video page duration timer, and call `UpdateMediaTickTimer()` from `StartCountdownTimer`/`StopCountdownTimer` (not just the audio-state subscription).

### companion-module-showcast (`src/types.ts`, `src/variables.ts`)

- `types.ts`: add `pageTimer: { active: boolean; remainingMs: number; durationMs: number }` to `ShowCastState`.
- `variables.ts`: add `page_timer_active` (`"true"`/`"false"`), `page_timer_remaining` (`M:SS` via existing `fmtMs`), `page_timer_duration` (`M:SS`).

## Out of scope

- Rundown-level auto-advance (`RundownEntry.AutoAdvance`/`AutoAdvanceDelayMs`) — those fields exist but are unimplemented and unrelated to this change.
