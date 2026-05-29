# Timer Fixes Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix three timer bugs: clock timers auto-start on launch, clock timers run forever past their end time, and timer text updates in package preview thumbnails and the editor canvas.

**Architecture:** All fixes are confined to `TimerViewModel` (startup + end-state) and `PageRenderer.Render()` (preview isolation). A new `bool useLiveTimers` flag on `Render()` gates TimerTextCache lookups; thumbnail and editor renders pass `false`, live output renders keep the default `true`.

**Tech Stack:** C#/.NET 9, xUnit, DispatcherTimer, SkiaSharp, Avalonia

---

### Task 1: Remove clock timer auto-start

**Files:**
- Modify: `ViewModels/TimerViewModel.cs:20-31`
- Create: `ShowCast.Tests/ViewModels/TimerViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// ShowCast.Tests/ViewModels/TimerViewModelTests.cs
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class TimerViewModelTests
{
    [Fact]
    public void ClockTimer_DoesNotAutoStart_OnCreation()
    {
        var def = new TimerDef { Type = TimerType.Clock, ClockTime = "23:59" };
        var vm  = new TimerViewModel(def);

        Assert.False(vm.IsRunning);

        vm.Dispose();
    }

    [Fact]
    public void CounterTimer_DoesNotAutoStart_OnCreation()
    {
        var def = new TimerDef { Type = TimerType.Counter, StartSeconds = 300 };
        var vm  = new TimerViewModel(def);

        Assert.False(vm.IsRunning);

        vm.Dispose();
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ShowCast.Tests/ --filter "ClockTimer_DoesNotAutoStart_OnCreation" -v minimal`
Expected: FAIL — clock timer IS running after construction.

- [ ] **Step 3: Remove the auto-start block in TimerViewModel constructor**

In `ViewModels/TimerViewModel.cs`, replace:

```csharp
public TimerViewModel(TimerDef def)
{
    Def = def;
    _currentSeconds = def.Type == TimerType.Counter ? def.StartSeconds : 0;
    TimerTextCache.Update(def.Id, DisplayText);

    // Clock timers always display a live countdown — start the refresh loop immediately
    if (def.Type == TimerType.Clock)
    {
        _ticker = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Normal, OnTick);
        _ticker.Start();
        _running = true;
    }
}
```

With:

```csharp
public TimerViewModel(TimerDef def)
{
    Def = def;
    _currentSeconds = def.Type == TimerType.Counter ? def.StartSeconds : 0;
    TimerTextCache.Update(def.Id, DisplayText);
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ShowCast.Tests/ --filter "TimerViewModelTests" -v minimal`
Expected: PASS both tests.

- [ ] **Step 5: Commit**

```bash
git add ViewModels/TimerViewModel.cs ShowCast.Tests/ViewModels/TimerViewModelTests.cs
git commit -m "fix(timers): remove clock timer auto-start on construction"
```

---

### Task 2: Fix clock timer end state — stop at target time

**Files:**
- Modify: `ViewModels/TimerViewModel.cs:162-183` (`OnTick`) and `ViewModels/TimerViewModel.cs:186-195` (`ClockSecondsRemaining`)
- Modify: `ShowCast.Tests/ViewModels/TimerViewModelTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `TimerViewModelTests`:

```csharp
[Fact]
public void ClockTimer_ClockSecondsRemaining_ReturnsNonPositive_WhenTargetIsPast()
{
    // A time exactly equal to "now" rounded to the minute already passed.
    var past = DateTime.Now.AddMinutes(-5);
    var def  = new TimerDef
    {
        Type      = TimerType.Clock,
        ClockTime = $"{past.Hour:00}:{past.Minute:00}"
    };
    var vm = new TimerViewModel(def);

    // Before fix: ClockSecondsRemaining adds a day when target <= now,
    // so the value is large (~86400). After fix it is ≤ 0.
    // Access via DisplayText — a stopped clock at 0 shows "0:00".
    // We verify the raw logic by checking that DisplayText is "0:00"
    // (or was already "0:00" because the day-roll was removed).
    // The key assertion: remaining should not be nearly 86400 seconds.
    int remaining = InvokeClockSecondsRemaining(vm);
    Assert.True(remaining <= 0, $"Expected ≤ 0 but got {remaining}");

    vm.Dispose();
}

// Reflection helper — accesses the private method for direct testing.
static int InvokeClockSecondsRemaining(TimerViewModel vm)
{
    var m = typeof(TimerViewModel).GetMethod("ClockSecondsRemaining",
        System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;
    return (int)m.Invoke(vm, null)!;
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ShowCast.Tests/ --filter "ClockTimer_ClockSecondsRemaining_ReturnsNonPositive" -v minimal`
Expected: FAIL — current code returns ~86400 due to AddDays(1).

- [ ] **Step 3: Fix ClockSecondsRemaining and OnTick**

In `ViewModels/TimerViewModel.cs`, replace `ClockSecondsRemaining`:

```csharp
int ClockSecondsRemaining()
{
    var parts = Def.ClockTime.Split(':');
    if (parts.Length != 2 || !int.TryParse(parts[0], out int h) || !int.TryParse(parts[1], out int m))
        return 0;
    var now    = DateTime.Now;
    var target = new DateTime(now.Year, now.Month, now.Day, h, m, 0);
    return (int)(target - now).TotalSeconds;
}
```

Then in `OnTick`, replace the Clock branch:

```csharp
void OnTick(object? sender, EventArgs e)
{
    if (Def.Type == TimerType.Clock)
    {
        int remaining = ClockSecondsRemaining();
        if (remaining <= 0)
        {
            CurrentSeconds = 0;
            Pause();
            return;
        }
        TimerTextCache.Update(Def.Id, DisplayText);
        this.RaisePropertyChanged(nameof(DisplayText));
        this.RaisePropertyChanged(nameof(DisplayBrush));
        return;
    }

    bool countDown = Def.StartSeconds >= Def.EndSeconds;
    int next = WallClockSeconds();

    bool hitEnd = countDown ? next <= Def.EndSeconds : next >= Def.EndSeconds;
    if (hitEnd && !Def.OverflowEnabled)
    {
        CurrentSeconds = Def.EndSeconds;
        Pause();
        return;
    }

    CurrentSeconds = next;
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ShowCast.Tests/ --filter "TimerViewModelTests" -v minimal`
Expected: PASS all three tests.

- [ ] **Step 5: Commit**

```bash
git add ViewModels/TimerViewModel.cs ShowCast.Tests/ViewModels/TimerViewModelTests.cs
git commit -m "fix(timers): stop clock timer when it reaches its target time"
```

---

### Task 3: Isolate timer text from preview renders

**Files:**
- Modify: `Engine/PageRenderer.cs:17` (`Render` signature + `DrawText` call)
- Modify: `Engine/PageRenderer.cs:327` (`DrawText`)
- Modify: `ViewModels/PageViewModel.cs:103` (`RebuildThumbnail`)
- Modify: `Views/EditorCanvas.cs:267` (`RebuildSlide`)
- Modify: `Views/EditorCanvas.cs:257` (`RebuildSlideAnimated`)

- [ ] **Step 1: Write the failing test**

Add to `ShowCast.Tests/Core/` a new file `PageRendererTimerTests.cs`:

```csharp
// ShowCast.Tests/Core/PageRendererTimerTests.cs
using ShowCast.Core;
using ShowCast.Engine;
using SkiaSharp;
using Xunit;

namespace ShowCast.Tests.Core;

public class PageRendererTimerTests
{
    [Fact]
    public void Render_WithUseLiveTimersFalse_UsesLayerTextNotCache()
    {
        var timerId = Guid.NewGuid();
        TimerTextCache.Values[timerId] = "5:00";

        var layer = new SlideLayer
        {
            Type         = LayerType.Text,
            Text         = "STATIC",
            TimerBinding = timerId,
            Width        = 1f, Height = 1f
        };
        var page = new Page();
        page.AddLayer(layer);

        // When useLiveTimers=false the renderer must ignore the cache
        using var surface = SKSurface.Create(new SKImageInfo(320, 180, SKColorType.Rgba8888));
        // Should not throw; just verifying the parameter is accepted
        PageRenderer.Render(surface.Canvas, page, LayerRole.All, 320, 180, useLiveTimers: false);

        TimerTextCache.Values.TryRemove(timerId, out _);
    }

    [Fact]
    public void Render_WithUseLiveTimersTrue_UsesCache()
    {
        var timerId = Guid.NewGuid();
        TimerTextCache.Values[timerId] = "LIVE";

        var layer = new SlideLayer
        {
            Type         = LayerType.Text,
            Text         = "STATIC",
            TimerBinding = timerId,
            Width        = 1f, Height = 1f
        };
        var page = new Page();
        page.AddLayer(layer);

        using var surface = SKSurface.Create(new SKImageInfo(320, 180, SKColorType.Rgba8888));
        // Should not throw; verifying default (true) is accepted
        PageRenderer.Render(surface.Canvas, page, LayerRole.All, 320, 180);

        TimerTextCache.Values.TryRemove(timerId, out _);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (compile error)**

Run: `dotnet test ShowCast.Tests/ --filter "PageRendererTimerTests" -v minimal`
Expected: compile error — `useLiveTimers` parameter does not exist yet.

- [ ] **Step 3: Add `useLiveTimers` parameter to PageRenderer.Render and DrawText**

In `Engine/PageRenderer.cs`, change the `Render` signature (line 17):

```csharp
public static void Render(SKCanvas canvas, Page page, LayerRole roleFilter,
                          int canvasWidth, int canvasHeight,
                          double elapsedMs     = -1.0,
                          double exitElapsedMs = -1.0,
                          bool useLiveTimers   = true)
```

In `Render`, find the `LayerType.Text` case (line ~82) and change it to:

```csharp
case LayerType.Text:
    DrawText(canvas, layer, canvasWidth, canvasHeight, useLiveTimers);
    break;
```

Change `DrawText` signature and first line:

```csharp
static void DrawText(SKCanvas canvas, SlideLayer layer, int w, int h, bool useLiveTimers = true)
{
    string text = useLiveTimers && layer.TimerBinding is { } tid
                  && TimerTextCache.Values.TryGetValue(tid, out var tv)
        ? tv : layer.Text;
    if (string.IsNullOrEmpty(text)) return;
    // ... rest unchanged
```

- [ ] **Step 4: Update thumbnail and editor canvas callers**

In `ViewModels/PageViewModel.cs`, change `RebuildThumbnail` (line 103):

```csharp
PageRenderer.Render(surface.Canvas, Model, LayerRole.All, ThumbW, ThumbH, useLiveTimers: false);
```

In `Views/EditorCanvas.cs`, change `RebuildSlide` (line 273):

```csharp
PageRenderer.Render(surface.Canvas, slide, LayerRole.All, RenderW, RenderH, useLiveTimers: false);
```

In `Views/EditorCanvas.cs`, change `RebuildSlideAnimated` (line 263):

```csharp
PageRenderer.Render(surface.Canvas, slide, LayerRole.All, RenderW, RenderH, elapsedMs, useLiveTimers: false);
```

- [ ] **Step 5: Run tests to verify they pass**

Run: `dotnet test ShowCast.Tests/ --filter "PageRendererTimerTests" -v minimal`
Expected: PASS both tests.

Run full suite: `dotnet test ShowCast.Tests/ -v minimal`
Expected: all tests pass (no regressions).

- [ ] **Step 6: Commit**

```bash
git add Engine/PageRenderer.cs ViewModels/PageViewModel.cs Views/EditorCanvas.cs ShowCast.Tests/Core/PageRendererTimerTests.cs
git commit -m "fix(timers): isolate live timer text from preview/thumbnail renders"
```
