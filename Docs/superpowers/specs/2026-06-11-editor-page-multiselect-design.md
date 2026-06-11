# Editor Page Multi-Select Design

**Date:** 2026-06-11
**Status:** Approved

## Summary

Add multi-select to the editor filmstrip (`EditorShowPanel`). Users can Ctrl+click to toggle individual pages and Shift+click for range selection. Selected pages can be bulk-deleted, bulk-duplicated, and drag-reordered as a group.

## Scope

- Ctrl+click: toggle a page in/out of selection
- Shift+click: extend selection to a range
- Drag: if the dragged page is part of a multi-selection, all selected pages move together to the drop position
- Delete via context menu: removes all selected pages
- Duplicate via context menu: duplicates all selected pages in order, inserts copies after the last selected page
- Rename: hidden in context menu when multiple pages are selected
- Context menu labels show count when multi-select: "Duplicate 3 Pages", "Delete 3 Pages"
- Editing canvas (right panel) always shows the primary (last-clicked) page

## Out of Scope

- Keyboard shortcuts (Delete key, Ctrl+D) — not added in this iteration
- Multi-select in the main PageGridPanel — only the editor filmstrip
- Drag-selecting (rubber-band lasso)

## File Map

| File | Change |
|------|--------|
| `Views/EditorShowPanel.axaml` | `SelectionMode="Multiple"` on `SlideList` |
| `Views/EditorShowPanel.axaml.cs` | Sync selection to VM; multi-page drag/drop; context menu |
| `ViewModels/MainViewModel.cs` | `SelectedEditorPages`, `SetEditorPageSelection`, `RemoveSelectedEditorPages`, `DuplicateSelectedEditorPages`, `MovePages` |

## Design

### AXAML

One attribute change on `SlideList`:

```xml
<ListBox Name="SlideList" SelectionMode="Multiple" ...>
```

Avalonia handles Ctrl+click (toggle) and Shift+click (range) natively. The existing `SelectedItem="{Binding SelectedEditorPage}"` binding continues to track the primary page for the editing canvas.

### ViewModel (`MainViewModel.cs`)

```csharp
public IReadOnlyList<PageViewModel> SelectedEditorPages { get; private set; } = Array.Empty<PageViewModel>();

public void SetEditorPageSelection(IEnumerable<PageViewModel> pages)
{
    SelectedEditorPages = pages.ToList();
}

public void RemoveSelectedEditorPages()
{
    if (_editingPackage is null || SelectedEditorPages.Count == 0) return;
    var toRemove = SelectedEditorPages.ToList();

    // Find first non-removed page to navigate to after deletion
    var remaining = EditorPages.Except(toRemove).ToList();
    int firstIdx = toRemove.Min(p => EditorPages.IndexOf(p));
    var next = remaining.ElementAtOrDefault(Math.Clamp(firstIdx, 0, remaining.Count - 1));

    foreach (var pvm in toRemove)
    {
        _editingPackage.Pages.Remove(pvm.Model);
        EditorPages.Remove(pvm);
    }
    RenameDefaultPages(_editingPackage);
    SelectedEditorPages = Array.Empty<PageViewModel>();

    if (next is not null) SwitchEditingPage(next);
    else CloseEditor();
}
// Note: batch removal (not calling RemovePage in a loop) avoids multiple navigation side-effects

public void DuplicateSelectedEditorPages()
{
    if (_editingPackage is null || SelectedEditorPages.Count == 0) return;
    var ordered = SelectedEditorPages
        .OrderBy(p => _editingPackage.IndexOf(p.Model))
        .ToList();

    // Insert all copies after the last selected page
    int insertAt = _editingPackage.IndexOf(ordered.Last().Model) + 1;
    var newVms = new List<PageViewModel>();
    foreach (var pvm in ordered)
    {
        var copy = pvm.Model.Clone();  // deep copy: Transition, Easing, layers, timer/audio triggers
        _editingPackage.InsertPage(insertAt, copy);
        var newVm = new PageViewModel(copy, _editingPackage);
        int editorInsertAt = EditorPages.IndexOf(pvm) + 1 + newVms.Count;
        EditorPages.Insert(editorInsertAt, newVm);
        newVms.Add(newVm);
        insertAt++;
    }
    RenameDefaultPages(_editingPackage);
    SwitchEditingPage(newVms.Last());
}
// Note: uses Page.Clone() which is more complete than DuplicatePage's manual copy

public void MovePages(IList<PageViewModel> srcs, PageViewModel target)
{
    if (_editingPackage is null) return;
    var ordered = srcs.OrderBy(p => _editingPackage.IndexOf(p.Model)).ToList();
    int targetIdx = _editingPackage.IndexOf(target.Model);
    foreach (var pvm in ordered)
        _editingPackage.RemovePage(pvm.Model.Id);
    targetIdx = Math.Clamp(targetIdx, 0, _editingPackage.Pages.Count);
    for (int i = 0; i < ordered.Count; i++)
        _editingPackage.InsertPage(targetIdx + i, ordered[i].Model);
    RebuildEditorPages(_editingPageVm?.Model ?? ordered[0].Model);
}
```

### Code-behind (`EditorShowPanel.axaml.cs`)

**`OnSelectionChanged`:**
```csharp
void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
{
    if (VM is null) return;
    VM.SetEditorPageSelection(SlideList.SelectedItems.Cast<PageViewModel>());
    if (SlideList.SelectedItem is PageViewModel pvm)
        VM.SwitchEditingPage(pvm);
}
```

**`OnItemPointerMoved` (drag initiation):**
```csharp
var selection = VM?.SelectedEditorPages ?? Array.Empty<PageViewModel>();
if (selection.Count > 1 && selection.Contains(src))
    data.Set("pages", selection.ToList());
else
    data.Set("page", src);
```

**`OnDrop`:**
```csharp
if (e.Data.Contains("pages") && tgt is not null)
{
    var srcs = e.Data.Get("pages") as List<PageViewModel>;
    if (srcs is not null && !srcs.Contains(tgt))
        VM?.MovePages(srcs, tgt);
    e.Handled = true;
    return;
}
// existing single-page drop follows
```

**Context menu:**
- `isMulti = VM.SelectedEditorPages.Count > 1`
- Rename: hidden when `isMulti`
- Duplicate header: `"Duplicate"` or `"Duplicate N Pages"` — calls `DuplicateSelectedEditorPages()` or `DuplicatePage(pvm)`
- Delete header: `"Delete"` or `"Delete N Pages"` — calls `RemoveSelectedEditorPages()` or `RemovePage(pvm)`

