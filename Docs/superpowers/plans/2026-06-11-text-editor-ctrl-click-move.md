# Text Editor Ctrl+Click Move Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** While the inline text editor is active, Ctrl+clicking inside the text layer commits the edit and begins a move drag on that layer.

**Architecture:** Single `if` branch added inside the `if (insideLayer)` block of `OnPointerPressed` in `EditorCanvas.cs`. When Ctrl is held, call `EndCustomEdit()` (commits the text edit and clears `_textEditor`), then call `StartDrag(HandleKind.Move, edPt)` to initiate a normal layer move — exactly the same path as clicking to move a layer in non-editing mode. This is a one-file change with no new methods.

**Tech Stack:** C# / .NET 9, Avalonia 11.2.2

---

## File Map

| File | Change |
|------|--------|
| `Views/EditorCanvas.cs` | Add Ctrl branch in `OnPointerPressed` inside `if (insideLayer)` block |

---

### Task 1: Add Ctrl+click move branch to `OnPointerPressed`

**Files:**
- Modify: `Views/EditorCanvas.cs:700-705`

This is a view-level interaction change with no pure-logic surface to unit test. Test strategy: build clean + manual verification.

- [ ] **Step 1: Locate the insertion point**

Open `Views/EditorCanvas.cs` and find `OnPointerPressed` (line 689). The existing `if (insideLayer)` block looks like this (lines 700–706):

```csharp
if (insideLayer)
{
    // Keep _imeBox focused — do NOT call this.Focus() here
    _textEditor.OnPointerPressed(edPt);
    e.Handled = true;
    return;
}
```

- [ ] **Step 2: Replace the `if (insideLayer)` block**

Replace the entire `if (insideLayer)` block (lines 700–706) with:

```csharp
if (insideLayer)
{
    if (e.KeyModifiers.HasFlag(KeyModifiers.Control))
    {
        EndCustomEdit();
        this.Focus();
        StartDrag(HandleKind.Move, edPt);
        return;
    }
    // Keep _imeBox focused — do NOT call this.Focus() here
    _textEditor.OnPointerPressed(edPt);
    e.Handled = true;
    return;
}
```

`EndCustomEdit()` commits the current text and sets `_textEditor = null`. `StartDrag(HandleKind.Move, edPt)` then runs as if the user clicked the layer normally (same code path as line 751 in the normal-mode branch). `KeyModifiers` is in `Avalonia.Input`, already imported.

- [ ] **Step 3: Build to confirm 0 errors**

```
dotnet build ShowCast.Tests
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 4: Run full test suite**

```
dotnet test ShowCast.Tests -v quiet
```

Expected: all tests pass (0 failing).

- [ ] **Step 5: Commit**

```
git add Views/EditorCanvas.cs
git commit -m "feat: Ctrl+click text layer commits edit and starts move drag"
```
