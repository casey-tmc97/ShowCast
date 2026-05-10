# Core Logic Tests — Design Spec

**Date:** 2026-05-09
**Status:** Approved
**Scope:** `ShowCast.Tests/Core/EditorHistoryTests.cs`, `ShowCast.Tests/Core/ShowFileTests.cs`, `ShowCast.Tests/Core/PageTests.cs`

---

## Context

ShowCast ships in two weeks with ongoing updates planned. The existing 8 tests cover only the file serializer. Three areas contain non-trivial pure logic — no SkiaSharp surfaces or NDI — that are realistic regression targets as features are added:

- `EditorHistory` — undo/redo state machine with depth capping and redo-clear-on-push
- `ShowFile.RemoveRundownFolder` — reparents orphaned rundowns to the deleted folder's parent
- `Page.LayersForRoles` / `Page.Clone` — role bitmask filtering and deep copy

Rendering (`PageRenderer`, `TransitionCompositor`) requires a live `SKCanvas` and is not testable headlessly.

---

## Test Files

| File | Tests | Target |
|---|---|---|
| `ShowCast.Tests/Core/EditorHistoryTests.cs` | 6 | `Core/EditorHistory.cs` |
| `ShowCast.Tests/Core/ShowFileTests.cs` | 4 | `Core/ShowFile.cs` — `RemoveRundownFolder` |
| `ShowCast.Tests/Core/PageTests.cs` | 5 | `Core/Page.cs` — `LayersForRoles`, `Clone` |

---

## EditorHistory Tests

`EditorHistory` works by snapshotting `page.Layers` *before* a mutation. `Push` saves the current state; `Undo` swaps back; `Redo` re-applies. `MaxDepth = 99`.

```csharp
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class EditorHistoryTests
{
    static Page PageWithLayer(string name) =>
        new Page().Also(p => p.AddLayer(new SlideLayer { Name = name, ZOrder = 0 }));

    [Fact]
    public void Push_SetsCanUndo_True()
    {
        var history = new EditorHistory();
        var page    = PageWithLayer("A");

        history.Push(page);

        Assert.True(history.CanUndo);
        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Undo_RestoresPreviousState_AndSetsCanRedo()
    {
        var history = new EditorHistory();
        var page    = PageWithLayer("A");

        history.Push(page);                        // snapshot: [A]
        page.Layers.Clear();
        page.AddLayer(new SlideLayer { Name = "B", ZOrder = 0 });

        var result = history.Undo(page);

        Assert.True(result);
        Assert.False(history.CanUndo);
        Assert.True(history.CanRedo);
        Assert.Equal("A", page.Layers[0].Name);
    }

    [Fact]
    public void Redo_ReappliesChange_AndClearsCanRedo()
    {
        var history = new EditorHistory();
        var page    = PageWithLayer("A");

        history.Push(page);
        page.Layers.Clear();
        page.AddLayer(new SlideLayer { Name = "B", ZOrder = 0 });
        history.Undo(page);

        var result = history.Redo(page);

        Assert.True(result);
        Assert.False(history.CanRedo);
        Assert.Equal("B", page.Layers[0].Name);
    }

    [Fact]
    public void Push_ClearsRedoStack()
    {
        var history = new EditorHistory();
        var page    = PageWithLayer("A");

        history.Push(page);
        history.Undo(page);
        Assert.True(history.CanRedo);

        // new push should wipe redo
        history.Push(page);

        Assert.False(history.CanRedo);
    }

    [Fact]
    public void Push_CappsUndoStackAt99()
    {
        var history = new EditorHistory();
        var page    = new Page();

        for (int i = 0; i < 105; i++)
            history.Push(page);

        int undoCount = 0;
        while (history.Undo(page)) undoCount++;

        Assert.Equal(99, undoCount);
    }

    [Fact]
    public void Undo_OnEmptyStack_ReturnsFalse_NoThrow()
    {
        var history = new EditorHistory();
        var page    = new Page();

        var result = history.Undo(page);

        Assert.False(result);
        Assert.False(history.CanUndo);
    }
}
```

**Helper note:** `PageWithLayer` uses a `.Also()` extension that doesn't exist yet. Replace with an inline helper:

```csharp
static Page PageWithLayer(string layerName)
{
    var page = new Page();
    page.AddLayer(new SlideLayer { Name = layerName, ZOrder = 0 });
    return page;
}
```

---

## ShowFile Tests

`RemoveRundownFolder(id)` reassigns child rundowns to the deleted folder's `ParentId`, then removes the folder and all of its direct children.

```csharp
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class ShowFileTests
{
    [Fact]
    public void RemoveRundownFolder_ReparentsChildRundownsToParent()
    {
        var file   = new ShowFile();
        var parent = file.AddRundownFolder("Parent");
        var child  = file.AddRundownFolder("Child", parentId: parent.Id);
        var rd     = file.AddRundown("MyRundown", folderId: child.Id);

        file.RemoveRundownFolder(child.Id);

        Assert.Equal(parent.Id, rd.FolderId);
    }

    [Fact]
    public void RemoveRundownFolder_RootFolder_SetsRundownFolderIdToNull()
    {
        var file   = new ShowFile();
        var folder = file.AddRundownFolder("Root");
        var rd     = file.AddRundown("MyRundown", folderId: folder.Id);

        file.RemoveRundownFolder(folder.Id);

        Assert.Null(rd.FolderId);
    }

    [Fact]
    public void RemoveRundownFolder_AlsoRemovesChildFolders()
    {
        var file   = new ShowFile();
        var parent = file.AddRundownFolder("Parent");
        var child  = file.AddRundownFolder("Child", parentId: parent.Id);

        file.RemoveRundownFolder(parent.Id);

        Assert.DoesNotContain(file.RundownFolders, f => f.Id == parent.Id);
        Assert.DoesNotContain(file.RundownFolders, f => f.Id == child.Id);
    }

    [Fact]
    public void RemoveRundownFolder_NonExistentId_IsNoOp()
    {
        var file = new ShowFile();
        file.AddRundownFolder("Folder");

        var ex = Record.Exception(() => file.RemoveRundownFolder(Guid.NewGuid()));

        Assert.Null(ex);
        Assert.Single(file.RundownFolders);
    }
}
```

---

## Page Tests

```csharp
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class PageTests
{
    [Fact]
    public void LayersForRoles_ExcludesInvisibleLayers()
    {
        var page = new Page();
        page.AddLayer(new SlideLayer { Name = "Visible",   Visible = true,  Roles = LayerRole.Program, ZOrder = 0 });
        page.AddLayer(new SlideLayer { Name = "Invisible", Visible = false, Roles = LayerRole.Program, ZOrder = 1 });

        var result = page.LayersForRoles(LayerRole.Program).ToList();

        Assert.Single(result);
        Assert.Equal("Visible", result[0].Name);
    }

    [Fact]
    public void LayersForRoles_ExcludesLayersWithNonMatchingRole()
    {
        var page = new Page();
        page.AddLayer(new SlideLayer { Name = "Program", Visible = true, Roles = LayerRole.Program, ZOrder = 0 });
        page.AddLayer(new SlideLayer { Name = "Stage",   Visible = true, Roles = LayerRole.Stage,   ZOrder = 1 });

        var result = page.LayersForRoles(LayerRole.Stage).ToList();

        Assert.Single(result);
        Assert.Equal("Stage", result[0].Name);
    }

    [Fact]
    public void Clone_IsDeepCopy_MutatingCloneDoesNotAffectOriginal()
    {
        var page = new Page();
        page.AddLayer(new SlideLayer { Name = "Original", ZOrder = 0 });

        var clone = page.Clone();
        clone.Layers[0].Name = "Mutated";

        Assert.Equal("Original", page.Layers[0].Name);
    }

    [Fact]
    public void Clone_ProducesNewLayerIds()
    {
        var page = new Page();
        page.AddLayer(new SlideLayer { ZOrder = 0 });

        var clone = page.Clone();

        Assert.NotEqual(page.Layers[0].Id, clone.Layers[0].Id);
    }

    [Fact]
    public void Clone_CopiesTriggerTimerIds()
    {
        var timerId = Guid.NewGuid();
        var page    = new Page();
        page.TriggerTimerIds.Add(timerId);

        var clone = page.Clone();

        Assert.Contains(timerId, clone.TriggerTimerIds);
    }
}
```

---

## What This Is Not

- No rendering tests — `PageRenderer` and `TransitionCompositor` require `SKCanvas` and are excluded
- No ViewModel tests — ReactiveUI makes headless testing complex; not worth the setup cost
- No `OutputState` tests — depends on ReactiveUI binding infrastructure

---

## Files Changed

| File | Change |
|---|---|
| `ShowCast.Tests/Core/EditorHistoryTests.cs` | Create — 6 tests |
| `ShowCast.Tests/Core/ShowFileTests.cs` | Create — 4 tests |
| `ShowCast.Tests/Core/PageTests.cs` | Create — 5 tests |
