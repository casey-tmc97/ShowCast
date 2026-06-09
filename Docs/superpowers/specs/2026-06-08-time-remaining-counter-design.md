# Time Remaining Counter on Page Cards

**Date:** 2026-06-08
**Status:** Approved

## Overview

Show a live draining progress bar at the bottom of the active page card for any page that has a video layer or a go-to-next auto-advance timer. The existing static timer badge (⏱ 5s) also updates to a live countdown while the page is live.

## Scope

- Visible only on the **live page card** (not on all cards)
- Shown in **both** the flat-view grid and the grouped-rundown view
- For pages with a video layer: bar tracks video playback position (priority)
- For pages with only an auto-advance timer: bar tracks the countdown
- For pages with both: bar tracks video (video takes priority)
- For pages with neither: bar and countdown are never shown

---

## Section 1: PageViewModel

### New properties

```csharp
// Reactive, updated by MainViewModel every 100ms while live
double ProgressFraction   // 1.0 = just went live (full bar), drains to 0.0
bool   HasProgress        // controls bar visibility
string? LiveTimerLabel    // _liveCountdownLabel ?? TimerLabel (badge binding)
```

`LiveTimerLabel` returns the live countdown string (e.g. "4.2s") when the countdown is active, falling back to the static configured label (e.g. "5s") otherwise. The timer badge text binding switches from `TimerLabel` to `LiveTimerLabel`.

### New internal methods (called by MainViewModel)

```csharp
void UpdateCountdown(double fraction, string label)
// Sets ProgressFraction, _liveCountdownLabel, HasProgress=true, raises LiveTimerLabel

void ClearCountdown()
// Zeroes ProgressFraction, nulls _liveCountdownLabel, HasProgress=false, raises LiveTimerLabel
```

---

## Section 2: MainViewModel

### New fields

```csharp
DispatcherTimer? _countdownTimer       // 100ms interval, UI thread
PageViewModel?   _livePageVm           // live page's VM reference
OutputState?     _liveOutputForCountdown  // output driving the live page (for video registry)
DateTime         _livePageStartTime    // recorded at go-live, for timer-only countdown
```

### New methods

**`StartCountdownTimer(PageViewModel liveVm, OutputState? liveOutput)`**
- Records all four fields above
- Starts a new `DispatcherTimer` (100ms) only if `HasVideoLayers(liveVm.Model) || liveVm.Model.DurationMs > 0`
- Stops any existing `_countdownTimer` first

**`StopCountdownTimer()`**
- Stops and disposes `_countdownTimer`
- Calls `_livePageVm?.ClearCountdown()`

**`TickCountdown()`** (called by timer elapsed)
1. If `HasVideoLayers(_livePageVm.Model)`:
   - Read `(timeMs, lengthMs) = _liveOutputForCountdown?.VideoRegistry?.GetPrimaryTime() ?? (0, 0)`
   - If `lengthMs > 0`: `fraction = 1.0 - timeMs / lengthMs`, format label → call `UpdateCountdown`
   - If `lengthMs == 0`: skip tick (video not yet registered; bar stays at last value)
2. Else if `_livePageVm.Model.DurationMs > 0`:
   - `elapsed = (DateTime.UtcNow - _livePageStartTime).TotalMilliseconds`
   - `fraction = Math.Max(0, 1.0 - elapsed / durationMs)`
   - Format label → call `UpdateCountdown`

**Label formatting:**
```
remaining >= 10s  →  "14s"   (floor to whole seconds)
remaining < 10s   →  "4.2s"  (one decimal)
remaining <= 0    →  "0s"
```

### Call site hooks (no structural changes)

| Call site | Addition |
|-----------|----------|
| `GoLive()` after `StartPageTimer` | `StartCountdownTimer(SelectedPage, SelectedOutput)` |
| `GoLiveFromGroup()` after `StartPageTimer` | `StartCountdownTimer(pvm, output)` |
| `SetPageTimer()` inside the `if (pvm.Model == SelectedOutput?.LivePage)` guard | `StartCountdownTimer(pvm, SelectedOutput)` |
| `ClearLive()` after `StopPageTimer()` | `StopCountdownTimer()` |
| `ClearOutput(output)` after `StopPageTimer()` | `if (output == _liveOutputForCountdown) StopCountdownTimer()` |

---

## Section 3: AXAML — PageGridPanel.axaml

Applied identically to **both** DataTemplates (flat-view ~line 111, grouped-rundown ~line 460).

### Change 1: Timer badge binding

```xml
<!-- Before -->
<TextBlock Text="{Binding TimerLabel}" ... />
<!-- After -->
<TextBlock Text="{Binding LiveTimerLabel}" ... />
```

### Change 2: Progress bar (inserted between thumbnail Grid and name TextBlock)

```xml
<ProgressBar Value="{Binding ProgressFraction}"
             Minimum="0" Maximum="1"
             Height="3"
             IsVisible="{Binding HasProgress}"
             Foreground="#5599ff"
             Background="#333333"
             CornerRadius="0"/>
```

- 3px tall, sharp corners, fits inside the card `StackPanel`
- Blue fill `#5599ff` matches existing insert-indicator color
- Dark track `#333333`
- Fill shrinks from right as `ProgressFraction` drains 1→0
- Hidden when `HasProgress=false`

---

## Edge Cases

- **Video not yet registered** (`lengthMs == 0` on first tick): tick is skipped; bar shows at 1.0 until video is ready.
- **Looping video** (`VideoLoopMode.Loop`): `GetPrimaryTime()` returns position within the current loop; bar drains and refills each loop.
- **`ClearOutput` for a non-live output**: only `StopCountdownTimer()` if `output == _liveOutputForCountdown`, so clearing a secondary output doesn't disrupt the countdown.
- **Page with no video and no timer**: `StartCountdownTimer` returns early; `HasProgress` stays false; bar never appears.
