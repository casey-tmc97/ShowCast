# Add to Rundown — Context Menu Feature

**Date:** 2026-06-11
**Status:** Approved

## Summary

When right-clicking a package in the ItemsPanel, add an "Add to Rundown" menu item with a flyout submenu listing every rundown the package is not already in. Clicking a rundown name adds a `RundownEntry` for that package. The feature is available in both ShowingShow and ShowingRundown modes.

## Scope

- Appears in both Show mode and Rundown mode context menus
- Submenu is a flat list (no folder hierarchy)
- Rundowns where the package is already present are hidden (not shown, not disabled)
- If all rundowns already contain the package, "Add to Rundown" is omitted entirely
- No visual feedback after adding (silent)

## Changes

### `MainViewModel.cs` — new method

```csharp
public void AddPackageToRundown(Package package, Rundown rundown)
{
    rundown.AddEntry(new RundownEntry { PackageId = package.Id });
}
```

No `RefreshPackageItems` call — the current panel view is not affected by adding to a different rundown.

### `ItemsPanel.axaml.cs` — `OnContextRequested()`

Insert between the existing RundownMode items and the "Remove" item:

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
```

## Resulting Menu Structure

| Mode | Available rundowns | Menu |
|------|-------------------|------|
| RundownMode | yes | Move Up / Move Down / ─── / Add to Rundown ▶ / ─── / Remove |
| RundownMode | no | Move Up / Move Down / ─── / Remove |
| ShowMode | yes | Add to Rundown ▶ / ─── / Remove |
| ShowMode | no | Remove |

## Out of Scope

- No submenu folder hierarchy (flat list only)
- No toast/status bar confirmation
- No duplicate entry support (existing entries filter the list)
