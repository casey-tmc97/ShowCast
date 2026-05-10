# Package Lookup Optimization — Design Spec

**Date:** 2026-05-09
**Status:** Approved
**Scope:** `ViewModels/PageViewModel.cs`, `ViewModels/PageGroupViewModel.cs`, `ViewModels/MainViewModel.cs`, `ViewModels/AddEditEventViewModel.cs`, `ViewModels/SchedulerViewModel.cs`

---

## Context

ShowCast shows can be large. Three separate ViewModels duplicate package-lookup logic with nested linear scans:

- `MainViewModel.FindPackageForPage(Page)` — O(shows × packages × pages), called on page selection, move, copy, and editor open
- `MainViewModel` uses `_showFile.FindPackage(Guid)` — O(shows × packages), called on output rebuild, scheduled event firing, and rundown operations
- `AddEditEventViewModel.FindPackage(Guid)` — exact duplicate of `ShowFile.FindPackage`
- `SchedulerViewModel.FindPackageName(Guid)` — partial duplicate, returns name instead of object

The fix consolidates all lookups and makes the hot paths O(1) without changing any behavior or data model.

---

## Decisions

| Question | Decision |
|---|---|
| Index location | `MainViewModel` field — mutation surface is exactly 2 public methods there |
| Page→Package lookup | Eliminate via `PageViewModel.Owner` property — VM already has the answer |
| `AddEditEventViewModel` / `SchedulerViewModel` | Route to `_showFile.FindPackage` — linear scan acceptable, only called on dialog open |
| Data model changes | None — `ShowFile` and `Show` unchanged |
| Serialization impact | None |

---

## Architecture

### 1. `PageViewModel` — add `Owner` property

```csharp
public Package Owner { get; }

public PageViewModel(Page page, Package owner)
{
    Model = page;
    Owner = owner;
    RebuildThumbnail();
}
```

Every `new PageViewModel(page)` callsite already has the owning `Package` in scope. With `Owner` on the VM, all `FindPackageForPage(pvm.Model)` calls become `pvm.Owner` — O(1), no lookup.

---

### 2. `PageGroupViewModel` — pass owner at construction

`PageGroupViewModel` creates `PageViewModel` instances and already holds a `Package` reference. Update the construction call to pass it.

---

### 3. `MainViewModel` — `_packageById` dictionary

**New field:**
```csharp
readonly Dictionary<Guid, Package> _packageById = new();
```

**Populated in `RebuildFromShowFile`** (after clearing, before restoring settings):
```csharp
_packageById.Clear();
foreach (var show in _showFile.Shows)
    foreach (var pkg in show.Packages)
        _packageById[pkg.Id] = pkg;
```

**Updated in `AddPackageToShow`** (after `targetShow.AddPackage`):
```csharp
_packageById[package.Id] = package;
```

**Updated in `RemovePackageFromShow`** (after `_selectedShow.RemovePackage`):
```csharp
_packageById.Remove(package.Id);
```

**All `_showFile.FindPackage(id)` calls inside `MainViewModel`** replaced with:
```csharp
_packageById.TryGetValue(id, out var pkg) ? pkg : null
```

**`FindPackageForPage` deleted.** All call sites become `pvm.Owner`.

---

### 4. `AddEditEventViewModel` — remove duplicate

Delete `FindPackage(Guid id)` private method. Replace its call site:
```csharp
// Before:
var pkg = FindPackage(entry.PackageId);

// After:
var pkg = _showFile.FindPackage(entry.PackageId);
```

---

### 5. `SchedulerViewModel` — remove duplicate

Delete `FindPackageName(Guid packageId)` private method. Replace its call site:
```csharp
// Before:
var pkg = FindPackageName(evt.PackageId);

// After:
var pkg = _showFile.FindPackage(evt.PackageId)?.Name ?? "(deleted)";
```

---

## What This Is Not

- No changes to `ShowFile`, `Show`, or any Core data model
- No new serialization fields
- No DI or service locator — the dictionary is a private `MainViewModel` field
- No index for Rundown or other entity types — only packages are hot-path lookups

---

## Files Changed

| File | Change |
|---|---|
| `ViewModels/PageViewModel.cs` | Add `Owner` property and update constructor |
| `ViewModels/PageGroupViewModel.cs` | Pass owning package to `PageViewModel` constructor |
| `ViewModels/MainViewModel.cs` | Add `_packageById`, populate/maintain it, replace all lookup calls, delete `FindPackageForPage` |
| `ViewModels/AddEditEventViewModel.cs` | Delete `FindPackage`, route to `_showFile.FindPackage` |
| `ViewModels/SchedulerViewModel.cs` | Delete `FindPackageName`, route to `_showFile.FindPackage` |
