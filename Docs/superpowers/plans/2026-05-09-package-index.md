# Package Lookup Optimization Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace O(N) package and page-owner lookups in ShowCast's ViewModels with an O(1) dictionary index and owner reference on `PageViewModel`.

**Architecture:** `PageViewModel` gains a `Package? Owner` property set at construction, eliminating all `FindPackageForPage` calls. A `Dictionary<Guid, Package> _packageById` field on `MainViewModel` is populated at load and maintained at the two package-mutation callsites, replacing all `_showFile.FindPackage` calls inside MainViewModel. Duplicate lookup methods in `AddEditEventViewModel` and `SchedulerViewModel` are deleted and routed to `_showFile.FindPackage` (linear scan acceptable there — only called on dialog open).

**Tech Stack:** C# .NET 9, ReactiveUI, no new dependencies.

---

## File Map

| File | Change |
|---|---|
| `ViewModels/PageViewModel.cs` | Add `Package? Owner` property; add `owner` param to constructor |
| `ViewModels/PageGroupViewModel.cs` | Pass `package` to `new PageViewModel` |
| `ViewModels/MainViewModel.cs` | Add `_packageById`; update `RebuildFromShowFile`, `AddPackageToShow`, `RemovePackageFromShow`; replace all `_showFile.FindPackage` and `FindPackageForPage` calls; delete `FindPackageForPage` |
| `ViewModels/AddEditEventViewModel.cs` | Delete `FindPackage`, call `_showFile.FindPackage` |
| `ViewModels/SchedulerViewModel.cs` | Delete `FindPackageName`, call `_showFile.FindPackage` |

---

## Task 1: Add `Owner` to `PageViewModel` and update all construction callsites

**Files:**
- Modify: `ViewModels/PageViewModel.cs`
- Modify: `ViewModels/PageGroupViewModel.cs`
- Modify: `ViewModels/MainViewModel.cs`

This task changes the `PageViewModel` constructor and fixes every callsite in one commit so the build stays green throughout.

- [ ] **Step 1: Update `PageViewModel` constructor**

In `ViewModels/PageViewModel.cs`, change lines 14–20 from:

```csharp
public Page Model { get; }

public PageViewModel(Page page)
{
    Model = page;
    RebuildThumbnail();
}
```

To:

```csharp
public Page Model { get; }
public Package? Owner { get; }

public PageViewModel(Page page, Package? owner = null)
{
    Model = page;
    Owner = owner;
    RebuildThumbnail();
}
```

The `owner` parameter defaults to `null` so existing callsites still compile while this task is in progress.

- [ ] **Step 2: Update callsite in `PageGroupViewModel.cs`**

In `ViewModels/PageGroupViewModel.cs` around line 68, change:

```csharp
foreach (var page in package.Pages)
{
    var pvm = new PageViewModel(page);
    pvm.IsLive = page == livePage;
    Pages.Add(pvm);
}
```

To:

```csharp
foreach (var page in package.Pages)
{
    var pvm = new PageViewModel(page, package);
    pvm.IsLive = page == livePage;
    Pages.Add(pvm);
}
```

- [ ] **Step 3: Update all 8 callsites in `MainViewModel.cs`**

Locate and change each `new PageViewModel(...)` call. Find them by searching for `new PageViewModel` in the file. Make these changes (the package variable is named in context at each site):

**`SyncPageCollectionAfterOrderChange` — two callsites, both loop over `pkg.Pages`:**
```csharp
// Both occurrences: change
var pvm = new PageViewModel(page);
// to
var pvm = new PageViewModel(page, pkg);
```

**`RebuildEditorPages` — loops over `_editingPackage.Pages`:**
```csharp
// Change
var vm = new PageViewModel(page);
// to
var vm = new PageViewModel(page, _editingPackage);
```

**`AddPage` — `package` is `SelectedOutput?.ActivePackage`:**
```csharp
// Change
var pvm = new PageViewModel(page);
// to
var pvm = new PageViewModel(page, package);
```

**`DuplicatePage` — `package` is resolved just above:**
```csharp
// Change
var newVm = new PageViewModel(copy);
// to
var newVm = new PageViewModel(copy, package);
```

**`PastePage` — `package` is resolved just above:**
```csharp
// Change
var newVm = new PageViewModel(copy);
// to
var newVm = new PageViewModel(copy, package);
```

**`AddPageToGroup` — `package` is `group.Package`:**
```csharp
// Change
var groupVm = new PageViewModel(page);
// to
var groupVm = new PageViewModel(page, package);
```

**`RefreshPageList` — `package` is `SelectedOutput?.ActivePackage`:**
```csharp
// Change
var pvm = new PageViewModel(page);
// to
var pvm = new PageViewModel(page, package);
```

- [ ] **Step 4: Build and verify**

```
dotnet build ShowCast.csproj
```

Expected: 0 errors. (Pre-existing warnings about AVLN3001 and nullable refs are fine.)

- [ ] **Step 5: Run existing tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: Passed! — 8/8.

- [ ] **Step 6: Commit**

```
git add ViewModels/PageViewModel.cs ViewModels/PageGroupViewModel.cs ViewModels/MainViewModel.cs
git commit -m "feat: add Owner property to PageViewModel"
```

---

## Task 2: Add `_packageById` dictionary to `MainViewModel`

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

This task adds the O(1) package-by-ID index and replaces the four `_showFile.FindPackage` calls inside `MainViewModel`.

- [ ] **Step 1: Add the dictionary field**

In `ViewModels/MainViewModel.cs`, find where other private fields are declared near the top of the class (look for `ShowFile _showFile` or similar). Add this field nearby:

```csharp
readonly Dictionary<Guid, Package> _packageById = new();
```

- [ ] **Step 2: Populate the dictionary in `RebuildFromShowFile`**

In `RebuildFromShowFile`, after the `OutputStates.Clear();` line and before the `foreach (var def in _showFile.Timers)` block, add:

```csharp
_packageById.Clear();
foreach (var show in _showFile.Shows)
    foreach (var pkg in show.Packages)
        _packageById[pkg.Id] = pkg;
```

- [ ] **Step 3: Update `_showFile.FindPackage` in `RebuildFromShowFile`**

Around line 159 in `RebuildFromShowFile`, change:

```csharp
state.ActivePackage = _showFile.FindPackage(cfg.ActivePackageId);
```

To:

```csharp
state.ActivePackage = _packageById.TryGetValue(cfg.ActivePackageId, out var activePkg) ? activePkg : null;
```

- [ ] **Step 4: Update `_showFile.FindPackage` in `TickScheduler`**

Around line 561 in `TickScheduler`, change:

```csharp
var pkg = _showFile.FindPackage(evt.PackageId);
```

To:

```csharp
_packageById.TryGetValue(evt.PackageId, out var pkg);
```

- [ ] **Step 5: Update `ShowFile.FindPackage` in `RefreshPackageItems`**

Around line 1737 in `RefreshPackageItems`, change:

```csharp
var pkg = ShowFile.FindPackage(entry.PackageId);
```

To:

```csharp
_packageById.TryGetValue(entry.PackageId, out var pkg);
```

- [ ] **Step 6: Update `ShowFile.FindPackage` in `RefreshPageGroups`**

Around line 1754 in `RefreshPageGroups`, change:

```csharp
var pkg = ShowFile.FindPackage(entry.PackageId);
```

To:

```csharp
_packageById.TryGetValue(entry.PackageId, out var pkg);
```

- [ ] **Step 7: Maintain the index in `AddPackageToShow`**

In `AddPackageToShow` (around line 728), after `var package = targetShow.AddPackage(name);`, add:

```csharp
_packageById[package.Id] = package;
```

- [ ] **Step 8: Maintain the index in `RemovePackageFromShow`**

In `RemovePackageFromShow` (around line 756), after `_selectedShow.RemovePackage(package.Id);`, add:

```csharp
_packageById.Remove(package.Id);
```

- [ ] **Step 9: Build and run tests**

```
dotnet build ShowCast.csproj
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: 0 errors, 8/8 tests pass.

- [ ] **Step 10: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: add _packageById index to MainViewModel, replace FindPackage calls"
```

---

## Task 3: Replace `FindPackageForPage` callsites with `pvm.Owner`, delete the method

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Update `OpenEditor`**

Around line 1099, change:

```csharp
_editingPackage = FindPackageForPage(pvm.Model);
```

To:

```csharp
_editingPackage = pvm.Owner;
```

- [ ] **Step 2: Update `DuplicatePage`**

Around line 1371, change:

```csharp
var package = IsEditorOpen ? _editingPackage : (FindPackageForPage(pvm?.Model!) ?? SelectedOutput?.ActivePackage);
```

To:

```csharp
var package = IsEditorOpen ? _editingPackage : (pvm?.Owner ?? SelectedOutput?.ActivePackage);
```

- [ ] **Step 3: Update `MovePageToPackage`**

Around line 1422, change:

```csharp
var sourcePackage = FindPackageForPage(pvm.Model);
```

To:

```csharp
var sourcePackage = pvm.Owner;
```

- [ ] **Step 4: Update `CutPage`**

Around line 1465, change:

```csharp
var package = FindPackageForPage(pvm.Model) ?? SelectedOutput?.ActivePackage;
```

To:

```csharp
var package = pvm.Owner ?? SelectedOutput?.ActivePackage;
```

- [ ] **Step 5: Delete `FindPackageForPage`**

Delete the entire `FindPackageForPage` method (around lines 1149–1156):

```csharp
Package? FindPackageForPage(Page page)
{
    foreach (var show in ShowFile.Shows)
        foreach (var pkg in show.Packages)
            if (pkg.Pages.Contains(page))
                return pkg;
    return null;
}
```

- [ ] **Step 6: Build and run tests**

```
dotnet build ShowCast.csproj
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: 0 errors, 8/8 tests pass.

- [ ] **Step 7: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "refactor: replace FindPackageForPage with pvm.Owner, delete method"
```

---

## Task 4: Delete duplicate lookup methods in `AddEditEventViewModel` and `SchedulerViewModel`

**Files:**
- Modify: `ViewModels/AddEditEventViewModel.cs`
- Modify: `ViewModels/SchedulerViewModel.cs`

- [ ] **Step 1: Update `AddEditEventViewModel`**

In `ViewModels/AddEditEventViewModel.cs`, find the `FindPackage` callsite around line 228:

```csharp
var pkg = FindPackage(entry.PackageId);
```

Change it to:

```csharp
var pkg = _showFile.FindPackage(entry.PackageId);
```

Then delete the private `FindPackage` method (around lines 234–240):

```csharp
Package? FindPackage(Guid id)
{
    foreach (var show in _showFile.Shows)
        foreach (var pkg in show.Packages)
            if (pkg.Id == id) return pkg;
    return null;
}
```

- [ ] **Step 2: Update `SchedulerViewModel`**

In `ViewModels/SchedulerViewModel.cs`, find the `FindPackageName` callsite around line 228:

```csharp
var pkg = FindPackageName(evt.PackageId);
```

Change it to:

```csharp
var pkg = _showFile.FindPackage(evt.PackageId)?.Name ?? "(deleted)";
```

Then delete the private `FindPackageName` method (around lines 233–238):

```csharp
string FindPackageName(Guid packageId)
{
    foreach (var show in _showFile.Shows)
        foreach (var pkg in show.Packages)
            if (pkg.Id == packageId) return pkg.Name;
    return "(deleted)";
}
```

- [ ] **Step 3: Build and run tests**

```
dotnet build ShowCast.csproj
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: 0 errors, 8/8 tests pass.

- [ ] **Step 4: Commit**

```
git add ViewModels/AddEditEventViewModel.cs ViewModels/SchedulerViewModel.cs
git commit -m "refactor: remove duplicate FindPackage methods, route to ShowFile.FindPackage"
```
