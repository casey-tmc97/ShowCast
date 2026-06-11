# Editor Page Multi-Select Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Ctrl+click / Shift+click multi-select to the editor filmstrip (`EditorShowPanel`) so users can bulk-delete, bulk-duplicate, and drag-reorder groups of pages.

**Architecture:** Avalonia's `ListBox` handles Ctrl/Shift selection natively when `SelectionMode="Multiple"`. `OnSelectionChanged` syncs the full selection to a new `SelectedEditorPages: IReadOnlyList<PageViewModel>` on `MainViewModel`. Drag packs all selected pages when the dragged page is in the selection. Three new VM methods (`RemoveSelectedEditorPages`, `DuplicateSelectedEditorPages`, `MovePages`) perform the bulk operations. Context menu labels and handlers switch on `SelectedEditorPages.Count`.

**Tech Stack:** C# / .NET 9, Avalonia UI, xUnit

---

## File Map

| File | Change |
|------|--------|
| `ShowCast.Tests/ViewModels/MainViewModelEditorMultiSelectTests.cs` | New test file — 5 ViewModel unit tests |
| `ViewModels/MainViewModel.cs` | Add `SelectedEditorPages`, `SetEditorPageSelection`, `RemoveSelectedEditorPages`, `DuplicateSelectedEditorPages`, `MovePages` |
| `Views/EditorShowPanel.axaml` | `SelectionMode="Multiple"` on `SlideList` |
| `Views/EditorShowPanel.axaml.cs` | Update `OnSelectionChanged`, `OnItemPointerMoved`, `OnDrop`, `OnPageContextRequested` |

---

## Key API Facts

These are easy to get wrong — read before implementing:

- `Package.IndexOf(Guid pageId)` → returns `int` index of the page, or -1. Takes a **Guid**, not a `Page`.
- `Package.InsertPage(int index, Page page)` → clamps index automatically.
- `Package.RemovePage(int index)` → takes an **int index**, NOT a `Page` or `Guid`. To remove by page reference use `package.Pages.Remove(page)` (standard `List<T>` method).
- `Package.Pages` is `List<Page>` — direct access is fine.
- `RebuildEditorPages(Page? current)` is a **private** method on `MainViewModel`. Rebuilds `EditorPages` from scratch from `_editingPackage.Pages`.
- `MovePage(src, tgt)` places `src` **just before** `tgt` — `MovePages` must mirror this semantic.
- `EditorPages` is `ObservableCollection<PageViewModel>` — use `EditorPages.Remove(pvm)` and `EditorPages.Insert(i, pvm)`.

---

### Task 1: Write failing ViewModel tests

**Files:**
- Create: `ShowCast.Tests/ViewModels/MainViewModelEditorMultiSelectTests.cs`

- [ ] **Step 1: Create the test file**

Create `ShowCast.Tests/ViewModels/MainViewModelEditorMultiSelectTests.cs`:

```csharp
using System.Linq;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelEditorMultiSelectTests
{
    // Creates a VM with an open editor on a 4-page package.
    // AddPackageToShow creates page[0]; we add pages[1..3] manually.
    // OpenEditor on page[0] calls RebuildEditorPages, giving us ep0..ep3.
    static (MainViewModel vm, Package pkg,
            PageViewModel ep0, PageViewModel ep1,
            PageViewModel ep2, PageViewModel ep3) Setup()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg = show.Packages.Last();

        pkg.AddPage(new Page { Name = "2" });
        pkg.AddPage(new Page { Name = "3" });
        pkg.AddPage(new Page { Name = "4" });

        var pvm0 = new PageViewModel(pkg.Pages[0], pkg);
        vm.OpenEditor(pvm0);

        return (vm, pkg,
                vm.EditorPages[0], vm.EditorPages[1],
                vm.EditorPages[2], vm.EditorPages[3]);
    }

    [Fact]
    public void SetEditorPageSelection_SetsSelectedEditorPages()
    {
        var (vm, _, _, ep1, ep2, _) = Setup();

        vm.SetEditorPageSelection(new[] { ep1, ep2 });

        Assert.Equal(2, vm.SelectedEditorPages.Count);
        Assert.Contains(ep1, vm.SelectedEditorPages);
        Assert.Contains(ep2, vm.SelectedEditorPages);
    }

    [Fact]
    public void RemoveSelectedEditorPages_RemovesPagesAndNavigatesToAdjacent()
    {
        var (vm, pkg, _, ep1, ep2, _) = Setup();
        vm.SetEditorPageSelection(new[] { ep1, ep2 });

        vm.RemoveSelectedEditorPages();

        Assert.Equal(2, pkg.Pages.Count);
        Assert.DoesNotContain(ep1.Model, pkg.Pages);
        Assert.DoesNotContain(ep2.Model, pkg.Pages);
        Assert.Equal(2, vm.EditorPages.Count);
        Assert.NotNull(vm.EditingPage);
    }

    [Fact]
    public void RemoveSelectedEditorPages_WhenAllPagesRemoved_ClosesEditor()
    {
        var vm   = new MainViewModel();
        var show = vm.AddShow("S");
        vm.AddPackageToShow("P", show);
        var pkg  = show.Packages.Last();
        var pvm0 = new PageViewModel(pkg.Pages[0], pkg);
        vm.OpenEditor(pvm0);

        vm.SetEditorPageSelection(new[] { vm.EditorPages[0] });
        vm.RemoveSelectedEditorPages();

        Assert.False(vm.IsEditorOpen);
    }

    [Fact]
    public void DuplicateSelectedEditorPages_InsertsGroupAfterLastSelected()
    {
        // Setup: pages [0,1,2,3] (ep0..ep3). Select ep1 and ep2.
        // After duplicate: [ep0, ep1, ep2, copy-of-ep1, copy-of-ep2, ep3]
        var (vm, pkg, ep0, ep1, ep2, ep3) = Setup();
        vm.SetEditorPageSelection(new[] { ep1, ep2 });

        vm.DuplicateSelectedEditorPages();

        Assert.Equal(6, pkg.Pages.Count);
        Assert.Equal(6, vm.EditorPages.Count);

        // ep0, ep1, ep2 stay at their original positions
        Assert.Equal(ep0.Model, pkg.Pages[0]);
        Assert.Equal(ep1.Model, pkg.Pages[1]);
        Assert.Equal(ep2.Model, pkg.Pages[2]);

        // Copies are at indices 3 and 4; ep3 is pushed to index 5
        Assert.Equal(ep3.Model, pkg.Pages[5]);

        // EditorPages[3] and [4] are the new copies; their Models
        // are distinct from the originals (Clone creates new Page objects)
        Assert.NotSame(ep1.Model, vm.EditorPages[3].Model);
        Assert.NotSame(ep2.Model, vm.EditorPages[4].Model);
        Assert.Same(ep3,          vm.EditorPages[5]);
    }

    [Fact]
    public void MovePages_MovesGroupJustBeforeTarget()
    {
        // Setup: [ep0, ep1, ep2, ep3]. Move ep0 and ep1 to just before ep3.
        // MovePage semantics: drop target = insert just before that target.
        // Expected result: [ep2, ep0, ep1, ep3]
        var (vm, pkg, ep0, ep1, ep2, ep3) = Setup();

        vm.MovePages(new[] { ep0, ep1 }, ep3);

        Assert.Equal(ep2.Model, pkg.Pages[0]);
        Assert.Equal(ep0.Model, pkg.Pages[1]);
        Assert.Equal(ep1.Model, pkg.Pages[2]);
        Assert.Equal(ep3.Model, pkg.Pages[3]);
    }
}
```

- [ ] **Step 2: Run to confirm compile failures**

```
dotnet test ShowCast.Tests --filter "MainViewModelEditorMultiSelectTests" -v normal
```

Expected: Build errors — `SetEditorPageSelection`, `RemoveSelectedEditorPages`, `DuplicateSelectedEditorPages`, `MovePages`, `SelectedEditorPages` not found.

- [ ] **Step 3: Commit**

```
git add ShowCast.Tests/ViewModels/MainViewModelEditorMultiSelectTests.cs
git commit -m "test: add failing multi-select editor page tests"
```

---

### Task 2: Implement ViewModel methods

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

Add all five members in the `// ── Page editor ───` region, after the existing `SelectedEditorPage` property (around line 1923).

- [ ] **Step 1: Add `SelectedEditorPages` and `SetEditorPageSelection` after `SelectedEditorPage`**

Find:
```csharp
    private PageViewModel? _selectedEditorPage;
    public PageViewModel? SelectedEditorPage
    {
        get => _selectedEditorPage;
        set => this.RaiseAndSetIfChanged(ref _selectedEditorPage, value);
    }
```

Insert immediately after:

```csharp
    public IReadOnlyList<PageViewModel> SelectedEditorPages { get; private set; }
        = Array.Empty<PageViewModel>();

    public void SetEditorPageSelection(IEnumerable<PageViewModel> pages)
    {
        SelectedEditorPages = pages.ToList();
    }
```

- [ ] **Step 2: Add `RemoveSelectedEditorPages` after `SetEditorPageSelection`**

```csharp
    public void RemoveSelectedEditorPages()
    {
        if (_editingPackage is null || SelectedEditorPages.Count == 0) return;
        var toRemove = SelectedEditorPages.ToList();

        var remaining = EditorPages.Except(toRemove).ToList();
        int firstIdx  = toRemove.Min(p => EditorPages.IndexOf(p));
        var next      = remaining.Count > 0
            ? remaining.ElementAtOrDefault(Math.Clamp(firstIdx, 0, remaining.Count - 1))
            : null;

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
```

- [ ] **Step 3: Add `DuplicateSelectedEditorPages` after `RemoveSelectedEditorPages`**

```csharp
    public void DuplicateSelectedEditorPages()
    {
        if (_editingPackage is null || SelectedEditorPages.Count == 0) return;
        var ordered = SelectedEditorPages
            .OrderBy(p => _editingPackage.IndexOf(p.Model.Id))
            .ToList();

        // Insert copies as a group immediately after the last selected page
        int pkgInsertAt = _editingPackage.IndexOf(ordered.Last().Model.Id) + 1;
        int editorBase  = EditorPages.IndexOf(ordered.Last());
        var newVms      = new List<PageViewModel>();
        foreach (var pvm in ordered)
        {
            var copy  = pvm.Model.Clone();
            _editingPackage.InsertPage(pkgInsertAt, copy);
            var newVm = new PageViewModel(copy, _editingPackage);
            EditorPages.Insert(editorBase + 1 + newVms.Count, newVm);
            newVms.Add(newVm);
            pkgInsertAt++;
        }
        RenameDefaultPages(_editingPackage);
        SwitchEditingPage(newVms.Last());
    }
```

- [ ] **Step 4: Add `MovePages` after `DuplicateSelectedEditorPages`**

`MovePages` mirrors `MovePage`'s "insert just before target" semantic. Each removed page that sits before the target decrements `targetIdx` to compensate for the shifted indices.

```csharp
    public void MovePages(IList<PageViewModel> srcs, PageViewModel target)
    {
        if (_editingPackage is null) return;
        var ordered   = srcs.OrderBy(p => _editingPackage.IndexOf(p.Model.Id)).ToList();
        int targetIdx = _editingPackage.IndexOf(target.Model.Id);

        foreach (var pvm in ordered)
        {
            int srcIdx = _editingPackage.IndexOf(pvm.Model.Id);
            _editingPackage.Pages.RemoveAt(srcIdx);
            if (srcIdx < targetIdx) targetIdx--;
        }
        targetIdx = Math.Clamp(targetIdx, 0, _editingPackage.Pages.Count);
        for (int i = 0; i < ordered.Count; i++)
            _editingPackage.InsertPage(targetIdx + i, ordered[i].Model);

        RebuildEditorPages(_editingPageVm?.Model ?? ordered[0].Model);
    }
```

- [ ] **Step 5: Run the 5 new tests**

```
dotnet test ShowCast.Tests --filter "MainViewModelEditorMultiSelectTests" -v normal
```

Expected: All 5 pass.

- [ ] **Step 6: Run the full suite**

```
dotnet test ShowCast.Tests
```

Expected: All tests pass (no regressions).

- [ ] **Step 7: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: add SelectedEditorPages and bulk page operations to MainViewModel"
```

---

### Task 3: AXAML + code-behind — selection sync and drag/drop

**Files:**
- Modify: `Views/EditorShowPanel.axaml` (one attribute change)
- Modify: `Views/EditorShowPanel.axaml.cs` (`OnSelectionChanged`, `OnItemPointerMoved`, `OnDrop`)

- [ ] **Step 1: Add `SelectionMode="Multiple"` to `SlideList`**

In `Views/EditorShowPanel.axaml`, find the `<ListBox x:Name="SlideList"` element (line 31–36). Replace:

```xml
            <ListBox x:Name="SlideList"
                     ItemsSource="{Binding EditorPages}"
                     SelectedItem="{Binding SelectedEditorPage}"
                     Background="Transparent"
                     Padding="0"
                     SelectionChanged="OnSelectionChanged">
```

With:

```xml
            <ListBox x:Name="SlideList"
                     ItemsSource="{Binding EditorPages}"
                     SelectedItem="{Binding SelectedEditorPage}"
                     SelectionMode="Multiple"
                     Background="Transparent"
                     Padding="0"
                     SelectionChanged="OnSelectionChanged">
```

- [ ] **Step 2: Update `OnSelectionChanged` to sync the full selection**

In `Views/EditorShowPanel.axaml.cs`, find:

```csharp
    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VM is null) return;
        if (SlideList.SelectedItem is PageViewModel pvm)
            VM.SwitchEditingPage(pvm);
    }
```

Replace with:

```csharp
    void OnSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (VM is null) return;
        VM.SetEditorPageSelection(SlideList.SelectedItems.Cast<PageViewModel>());
        if (SlideList.SelectedItem is PageViewModel pvm)
            VM.SwitchEditingPage(pvm);
    }
```

Verify `using System.Linq;` is present at the top of the file. If not, add it with the other `using` statements.

- [ ] **Step 3: Update `OnItemPointerMoved` to pack the whole selection when dragging a selected page**

In `Views/EditorShowPanel.axaml.cs`, inside `async void OnItemPointerMoved`, find:

```csharp
        var src = _dragging;
        _dragging = null;   // prevent re-entrance

        var data = new DataObject();
        data.Set("page", src);
        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);

        ClearDropTarget();
```

Replace with:

```csharp
        var src = _dragging;
        _dragging = null;   // prevent re-entrance

        var data      = new DataObject();
        var selection = VM?.SelectedEditorPages ?? Array.Empty<PageViewModel>();
        if (selection.Count > 1 && selection.Contains(src))
            data.Set("pages", selection.ToList());
        else
            data.Set("page", src);

        await DragDrop.DoDragDrop(e, data, DragDropEffects.Move);

        ClearDropTarget();
```

Verify `using System.Collections.Generic;` is present. If not, add it.

- [ ] **Step 4: Update `OnDrop` to handle multi-page drops**

In `Views/EditorShowPanel.axaml.cs`, replace the entire `void OnDrop` method body:

```csharp
    void OnDrop(object? sender, DragEventArgs e)
    {
        ClearDropTarget();
        var tgt = FindPvm(e.Source as Control);

        if (e.Data.Contains("pages") && tgt is not null)
        {
            var srcs = e.Data.Get("pages") as List<PageViewModel>;
            if (srcs is not null && !srcs.Contains(tgt))
                VM?.MovePages(srcs, tgt);
            e.Handled = true;
            return;
        }

        if (!e.Data.Contains("page")) return;

        var src = e.Data.Get("page") as PageViewModel;
        if (src is not null && tgt is not null && src != tgt)
            VM?.MovePage(src, tgt);

        e.Handled = true;
    }
```

- [ ] **Step 5: Build**

```
dotnet build
```

Expected: 0 errors.

- [ ] **Step 6: Run full test suite**

```
dotnet test ShowCast.Tests
```

Expected: All tests pass.

- [ ] **Step 7: Commit**

```
git add Views/EditorShowPanel.axaml Views/EditorShowPanel.axaml.cs
git commit -m "feat: enable multi-select in editor filmstrip with drag support"
```

---

### Task 4: Update context menu for multi-select

**Files:**
- Modify: `Views/EditorShowPanel.axaml.cs` (`OnPageContextRequested` only)

- [ ] **Step 1: Replace `OnPageContextRequested` entirely**

Find the entire `void OnPageContextRequested` method (around line 141–174) and replace it:

```csharp
    void OnPageContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        if (VM is null) return;

        var pvm = FindPvm(e.Source as Control);
        if (pvm is null) return;

        if (SlideList.SelectedItem != pvm)
            SlideList.SelectedItem = pvm;

        var isMulti = VM.SelectedEditorPages.Count > 1;
        var count   = VM.SelectedEditorPages.Count;
        var menu    = new ContextMenu();

        if (!isMulti)
        {
            var renameItem = new MenuItem { Header = "Rename…" };
            renameItem.Click += async (_, _) =>
            {
                var dlg  = new TextInputDialog("Rename Page", "Page name", pvm.Model.Name);
                var name = await dlg.ShowAsync(TopLevel.GetTopLevel(this) as Window);
                if (!string.IsNullOrWhiteSpace(name))
                    VM.RenamePage(pvm, name.Trim());
            };
            menu.Items.Add(renameItem);
        }

        var duplicateItem = new MenuItem
        {
            Header = isMulti ? $"Duplicate {count} Pages" : "Duplicate"
        };
        duplicateItem.Click += (_, _) =>
        {
            if (isMulti) VM.DuplicateSelectedEditorPages();
            else VM.DuplicatePage(pvm);
        };

        var deleteItem = new MenuItem
        {
            Header = isMulti ? $"Delete {count} Pages" : "Delete"
        };
        deleteItem.Click += (_, _) =>
        {
            if (isMulti) VM.RemoveSelectedEditorPages();
            else VM.RemovePage(pvm);
        };

        menu.Items.Add(duplicateItem);
        menu.Items.Add(new Separator());
        menu.Items.Add(deleteItem);
        menu.Open(e.Source as Control ?? (Control)sender!);
        e.Handled = true;
    }
```

- [ ] **Step 2: Build**

```
dotnet build
```

Expected: 0 errors.

- [ ] **Step 3: Run full test suite**

```
dotnet test ShowCast.Tests
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add Views/EditorShowPanel.axaml.cs
git commit -m "feat: update editor context menu for multi-select (bulk delete/duplicate)"
```
