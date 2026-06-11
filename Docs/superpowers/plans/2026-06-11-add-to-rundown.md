# Add to Rundown Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add an "Add to Rundown" submenu to the package right-click context menu so a package can be added to any rundown it isn't already in.

**Architecture:** A new `AddPackageToRundown(Package, Rundown)` method on `MainViewModel` appends a `RundownEntry` to the target rundown. `OnContextRequested()` in `ItemsPanel.axaml.cs` builds a dynamic submenu from all rundowns not already containing the package, appearing in both Show mode and Rundown mode.

**Tech Stack:** C# / .NET 9, Avalonia UI, xUnit

---

## File Map

| File | Change |
|------|--------|
| `ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs` | Add two new tests |
| `ViewModels/MainViewModel.cs` | Add `AddPackageToRundown` method |
| `Views/ItemsPanel.axaml.cs` | Add submenu block inside `OnContextRequested` |

---

### Task 1: Write failing tests for `AddPackageToRundown`

**Files:**
- Modify: `ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs`

- [ ] **Step 1: Add two tests at the bottom of `MainViewModelRundownTests`**

Open `ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs` and append these two tests inside the class body, before the final `}`:

```csharp
[Fact]
public void AddPackageToRundown_AddsEntryToTargetRundown()
{
    var vm = new MainViewModel();
    var show = vm.AddShow("S");
    vm.AddPackageToShow("P", show);
    var pkg = show.Packages.Last();

    var rd = vm.AddRundown("RD");

    vm.AddPackageToRundown(pkg, rd);

    Assert.Single(rd.Entries);
    Assert.Equal(pkg.Id, rd.Entries[0].PackageId);
}

[Fact]
public void AddPackageToRundown_DoesNotAffectCurrentView()
{
    var vm = new MainViewModel();
    var show = vm.AddShow("S");
    vm.AddPackageToShow("P", show);
    var pkg = show.Packages.Last();

    var rd1 = vm.AddRundown("RD1");
    rd1.AddEntry(new RundownEntry { PackageId = pkg.Id });
    vm.SelectedRundown = rd1;

    var rd2 = vm.AddRundown("RD2");

    var countBefore = vm.PackageItems.Count;
    vm.AddPackageToRundown(pkg, rd2);
    var countAfter = vm.PackageItems.Count;

    // Current view (rd1) is unchanged
    Assert.Equal(countBefore, countAfter);
    // Target rundown got the entry
    Assert.Single(rd2.Entries);
    Assert.Equal(pkg.Id, rd2.Entries[0].PackageId);
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test ShowCast.Tests --filter "AddPackageToRundown" -v normal
```

Expected: Both tests fail with `CS0117` or `MissingMethodException` — `AddPackageToRundown` doesn't exist yet.

---

### Task 2: Implement `AddPackageToRundown` on `MainViewModel`

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Locate the package management section**

In `ViewModels/MainViewModel.cs`, find the line:

```csharp
public void RemovePackageFromRundown(int index)
```

(around line 1580 — in the `// ── Rundown content management` block).

- [ ] **Step 2: Add the new method directly above `RemovePackageFromRundown`**

```csharp
public void AddPackageToRundown(Package package, Rundown rundown)
{
    rundown.AddEntry(new RundownEntry { PackageId = package.Id });
}
```

- [ ] **Step 3: Run the tests to confirm they pass**

```
dotnet test ShowCast.Tests --filter "AddPackageToRundown" -v normal
```

Expected: Both tests pass.

- [ ] **Step 4: Commit**

```
git add ShowCast.Tests/ViewModels/MainViewModelRundownTests.cs ViewModels/MainViewModel.cs
git commit -m "feat: add AddPackageToRundown method to MainViewModel"
```

---

### Task 3: Add the "Add to Rundown" submenu in `OnContextRequested`

**Files:**
- Modify: `Views/ItemsPanel.axaml.cs`

- [ ] **Step 1: Locate the insertion point**

In `Views/ItemsPanel.axaml.cs`, find `OnContextRequested`. The relevant section looks like this (around line 183):

```csharp
        if (VM.ShowingRundown)
        {
            // ... Move Up / Move Down / Separator ...
        }

        var remove = new MenuItem { Header = "Remove" };
```

The new submenu block goes between the closing `}` of the `if (VM.ShowingRundown)` block and the `var remove = ...` line.

- [ ] **Step 2: Insert the submenu block**

Replace:

```csharp
        var remove = new MenuItem { Header = "Remove" };
        remove.Click += (_, _) =>
```

With:

```csharp
        var availableRundowns = VM.ShowFile.Rundowns
            .Where(rd => !rd.Entries.Any(e => e.PackageId == package.Id))
            .ToList();

        if (availableRundowns.Count > 0)
        {
            var addToRundown = new MenuItem { Header = "Add to Rundown" };
            foreach (var rd in availableRundowns)
            {
                var captured = rd;
                var rdItem = new MenuItem { Header = captured.Name };
                rdItem.Click += (_, _) => VM.AddPackageToRundown(package, captured);
                addToRundown.Items.Add(rdItem);
            }
            menu.Items.Add(addToRundown);
            menu.Items.Add(new Separator());
        }

        var remove = new MenuItem { Header = "Remove" };
        remove.Click += (_, _) =>
```

- [ ] **Step 3: Add the missing `using` for LINQ if needed**

Check the top of `ItemsPanel.axaml.cs` for `using System.Linq;`. If it's absent, add it after the existing `using` statements.

- [ ] **Step 4: Build to confirm no compile errors**

```
dotnet build ShowCast
```

Expected: Build succeeds with 0 errors.

- [ ] **Step 5: Run the full test suite**

```
dotnet test ShowCast.Tests
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add Views/ItemsPanel.axaml.cs
git commit -m "feat: add 'Add to Rundown' submenu to package context menu"
```
