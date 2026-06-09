# Time Remaining Counter Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a draining progress bar and live countdown on the active page card for any page with a video layer or auto-advance timer.

**Architecture:** `PageViewModel` gets three new reactive properties (`ProgressFraction`, `HasProgress`, `LiveTimerLabel`) plus `UpdateCountdown`/`ClearCountdown` methods that `MainViewModel` calls. `MainViewModel` runs a 100ms `System.Timers.Timer` while a qualifying page is live, reading video position (priority) or elapsed time each tick. Both page-card DataTemplates in `PageGridPanel.axaml` get a 3px `ProgressBar` and an updated badge binding.

**Tech Stack:** C# 13, .NET 9, Avalonia UI 11, ReactiveUI, xUnit

---

## Files

| Action | Path |
|--------|------|
| Modify | `ViewModels/PageViewModel.cs` |
| Modify | `ViewModels/MainViewModel.cs` |
| Modify | `Views/PageGridPanel.axaml` |
| Create | `ShowCast.Tests/ViewModels/PageViewModelCountdownTests.cs` |
| Create | `ShowCast.Tests/ViewModels/MainViewModelCountdownTests.cs` |

---

## Task 1: PageViewModel countdown properties

**Files:**
- Modify: `ViewModels/PageViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/PageViewModelCountdownTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ShowCast.Tests/ViewModels/PageViewModelCountdownTests.cs`:

```csharp
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class PageViewModelCountdownTests
{
    [Fact]
    public void UpdateCountdown_SetsProgressFractionAndHasProgress()
    {
        var vm = new PageViewModel(new Page { Name = "Test" });

        vm.UpdateCountdown(0.75, "3.5s");

        Assert.Equal(0.75, vm.ProgressFraction);
        Assert.True(vm.HasProgress);
    }

    [Fact]
    public void UpdateCountdown_LiveTimerLabel_ShowsCountdownText()
    {
        var vm = new PageViewModel(new Page { Name = "Test", DurationMs = 5000 });

        vm.UpdateCountdown(0.5, "2.5s");

        Assert.Equal("2.5s", vm.LiveTimerLabel);
    }

    [Fact]
    public void ClearCountdown_ResetsProgressAndFallsBackToStaticLabel()
    {
        var vm = new PageViewModel(new Page { Name = "Test", DurationMs = 5000 });
        vm.UpdateCountdown(0.5, "2.5s");

        vm.ClearCountdown();

        Assert.Equal(0.0, vm.ProgressFraction);
        Assert.False(vm.HasProgress);
        Assert.Equal("5s", vm.LiveTimerLabel); // falls back to static TimerLabel
    }

    [Fact]
    public void LiveTimerLabel_NoCountdownActive_ReturnsStaticTimerLabel()
    {
        var vm = new PageViewModel(new Page { Name = "Test", DurationMs = 3000 });

        Assert.Equal("3s", vm.LiveTimerLabel);
    }

    [Fact]
    public void LiveTimerLabel_NoTimerAndNoCountdown_ReturnsNull()
    {
        var vm = new PageViewModel(new Page { Name = "Test" }); // DurationMs = 0

        Assert.Null(vm.LiveTimerLabel);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests --filter "PageViewModelCountdownTests"
```

Expected: compile error — `UpdateCountdown`, `ClearCountdown`, `ProgressFraction`, `HasProgress`, `LiveTimerLabel` do not exist yet.

- [ ] **Step 3: Add properties and methods to PageViewModel**

In `ViewModels/PageViewModel.cs`, add after the `HasTimer` property (line 55):

```csharp
private double _progressFraction;
public double ProgressFraction
{
    get => _progressFraction;
    private set => this.RaiseAndSetIfChanged(ref _progressFraction, value);
}

private bool _hasProgress;
public bool HasProgress
{
    get => _hasProgress;
    private set => this.RaiseAndSetIfChanged(ref _hasProgress, value);
}

private string? _liveCountdownLabel;
public string? LiveTimerLabel => _liveCountdownLabel ?? TimerLabel;

public void UpdateCountdown(double fraction, string label)
{
    ProgressFraction = fraction;
    _liveCountdownLabel = label;
    HasProgress = true;
    this.RaisePropertyChanged(nameof(LiveTimerLabel));
}

public void ClearCountdown()
{
    ProgressFraction = 0;
    _liveCountdownLabel = null;
    HasProgress = false;
    this.RaisePropertyChanged(nameof(LiveTimerLabel));
}
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests --filter "PageViewModelCountdownTests"
```

Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```
git add ViewModels/PageViewModel.cs ShowCast.Tests/ViewModels/PageViewModelCountdownTests.cs
git commit -m "feat: add countdown properties to PageViewModel"
```

---

## Task 2: MainViewModel countdown infrastructure

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/MainViewModelCountdownTests.cs`

- [ ] **Step 1: Write failing tests**

Create `ShowCast.Tests/ViewModels/MainViewModelCountdownTests.cs`:

```csharp
using System;
using System.Reflection;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelCountdownTests
{
    /// <summary>
    /// Creates a MainViewModel with one package containing one page.
    /// Sets DurationMs on the page via the PageViewModel setter so HasTimer is correct.
    /// </summary>
    static MainViewModel CreateVm(out PageViewModel pageVm, int durationMs = 3000)
    {
        var vm = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages[^1]; // last package (the one just added)

        vm.LoadPackageToSelectedOutput(pkg);
        vm.SelectedPage = vm.Pages[0];
        pageVm = vm.Pages[0];
        pageVm.DurationMs = durationMs; // use VM setter to raise HasTimer notification
        return vm;
    }

    static void CallTickCountdown(MainViewModel vm)
    {
        var method = typeof(MainViewModel).GetMethod("TickCountdown",
            BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new MissingMethodException("TickCountdown not found");
        method.Invoke(vm, null);
    }

    static string CallFormatCountdown(double remainingSec)
    {
        var method = typeof(MainViewModel).GetMethod("FormatCountdown",
            BindingFlags.NonPublic | BindingFlags.Static)
            ?? throw new MissingMethodException("FormatCountdown not found");
        return (string)method.Invoke(null, new object[] { remainingSec })!;
    }

    // ── FormatCountdown ───────────────────────────────────────────────────────

    [Fact]
    public void FormatCountdown_AboveTenSeconds_ReturnsWholeSecondsFloor()
    {
        Assert.Equal("14s", CallFormatCountdown(14.8));
        Assert.Equal("10s", CallFormatCountdown(10.0));
    }

    [Fact]
    public void FormatCountdown_BelowTenSeconds_ReturnsOneDecimal()
    {
        Assert.Equal("4.2s", CallFormatCountdown(4.2));
        Assert.Equal("9.9s", CallFormatCountdown(9.9));
    }

    [Fact]
    public void FormatCountdown_Zero_ReturnsZeroLabel()
    {
        Assert.Equal("0s", CallFormatCountdown(0.0));
        Assert.Equal("0s", CallFormatCountdown(-1.0));
    }

    // ── GoLive / TickCountdown ────────────────────────────────────────────────

    [Fact]
    public void GoLive_TimerPage_AfterTick_HasProgressIsTrueAndFractionInRange()
    {
        var vm = CreateVm(out var pageVm, durationMs: 5000);
        vm.GoLive();

        CallTickCountdown(vm);

        Assert.True(pageVm.HasProgress);
        Assert.InRange(pageVm.ProgressFraction, 0.0, 1.0);
    }

    [Fact]
    public void GoLive_NoTimerNoVideo_AfterTick_HasProgressStaysFalse()
    {
        var vm = CreateVm(out var pageVm, durationMs: 0);
        vm.GoLive();

        CallTickCountdown(vm);

        Assert.False(pageVm.HasProgress);
    }

    // ── ClearLive ─────────────────────────────────────────────────────────────

    [Fact]
    public void ClearLive_AfterGoLive_HasProgressIsFalse()
    {
        var vm = CreateVm(out var pageVm, durationMs: 5000);
        vm.GoLive();
        CallTickCountdown(vm);
        Assert.True(pageVm.HasProgress); // sanity

        vm.ClearLive();

        Assert.False(pageVm.HasProgress);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests --filter "MainViewModelCountdownTests"
```

Expected: compile error — `TickCountdown` and `FormatCountdown` do not exist yet.

- [ ] **Step 3: Add fields and new methods to MainViewModel**

In `ViewModels/MainViewModel.cs`, directly after the `_pageTimer` field declaration (near line 998), add the four countdown fields:

```csharp
System.Timers.Timer? _countdownTimer;
PageViewModel?       _livePageVm;
OutputState?         _liveOutputForCountdown;
DateTime             _livePageStartTime;
```

After `StopPageTimer()` (near line 1071), add four new methods:

```csharp
void StartCountdownTimer(PageViewModel liveVm, OutputState? liveOutput)
{
    _countdownTimer?.Stop();
    _countdownTimer?.Dispose();
    _countdownTimer = null;
    _livePageVm = liveVm;
    _liveOutputForCountdown = liveOutput;
    _livePageStartTime = DateTime.UtcNow;

    bool hasVideo = HasVideoLayers(liveVm.Model);
    if (!hasVideo && liveVm.Model.DurationMs <= 0) return;

    _countdownTimer = new System.Timers.Timer(100) { AutoReset = true };
    _countdownTimer.Elapsed += (_, _) =>
        Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(TickCountdown);
    _countdownTimer.Start();
}

void StopCountdownTimer()
{
    _countdownTimer?.Stop();
    _countdownTimer?.Dispose();
    _countdownTimer = null;
    _livePageVm?.ClearCountdown();
    _livePageVm = null;
    _liveOutputForCountdown = null;
}

void TickCountdown()
{
    if (_livePageVm is null) return;

    if (HasVideoLayers(_livePageVm.Model))
    {
        var (timeMs, lengthMs) = _liveOutputForCountdown?.VideoRegistry?.GetPrimaryTime() ?? (0, 0);
        if (lengthMs > 0)
        {
            double fraction    = 1.0 - (double)timeMs / lengthMs;
            double remainSec   = (lengthMs - timeMs) / 1000.0;
            _livePageVm.UpdateCountdown(fraction, FormatCountdown(remainSec));
        }
        // If lengthMs == 0, video not registered yet — skip tick, bar stays at last value
        return;
    }

    int durationMs = _livePageVm.Model.DurationMs;
    if (durationMs > 0)
    {
        double elapsed     = (DateTime.UtcNow - _livePageStartTime).TotalMilliseconds;
        double fraction    = Math.Max(0.0, 1.0 - elapsed / durationMs);
        double remainSec   = Math.Max(0.0, (durationMs - elapsed) / 1000.0);
        _livePageVm.UpdateCountdown(fraction, FormatCountdown(remainSec));
    }
}

static string FormatCountdown(double remainSec)
{
    if (remainSec >= 10) return $"{(int)remainSec}s";
    if (remainSec > 0)   return $"{remainSec:F1}s";
    return "0s";
}
```

- [ ] **Step 4: Hook StartCountdownTimer into GoLive**

In `GoLive()` (near line 859), add one line after `StartPageTimer`:

```csharp
StartPageTimer(SelectedPage.Model.DurationMs, SelectedPage.Model.LoopToStart);
StartCountdownTimer(SelectedPage, SelectedOutput);   // ← add this line
```

- [ ] **Step 5: Hook StartCountdownTimer into GoLiveFromGroup**

In `GoLiveFromGroup()` (near line 2555), add one line after `StartPageTimer`. The local variable `output` is already in scope:

```csharp
StartPageTimer(pvm.Model.DurationMs, pvm.Model.LoopToStart, group.Package);
StartCountdownTimer(pvm, output);   // ← add this line
```

- [ ] **Step 6: Hook StartCountdownTimer into the Companion page_live flat-view path**

The flat-view Companion path (near line 500) calls `flatOutput.GoLive(...)` directly. The `flatPvm` variable is currently declared inside the `if` block. Restructure to expose it, then add the countdown call:

Find this block (near line 496–510):

```csharp
var flatOutput = OutputStates.FirstOrDefault(o => o.ActivePackage == pkg)
                 ?? SelectedOutput;
if (flatOutput is null) return Err("No output selected");
int idx = pkg.Pages.IndexOf(page);
flatOutput.GoLive(page, idx, NextTransitionType, NextTransitionDuration, 0.5f);
if (flatOutput == SelectedOutput)
{
    LoadPackageToSelectedOutput(pkg);
    var flatPvm = Pages.FirstOrDefault(p => p.Model == page);
    if (flatPvm is not null) SelectedPage = flatPvm;
}
StartPageTimer(page.DurationMs, page.LoopToStart);
```

Replace with:

```csharp
var flatOutput = OutputStates.FirstOrDefault(o => o.ActivePackage == pkg)
                 ?? SelectedOutput;
if (flatOutput is null) return Err("No output selected");
int idx = pkg.Pages.IndexOf(page);
flatOutput.GoLive(page, idx, NextTransitionType, NextTransitionDuration, 0.5f);
PageViewModel? flatPvm = null;
if (flatOutput == SelectedOutput)
{
    LoadPackageToSelectedOutput(pkg);
    flatPvm = Pages.FirstOrDefault(p => p.Model == page);
    if (flatPvm is not null) SelectedPage = flatPvm;
}
StartPageTimer(page.DurationMs, page.LoopToStart);
if (flatPvm is not null) StartCountdownTimer(flatPvm, flatOutput);
```

- [ ] **Step 7: Hook StopCountdownTimer into SetPageTimer**

In `SetPageTimer()` (near line 1082), the guard currently runs `StartPageTimer`. Expand it to a block:

```csharp
// Before:
if (pvm.Model == SelectedOutput?.LivePage)
    StartPageTimer(durationMs, loopToStart, pvm.Owner);

// After:
if (pvm.Model == SelectedOutput?.LivePage)
{
    StartPageTimer(durationMs, loopToStart, pvm.Owner);
    StartCountdownTimer(pvm, SelectedOutput);
}
```

- [ ] **Step 8: Hook StopCountdownTimer into ClearLive and ClearOutput**

`ClearLive` and `ClearOutput` are currently one-liners (near line 993–994). Expand them:

```csharp
// Before:
public void ClearLive()                    { StopPageTimer(); SelectedOutput?.Clear(); UpdateIsLiveFlags(); PushStateToCompanion(); }
public void ClearOutput(OutputState output) { StopPageTimer(); output.Clear(); UpdateIsLiveFlags(); }

// After:
public void ClearLive()
{
    StopPageTimer();
    StopCountdownTimer();
    SelectedOutput?.Clear();
    UpdateIsLiveFlags();
    PushStateToCompanion();
}

public void ClearOutput(OutputState output)
{
    StopPageTimer();
    if (output == _liveOutputForCountdown) StopCountdownTimer();
    output.Clear();
    UpdateIsLiveFlags();
}
```

- [ ] **Step 9: Run tests to verify they pass**

```
dotnet test ShowCast.Tests --filter "MainViewModelCountdownTests"
```

Expected: 8 tests pass.

- [ ] **Step 10: Run full test suite to confirm no regressions**

```
dotnet test ShowCast.Tests
```

Expected: all tests pass.

- [ ] **Step 11: Commit**

```
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MainViewModelCountdownTests.cs
git commit -m "feat: add countdown timer infrastructure to MainViewModel"
```

---

## Task 3: AXAML progress bar on page cards

**Files:**
- Modify: `Views/PageGridPanel.axaml`

Both DataTemplates in `PageGridPanel.axaml` share an identical page card structure. Apply the same two changes to each:

1. Update the timer badge text binding from `TimerLabel` to `LiveTimerLabel`
2. Add a `ProgressBar` between the thumbnail `<Grid>` and the name `<TextBlock>`

- [ ] **Step 1: Update flat-view DataTemplate (near line 111)**

Find in the flat-view DataTemplate (inside `<ListBox.ItemTemplate>`, around lines 104–114):

```xml
                                        <!-- Timer badge (bottom-right) -->
                                        <Border Background="#bb000000" CornerRadius="3"
                                                Padding="4,2" Margin="5"
                                                HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                                IsVisible="{Binding HasTimer}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <TextBlock Text="⏱" FontSize="8" Foreground="#dddddd"/>
                                                <TextBlock Text="{Binding TimerLabel}" FontSize="8"
                                                           FontWeight="Bold" Foreground="#dddddd"/>
                                            </StackPanel>
                                        </Border>
                                    </Grid>

                                    <!-- Page name -->
                                    <TextBlock Text="{Binding Name}"
```

Replace with:

```xml
                                        <!-- Timer badge (bottom-right) -->
                                        <Border Background="#bb000000" CornerRadius="3"
                                                Padding="4,2" Margin="5"
                                                HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                                IsVisible="{Binding HasTimer}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <TextBlock Text="⏱" FontSize="8" Foreground="#dddddd"/>
                                                <TextBlock Text="{Binding LiveTimerLabel}" FontSize="8"
                                                           FontWeight="Bold" Foreground="#dddddd"/>
                                            </StackPanel>
                                        </Border>
                                    </Grid>

                                    <!-- Progress bar: drains left-to-right while page is live -->
                                    <ProgressBar Value="{Binding ProgressFraction}"
                                                 Minimum="0" Maximum="1"
                                                 Height="3"
                                                 IsVisible="{Binding HasProgress}"
                                                 Foreground="#5599ff"
                                                 Background="#333333"
                                                 CornerRadius="0"/>

                                    <!-- Page name -->
                                    <TextBlock Text="{Binding Name}"
```

- [ ] **Step 2: Update grouped-rundown DataTemplate (near line 460)**

Find in the grouped-rundown DataTemplate (inside `<ItemsControl.ItemTemplate>`, around lines 454–465):

```xml
                                        <Border Background="#bb000000" CornerRadius="3"
                                                Padding="4,2" Margin="5"
                                                HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                                IsVisible="{Binding HasTimer}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <TextBlock Text="⏱" FontSize="8" Foreground="#dddddd"/>
                                                <TextBlock Text="{Binding TimerLabel}" FontSize="8"
                                                           FontWeight="Bold" Foreground="#dddddd"/>
                                            </StackPanel>
                                        </Border>
                                    </Grid>
                                    <TextBlock Text="{Binding Name}"
```

Replace with:

```xml
                                        <Border Background="#bb000000" CornerRadius="3"
                                                Padding="4,2" Margin="5"
                                                HorizontalAlignment="Right" VerticalAlignment="Bottom"
                                                IsVisible="{Binding HasTimer}">
                                            <StackPanel Orientation="Horizontal" Spacing="2">
                                                <TextBlock Text="⏱" FontSize="8" Foreground="#dddddd"/>
                                                <TextBlock Text="{Binding LiveTimerLabel}" FontSize="8"
                                                           FontWeight="Bold" Foreground="#dddddd"/>
                                            </StackPanel>
                                        </Border>
                                    </Grid>

                                    <!-- Progress bar: drains left-to-right while page is live -->
                                    <ProgressBar Value="{Binding ProgressFraction}"
                                                 Minimum="0" Maximum="1"
                                                 Height="3"
                                                 IsVisible="{Binding HasProgress}"
                                                 Foreground="#5599ff"
                                                 Background="#333333"
                                                 CornerRadius="0"/>

                                    <TextBlock Text="{Binding Name}"
```

- [ ] **Step 3: Build to confirm no AXAML errors**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run full test suite**

```
dotnet test ShowCast.Tests
```

Expected: all tests pass.

- [ ] **Step 5: Commit**

```
git add Views/PageGridPanel.axaml
git commit -m "feat: add progress bar and live countdown to page cards"
```
