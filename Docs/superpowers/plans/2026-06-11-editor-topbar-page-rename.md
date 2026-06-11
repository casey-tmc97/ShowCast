# Editor Top Bar Page Rename Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Double-clicking the page name in the editor top bar opens an inline TextBox for renaming; Enter commits, Escape/click-away cancels.

**Architecture:** Replace the existing read-only `TextBlock` in Column 3 of the editor toolbar with a `Panel` containing both the TextBlock and a hidden `TextBox`. View code-behind handles all state via a `_isRenaming` bool guard — no ViewModel changes needed beyond calling the existing `RenamePage(PageViewModel, string)` method.

**Tech Stack:** C# / .NET 9, Avalonia 11.2.2

---

## File Map

| File | Change |
|------|--------|
| `Views/PageEditorOverlay.axaml` | Replace Column 3 TextBlock with Panel + TextBlock + TextBox |
| `Views/PageEditorOverlay.axaml.cs` | Add `_isRenaming` field; wire DoubleTapped, KeyDown, LostFocus |

---

### Task 1: Add inline rename to editor top bar

**Files:**
- Modify: `Views/PageEditorOverlay.axaml:35-39`
- Modify: `Views/PageEditorOverlay.axaml.cs`

This is view interaction code — no unit test surface. Test strategy: build clean + manual smoke check.

- [ ] **Step 1: Replace the Column 3 TextBlock in the AXAML**

Open `Views/PageEditorOverlay.axaml`. Lines 34–39 currently read:

```xml
<!-- Page name -->
<TextBlock Grid.Column="3"
           Text="{Binding EditingPageName, FallbackValue='Page Editor'}"
           Foreground="#cccccc" FontSize="13" FontWeight="Bold"
           VerticalAlignment="Center"
           HorizontalAlignment="Center"/>
```

Replace with:

```xml
<!-- Page name (double-click to rename) -->
<Panel Grid.Column="3" HorizontalAlignment="Center" VerticalAlignment="Center">
    <TextBlock Name="PageNameText"
               Text="{Binding EditingPageName, FallbackValue='Page Editor'}"
               Foreground="#cccccc" FontSize="13" FontWeight="Bold"
               VerticalAlignment="Center" HorizontalAlignment="Center"/>
    <TextBox Name="PageNameBox"
             IsVisible="False"
             FontSize="13" FontWeight="Bold"
             MinWidth="120"
             VerticalAlignment="Center" HorizontalAlignment="Center"/>
</Panel>
```

- [ ] **Step 2: Add `_isRenaming` field and event subscriptions to the code-behind**

Open `Views/PageEditorOverlay.axaml.cs`. Add the field after the existing `Key? _nudgeKey;` line (line 21):

```csharp
bool _isRenaming;
```

Add three event subscriptions to the constructor, after `TheInspector.SetCanvas(TheCanvas);`:

```csharp
PageNameText.DoubleTapped += OnPageNameDoubleTapped;
PageNameBox.KeyDown       += OnPageNameBoxKeyDown;
PageNameBox.LostFocus     += OnPageNameBoxLostFocus;
```

- [ ] **Step 3: Add the rename handlers to the code-behind**

Add the following four methods anywhere after the existing one-liner handlers (e.g., after `OnPreviewAnimation` at line 59):

```csharp
void OnPageNameDoubleTapped(object? sender, TappedEventArgs e)
{
    if (VM?.SelectedEditorPage is null) return;
    _isRenaming = true;
    PageNameBox.Text = VM.EditingPageName;
    PageNameText.IsVisible = false;
    PageNameBox.IsVisible  = true;
    PageNameBox.Focus();
    PageNameBox.SelectAll();
    e.Handled = true;
}

void OnPageNameBoxKeyDown(object? sender, KeyEventArgs e)
{
    if (e.Key == Key.Return) { CommitRename(); e.Handled = true; }
    if (e.Key == Key.Escape) { CancelRename(); e.Handled = true; }
}

void OnPageNameBoxLostFocus(object? sender, RoutedEventArgs e)
{
    if (_isRenaming) CancelRename();
}

void CommitRename()
{
    _isRenaming = false;
    var name = PageNameBox.Text?.Trim() ?? string.Empty;
    if (name.Length > 0 && VM?.SelectedEditorPage is { } pvm)
        VM.RenamePage(pvm, name);
    PageNameBox.IsVisible  = false;
    PageNameText.IsVisible = true;
}

void CancelRename()
{
    _isRenaming = false;
    PageNameBox.IsVisible  = false;
    PageNameText.IsVisible = true;
}
```

`TappedEventArgs` is in `Avalonia.Input`, already imported. `KeyEventArgs`, `Key`, and `RoutedEventArgs` are all already imported. `VM.RenamePage(PageViewModel, string)` exists in `MainViewModel`.

- [ ] **Step 4: Build to confirm 0 errors**

```
dotnet build ShowCast.Tests
```

Expected: `Build succeeded. 0 Error(s).`

- [ ] **Step 5: Run full test suite**

```
dotnet test ShowCast.Tests -v quiet
```

Expected: 209 passed, 0 failed.

- [ ] **Step 6: Commit**

```
git add Views/PageEditorOverlay.axaml Views/PageEditorOverlay.axaml.cs
git commit -m "feat: double-click page name in editor top bar to rename"
```
