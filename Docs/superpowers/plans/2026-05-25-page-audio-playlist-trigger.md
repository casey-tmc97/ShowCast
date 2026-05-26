# Page Audio Playlist Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** When a page goes live, automatically switch the audio player to a configured playlist and start playback.

**Architecture:** Add `TriggerAudioPlaylistId` (Guid) to the `Page` model. Extend the right-click context menu under Trigger → Audio → Playlist to assign/clear it. In `MainViewModel`, add `FirePageAudioTrigger` called from both `GoLive` and `GoLiveFromGroup`.

**Tech Stack:** C# / .NET 9, Avalonia UI, ReactiveUI, xUnit

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `Core/Page.cs` | Modify | Add `TriggerAudioPlaylistId` property; copy it in `Clone()` |
| `ShowCast.Tests/Core/PageTests.cs` | Create | Unit tests for `Page.Clone` copying the new property |
| `ViewModels/MainViewModel.cs` | Modify | Add `FirePageAudioTrigger`; call it from `GoLive` and `GoLiveFromGroup` |
| `Views/PageGridPanel.axaml.cs` | Modify | Extend `ShowPageContextMenuAsync` with Audio → Playlist sub-menu |

---

## Task 1: Add TriggerAudioPlaylistId to Page model

**Files:**
- Modify: `Core/Page.cs`
- Create: `ShowCast.Tests/Core/PageTests.cs`

- [ ] **Step 1: Write the failing test**

Create `ShowCast.Tests/Core/PageTests.cs`:

```csharp
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class PageTests
{
    [Fact]
    public void Clone_CopiesTriggerAudioPlaylistId()
    {
        var id = Guid.NewGuid();
        var original = new Page { TriggerAudioPlaylistId = id };

        var clone = original.Clone();

        Assert.Equal(id, clone.TriggerAudioPlaylistId);
    }

    [Fact]
    public void TriggerAudioPlaylistId_DefaultsToEmpty()
    {
        var page = new Page();

        Assert.Equal(Guid.Empty, page.TriggerAudioPlaylistId);
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "FullyQualifiedName~PageTests"
```

Expected: FAIL — `Page` has no `TriggerAudioPlaylistId` property.

- [ ] **Step 3: Add property to Page and update Clone**

In `Core/Page.cs`, add after `TriggerTimerIds`:

```csharp
/// <summary>Audio playlist to switch to and play when this page goes live. Guid.Empty = none.</summary>
public Guid TriggerAudioPlaylistId { get; set; } = Guid.Empty;
```

In `Page.Clone()`, add after the `TriggerTimerIds` loop:

```csharp
copy.TriggerAudioPlaylistId = TriggerAudioPlaylistId;
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "FullyQualifiedName~PageTests"
```

Expected: PASS — both tests green.

- [ ] **Step 5: Commit**

```
git add Core/Page.cs ShowCast.Tests/Core/PageTests.cs
git commit -m "feat: add TriggerAudioPlaylistId to Page model"
```

---

## Task 2: Add FirePageAudioTrigger to MainViewModel

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Add FirePageAudioTrigger method**

In `ViewModels/MainViewModel.cs`, add this private method directly after `FirePageTriggerTimers`:

```csharp
void FirePageAudioTrigger(Page page)
{
    if (page.TriggerAudioPlaylistId == Guid.Empty) return;
    var playlist = AudioPlayer.Playlists
        .FirstOrDefault(p => p.Id == page.TriggerAudioPlaylistId);
    if (playlist is null) return;
    AudioPlayer.SelectedPlaylist = playlist;  // switches playlist; applies resume logic internally
    AudioPlayer.Play();                       // starts playback
}
```

- [ ] **Step 2: Call FirePageAudioTrigger from GoLive**

In `GoLive()`, the end currently looks like:

```csharp
StartPageTimer(SelectedPage.Model.DurationMs, SelectedPage.Model.LoopToStart);
FirePageTriggerTimers(SelectedPage.Model);
```

Add the call immediately after `FirePageTriggerTimers`:

```csharp
StartPageTimer(SelectedPage.Model.DurationMs, SelectedPage.Model.LoopToStart);
FirePageTriggerTimers(SelectedPage.Model);
FirePageAudioTrigger(SelectedPage.Model);
```

- [ ] **Step 3: Call FirePageAudioTrigger from GoLiveFromGroup**

In `GoLiveFromGroup()`, the end currently looks like:

```csharp
StartPageTimer(pvm.Model.DurationMs, pvm.Model.LoopToStart);
FirePageTriggerTimers(pvm.Model);
```

Add the call immediately after `FirePageTriggerTimers`:

```csharp
StartPageTimer(pvm.Model.DurationMs, pvm.Model.LoopToStart);
FirePageTriggerTimers(pvm.Model);
FirePageAudioTrigger(pvm.Model);
```

- [ ] **Step 4: Build to verify no compile errors**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 5: Run full test suite**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: fire audio playlist trigger on GoLive"
```

---

## Task 3: Extend right-click context menu with Audio → Playlist sub-menu

**Files:**
- Modify: `Views/PageGridPanel.axaml.cs`

- [ ] **Step 1: Build the Audio sub-menu in ShowPageContextMenuAsync**

In `PageGridPanel.axaml.cs`, locate `ShowPageContextMenuAsync`. Find the block that builds `triggerItem` (around line 486):

```csharp
var triggerItem = new MenuItem { Header = "Trigger" };
triggerItem.Items.Add(triggerTimerItem);
```

Replace it with:

```csharp
// Trigger → Audio → Playlist submenu
var triggerAudioPlaylistItem = new MenuItem { Header = "Playlist" };
var availablePlaylists = VM?.AudioPlayer.Playlists;
if (availablePlaylists is null || availablePlaylists.Count == 0)
{
    triggerAudioPlaylistItem.Items.Add(new MenuItem { Header = "(no playlists)", IsEnabled = false });
}
else
{
    foreach (var playlist in availablePlaylists)
    {
        bool isChecked = pvm.Model.TriggerAudioPlaylistId == playlist.Id;
        var pItem = new MenuItem
        {
            Header = (isChecked ? "✓ " : "   ") + playlist.Name
        };
        var playlistCopy = playlist;
        pItem.Click += (_, _) =>
        {
            pvm.Model.TriggerAudioPlaylistId =
                pvm.Model.TriggerAudioPlaylistId == playlistCopy.Id
                    ? Guid.Empty          // clicking checked item clears it
                    : playlistCopy.Id;
        };
        triggerAudioPlaylistItem.Items.Add(pItem);
    }
}

var triggerAudioItem = new MenuItem { Header = "Audio" };
triggerAudioItem.Items.Add(triggerAudioPlaylistItem);

var triggerItem = new MenuItem { Header = "Trigger" };
triggerItem.Items.Add(triggerTimerItem);
triggerItem.Items.Add(triggerAudioItem);
```

- [ ] **Step 2: Verify no new using directives needed**

`AudioPlaylist` is in `ShowCast.Core`, which is already imported in `PageGridPanel.axaml.cs`. No new `using` directives are required — the code above uses `var` for local inference and avoids referencing `ObservableCollection<T>` by name.

- [ ] **Step 3: Build to verify no compile errors**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Run full test suite**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 5: Commit**

```
git add Views/PageGridPanel.axaml.cs
git commit -m "feat: add Trigger > Audio > Playlist to page right-click menu"
```

---

## Task 4: Manual smoke test

- [ ] **Step 1: Run the app**

```
dotnet run
```

- [ ] **Step 2: Verify the menu structure**

1. Right-click any page thumbnail
2. Hover over **Trigger**
3. Verify **Timer** sub-menu still works as before
4. Verify **Audio** sub-menu appears with **Playlist** item
5. Under **Playlist**, verify the "Default" playlist is listed (seeded on new show)

- [ ] **Step 3: Assign a playlist trigger**

1. Click a playlist name in the Playlist sub-menu
2. Close the menu; right-click the same page again
3. Open Trigger → Audio → Playlist — verify a ✓ appears next to the chosen playlist

- [ ] **Step 4: Verify trigger fires on GoLive**

1. Create a second audio playlist in the Audio panel (e.g. "Service Music")
2. Switch the Audio panel to "Default" playlist so it's not on "Service Music"
3. Right-click the page → Trigger → Audio → Playlist → select "Service Music"
4. Click the page to go live
5. Verify the Audio panel switches to "Service Music" and playback begins (or shows "File not found" if no tracks are imported — either is correct; the playlist switch is the observable behaviour)

- [ ] **Step 5: Verify clear works**

1. Right-click same page → Trigger → Audio → Playlist → click ✓ "Service Music"
2. Right-click again → verify no ✓ next to any playlist
3. Go live — verify Audio panel does NOT switch playlists

- [ ] **Step 6: Verify Clone carries the trigger**

1. Assign a playlist trigger to a page
2. Right-click → Duplicate
3. Right-click the duplicated page → Trigger → Audio → Playlist — verify same ✓ is present

- [ ] **Step 7: Verify save/load round-trip**

1. Assign a playlist trigger to a page
2. File → Save
3. File → Open the same file
4. Right-click that page → Trigger → Audio → Playlist — verify ✓ is still there
