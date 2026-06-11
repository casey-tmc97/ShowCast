# Editor Top Bar Page Rename Design

**Date:** 2026-06-11
**Status:** Approved

## Summary

Double-clicking the page name label in the editor top bar (Column 3, between Redo and the Add-Text button) opens an inline TextBox for renaming the current page. Enter commits; Escape or click-away cancels.

## Scope

- Double-click on the page name TextBlock → show inline TextBox, pre-filled and selected
- Enter → commit (call `VM.RenamePage`, restore TextBlock)
- Escape → cancel (restore TextBlock, discard edits)
- Click away (LostFocus) → cancel (restore TextBlock, discard edits)
- Empty name → treat as cancel (do not commit a blank name)
- No change to the filmstrip context-menu rename path

## Out of Scope

- Keyboard shortcut to open rename (F2, etc.)
- Rename from anywhere other than the top bar label
- Validation or max-length enforcement beyond empty-string guard

## File Map

| File | Change |
|------|--------|
| `Views/PageEditorOverlay.axaml` | Replace Column 3 TextBlock with a Panel containing TextBlock + TextBox |
| `Views/PageEditorOverlay.axaml.cs` | Add `_isRenaming` field; wire DoubleTapped, KeyDown, LostFocus handlers |

## Design

### AXAML (`Views/PageEditorOverlay.axaml`)

Replace the existing Column 3 TextBlock:

```xml
<!-- Page name -->
<TextBlock Grid.Column="3"
           Text="{Binding EditingPageName, FallbackValue='Page Editor'}"
           Foreground="#cccccc" FontSize="13" FontWeight="Bold"
           VerticalAlignment="Center"
           HorizontalAlignment="Center"/>
```

With a Panel that overlays both controls in the same column slot:

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

`PageNameText` is visible by default; `PageNameBox` starts hidden and swaps in on double-click.

### Code-behind (`Views/PageEditorOverlay.axaml.cs`)

Add one field:

```csharp
bool _isRenaming;
```

Subscribe in the constructor (after `InitializeComponent()`):

```csharp
PageNameText.DoubleTapped += OnPageNameDoubleTapped;
PageNameBox.KeyDown       += OnPageNameBoxKeyDown;
PageNameBox.LostFocus     += OnPageNameBoxLostFocus;
```

Handlers:

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

### Key Interactions

**_isRenaming guard:** `CommitRename`/`CancelRename` set `_isRenaming = false` before returning control. When Enter or Escape fires, focus moves away and triggers `LostFocus`; by that time `_isRenaming` is already false, so `OnPageNameBoxLostFocus` is a no-op — no double-cancel.

**Escape conflict with global OnKeyDown:** The existing `OnKeyDown` handler checks `if (e.Handled) return` at its top. `OnPageNameBoxKeyDown` sets `e.Handled = true` on Escape, so the global handler never sees it and `CloseEditor()` is not called while renaming.

**TextBox focus guard:** The existing TextBox ancestor check in `OnKeyDown` (lines 135–138) causes the global handler to bail out when `PageNameBox` has focus — so Delete/Backspace/arrow keys won't accidentally affect layers while the rename box is open.

**TappedEventArgs:** `DoubleTapped` in Avalonia 11 is `EventHandler<TappedEventArgs>`; `TappedEventArgs` is in `Avalonia.Input`, already imported in the file.
