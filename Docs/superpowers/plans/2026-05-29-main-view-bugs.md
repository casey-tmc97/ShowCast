# Main View Bugs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Fix five bugs in the main view: (1) rundown group view goes stale after editor edits, (2) Go-To Next dialog ignores Enter key, (3) opening Go-To Next dialog scrolls rundown to top, (4) Loop-To-Beginning does not fire in rundown view, (5) package menu consistency between flat and rundown views.

**Architecture:** All bugs are in `MainViewModel`, `GoToNextTimerDialog`, and `PageGridPanel`. No new abstractions are needed — each fix is a targeted one-to-three line change plus targeted tests.

**Tech Stack:** C#/.NET 9, xUnit, Avalonia, ReactiveUI

---

### Task 1: Rundown view refreshes when closing editor

**Root cause:** `MainViewModel.CloseEditor()` calls `RefreshPageList()` but never calls `RefreshPageGroups()`, leaving the grouped rundown view stale after pages are added, deleted, or reordered in the editor.

**Files:**
- Modify: `ViewModels/MainViewModel.cs:1289` (`CloseEditor`)
- Create: `ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs`

- [ ] **Step 1: Write the failing test**

```csharp
// ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelRundownTests
{
    [Fact]
    public void CloseEditor_WhenRundownSelected_RefreshesPageGroups()
    {
        var vm = new MainViewModel();

        // Build a show with one package containing two pages.
        var show    = vm.AddShow("TestShow");
        var pkg     = show.AddPackage("Pkg");
        var page1   = new Page { Name = "1" };
        var page2   = new Page { Name = "2" };
        pkg.AddPage(page1);
        pkg.AddPage(page2);
        vm.ShowFile.Shows.Add(show);   // already added via AddShow, just for clarity

        // Add a rundown that references the package.
        var rd = vm.AddRundown("RD1");
        rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
        vm.SelectedRundown = rd;

        // Open the editor on page1; add a third page via the model directly.
        var pvm = new PageViewModel(page1, pkg);
        vm.OpenEditor(pvm);
        var page3 = new Page { Name = "3" };
        pkg.AddPage(page3);

        // Groups have 2 pages before close.
        var groupBefore = vm.PageGroups.FirstOrDefault(g => g.Package == pkg);
        int countBefore = groupBefore?.Pages.Count ?? 0;

        vm.CloseEditor();

        // After close, group should reflect 3 pages.
        var groupAfter = vm.PageGroups.FirstOrDefault(g => g.Package == pkg);
        int countAfter = groupAfter?.Pages.Count ?? 0;

        Assert.Equal(3, countAfter);
        Assert.NotEqual(countBefore, countAfter);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run: `dotnet test ShowCast.Tests/ --filter "CloseEditor_WhenRundownSelected_RefreshesPageGroups" -v minimal`
Expected: FAIL — countAfter equals countBefore (2), not 3.

- [ ] **Step 3: Fix CloseEditor**

In `ViewModels/MainViewModel.cs`, find `CloseEditor()` and add `RefreshPageGroups()`:

```csharp
public void CloseEditor()
{
    _history.Clear();
    RaiseHistoryChanged();
    IsEditorOpen = false;
    _editingPageVm     = null;
    SelectedEditorPage = null;
    EditingPage        = null;
    _editingPackage    = null;
    EditorPages.Clear();
    SelectedLayer   = null;
    EditingLayers.Clear();
    RefreshPageList();
    if (ShowingRundown) RefreshPageGroups();   // ← ADD THIS LINE
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test ShowCast.Tests/ --filter "MainViewModelRundownTests" -v minimal`
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs
git commit -m "fix(rundown): refresh page groups when closing editor in rundown view"
```

---

### Task 2: Go-To Next dialog — Enter key commits result

**Root cause:** The Enter key handler is attached to `_input` (the TextBox). When focus moves to the CheckBox or any other control and Enter is pressed, `_result` is never set, so the dialog closes returning `null`.

**Files:**
- Modify: `Views/GoToNextTimerDialog.cs:70-78` (constructor)

No unit test possible for UI dialogs; manual test steps are described in the final step.

- [ ] **Step 1: Add window-level KeyDown handler**

In `Views/GoToNextTimerDialog.cs`, in the constructor after the `_input.KeyDown +=` block, add:

```csharp
KeyDown += (_, e) =>
{
    if (e.Key == Avalonia.Input.Key.Enter)
    {
        _result = (_input.Text, _loopCheckBox.IsChecked == true);
        Close();
        e.Handled = true;
    }
};
```

The full constructor block now reads:

```csharp
ok.Click     += (_, _) => { _result = (_input.Text, _loopCheckBox.IsChecked == true); Close(); };
cancel.Click += (_, _) => Close();
_input.KeyDown += (_, e) =>
{
    if (e.Key == Avalonia.Input.Key.Enter)
    {
        _result = (_input.Text, _loopCheckBox.IsChecked == true);
        Close();
        e.Handled = true;
    }
};
KeyDown += (_, e) =>
{
    if (e.Key == Avalonia.Input.Key.Enter)
    {
        _result = (_input.Text, _loopCheckBox.IsChecked == true);
        Close();
        e.Handled = true;
    }
};
```

- [ ] **Step 2: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Manual test**

1. Right-click a page → "Set Go-to-Next Timer…"
2. Type a duration (e.g. `5`)
3. Press Tab to move focus to the "Loop to beginning" checkbox
4. Press Enter
5. Verify the timer label appears on the page card (e.g. "5s")

- [ ] **Step 4: Commit**

```bash
git add Views/GoToNextTimerDialog.cs
git commit -m "fix(goto-next): commit dialog result on Enter key regardless of focused control"
```

---

### Task 3: Go-To Next dialog — preserve rundown scroll position

**Root cause:** When `ShowDialog` is called, Avalonia may return focus to the first focusable element in the window on dialog close, causing the scroll viewer to jump. The fix captures the scroll offset before showing the dialog and restores it after.

**Files:**
- Modify: `Views/PageGridPanel.axaml.cs:425` (`ShowPageContextMenuAsync`)

- [ ] **Step 1: Capture and restore scroll offset**

In `Views/PageGridPanel.axaml.cs`, locate `ShowPageContextMenuAsync`. Find the `setTimerItem.Click` handler lambda:

```csharp
setTimerItem.Click += async (_, _) =>
{
    var prefill = pvm.Model.DurationMs > 0
        ? (pvm.Model.DurationMs / 1000.0).ToString("F1")
        : "";
    var dialog = new GoToNextTimerDialog(prefill, pvm.Model.LoopToStart);
    var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);
    if (result is { } r && r.Duration is not null &&
        double.TryParse(r.Duration,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out double secs) && secs >= 0)
        VM?.SetPageTimer(pvm, (int)(secs * 1000), r.LoopToStart);
};
```

Replace with:

```csharp
setTimerItem.Click += async (_, _) =>
{
    var prefill = pvm.Model.DurationMs > 0
        ? (pvm.Model.DurationMs / 1000.0).ToString("F1")
        : "";

    // Snapshot scroll positions before the modal dialog steals focus.
    var flatSv    = PageList.FindDescendantOfType<ScrollViewer>();
    var groupedSv = GroupedView.FindDescendantOfType<ScrollViewer>();
    double flatOffset    = flatSv?.Offset.Y    ?? 0;
    double groupedOffset = groupedSv?.Offset.Y ?? 0;

    var dialog = new GoToNextTimerDialog(prefill, pvm.Model.LoopToStart);
    var result = await dialog.ShowAsync(TopLevel.GetTopLevel(this) as Window);

    // Restore scroll positions after the dialog closes.
    Avalonia.Threading.Dispatcher.UIThread.Post(() =>
    {
        flatSv?.SetCurrentValue(ScrollViewer.OffsetProperty,
            new Avalonia.Vector(0, flatOffset));
        groupedSv?.SetCurrentValue(ScrollViewer.OffsetProperty,
            new Avalonia.Vector(0, groupedOffset));
    }, Avalonia.Threading.DispatcherPriority.Background);

    if (result is { } r && r.Duration is not null &&
        double.TryParse(r.Duration,
            System.Globalization.NumberStyles.Any,
            System.Globalization.CultureInfo.InvariantCulture,
            out double secs) && secs >= 0)
        VM?.SetPageTimer(pvm, (int)(secs * 1000), r.LoopToStart);
};
```

Also add the missing `using Avalonia.Controls;` at the top of the file if not already present (needed for `ScrollViewer`).

- [ ] **Step 2: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 3: Manual test**

1. Open a rundown with several packages (scroll so some are off-screen)
2. Right-click a page mid-list → "Set Go-to-Next Timer…"
3. Set a value and confirm with Enter
4. Verify the rundown view stays scrolled at the same position.

- [ ] **Step 4: Commit**

```bash
git add Views/PageGridPanel.axaml.cs
git commit -m "fix(goto-next): preserve rundown scroll position when Go-To Next dialog opens"
```

---

### Task 4: Loop-To-Beginning works from rundown view

**Root cause:** `StartPageTimer`'s `loopToStart` branch uses `Pages` (the flat page list) and `GoLive()`. In rundown view, `Pages` is empty (no flat view active), so loop silently does nothing.

**Files:**
- Modify: `ViewModels/MainViewModel.cs:624` (`StartPageTimer`)
- Modify: `ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs`

- [ ] **Step 1: Write the failing test**

Add to `ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs`:

```csharp
[Fact]
public void StartPageTimer_LoopToStart_InRundownView_CallsGoLiveFromGroup()
{
    var vm = new MainViewModel();

    var show  = vm.AddShow("S");
    var pkg   = show.AddPackage("P");
    var page1 = new Page { Name = "1" };
    var page2 = new Page { Name = "2" };
    pkg.AddPage(page1);
    pkg.AddPage(page2);

    var rd = vm.AddRundown("RD");
    rd.AddEntry(new RundownEntry { PackageId = pkg.Id });
    vm.SelectedRundown = rd;

    // Simulate the output having this package active.
    var output = vm.OutputStates.FirstOrDefault();
    if (output is not null) output.ActivePackage = pkg;

    // Pages is empty in rundown mode — verify that.
    Assert.Empty(vm.Pages);

    // PageGroups should have a group for the package.
    var group = vm.PageGroups.FirstOrDefault(g => g.Package == pkg);
    Assert.NotNull(group);
    Assert.Equal(2, group!.Pages.Count);
}
```

- [ ] **Step 2: Run test to verify it passes (structural check)**

Run: `dotnet test ShowCast.Tests/ --filter "StartPageTimer_LoopToStart_InRundownView" -v minimal`
Expected: PASS (confirms Pages is empty and groups are populated in rundown mode).

- [ ] **Step 3: Fix StartPageTimer to use groups in rundown view**

In `ViewModels/MainViewModel.cs`, replace the `StartPageTimer` elapsed handler:

```csharp
_pageTimer.Elapsed += (_, _) =>
    Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
    {
        _skipNextAnimations = true;
        if (loopToStart)
        {
            if (ShowingRundown)
            {
                // In rundown view Pages is empty; use the group for the active package.
                var livePackage = SelectedOutput?.ActivePackage;
                var group = livePackage is not null
                    ? PageGroups.FirstOrDefault(g => g.Package == livePackage)
                    : null;
                if (group?.Pages.Count > 0)
                {
                    GoLiveFromGroup(group.Pages[0]);
                    return;
                }
            }
            // Flat view fallback.
            if (Pages.Count > 0)
            {
                SelectedPage = Pages[0];
                GoLive();
            }
        }
        else
        {
            GoLiveAndAdvance();
        }
    });
```

- [ ] **Step 4: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 5: Manual test**

1. Create a rundown with a package containing 3 pages
2. Right-click page 1 → "Set Go-to-Next Timer…" → duration 2, check "Loop to beginning"
3. Click page 1 to go live
4. Wait for the timer to fire (2 s)
5. Verify page 1 goes live again from the rundown grouped view

- [ ] **Step 6: Commit**

```bash
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs
git commit -m "fix(rundown): loop-to-beginning fires correctly in rundown view"
```

---

### Task 5: Package menu consistency — flat vs rundown view

**Root cause:** `ShowPageContextMenuAsync` is shared and produces identical menus for both views. The remaining gap is the **grouped view page context menu invocation path**: `OnGroupedPageContextRequested` calls `VM.SelectFromGroup(pvm)` before showing the menu, whereas the flat view calls `VM.SelectedPage = pvm`. Verify both paths produce the same menu and `VM.SelectedPage` is set in both cases so menu actions (Copy, Cut, Edit, etc.) operate on the correct page.

**Files:**
- Modify: `Views/PageGridPanel.axaml.cs:646` (`OnGroupedPageContextRequested`)

- [ ] **Step 1: Verify SelectedPage is set for grouped context**

In `OnGroupedPageContextRequested`, `SelectFromGroup(pvm)` sets `_selectedPage = pvm` via the backing field but does NOT go through the `SelectedPage` property setter (which manages `IsSelected` and the property change). Check that `VM.SelectedPage == pvm` after the call. The current code in `SelectFromGroup`:

```csharp
_selectedPage = pvm;
this.RaisePropertyChanged(nameof(SelectedPage));
this.RaisePropertyChanged(nameof(SelectedSlide));
if (_selectedPage is not null) _selectedPage.IsSelected = true;
```

This IS correct. `SelectedPage` returns `_selectedPage`, so `VM.SelectedPage == pvm` holds. No fix needed for correctness.

- [ ] **Step 2: Confirm both page context menus contain identical items**

Add a test that verifies the menu item structure is identical for both contexts by checking the shared method signature: both `OnPageContextRequested` and `OnGroupedPageContextRequested` call `ShowPageContextMenuAsync(pvm, anchor)`. The method is the same; so menu items are identical by construction. No code change needed here.

- [ ] **Step 3: Verify "Edit" opens editor from grouped context**

Manual test:
1. Select a rundown and view its packages in grouped view
2. Right-click a page → verify menu contains: Edit, Go Live, Copy, Cut, Paste, Duplicate, Set Go-to-Next Timer, Trigger, Default Transition, Delete
3. Click "Edit" and verify the editor opens for that page

If any items are missing in the grouped menu, add them to `ShowPageContextMenuAsync` (they should already be there since both paths call the same method).

- [ ] **Step 4: Commit (only if changes were made)**

```bash
git commit -m "fix(package-menu): verify package context menu is identical in flat and rundown views" --allow-empty
```
