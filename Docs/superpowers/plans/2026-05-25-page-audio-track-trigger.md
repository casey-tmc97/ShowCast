# Page Audio Track Trigger Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Allow a specific audio track within a playlist to be configured as a page trigger, so that going live jumps directly to that track and starts playing.

**Architecture:** Add `TriggerAudioTrackId` (Guid) to the `Page` model alongside the existing `TriggerAudioPlaylistId`. Update `FirePageAudioTrigger` in `MainViewModel` to call `AudioPlayer.Play(track)` when a specific track is set. Replace the flat playlist menu items with nested track submenus in `PageGridPanel.axaml.cs`.

**Tech Stack:** C# / .NET 9, Avalonia UI, ReactiveUI, xUnit

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `Core/Page.cs` | Modify | Add `TriggerAudioTrackId` property; copy it in `Clone()` |
| `ShowCast.Tests/Core/PageTests.cs` | Modify | Add tests for the new property |
| `ShowCast.Tests/Core/ShowFileSerializerTests.cs` | Modify | Add round-trip serialization test |
| `ViewModels/MainViewModel.cs` | Modify | Update `FirePageAudioTrigger` to handle specific track |
| `Views/PageGridPanel.axaml.cs` | Modify | Replace flat playlist items with nested track submenus |

---

## Task 1: Add TriggerAudioTrackId to Page model

**Files:**
- Modify: `Core/Page.cs`
- Modify: `ShowCast.Tests/Core/PageTests.cs`
- Modify: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Step 1: Write the failing tests**

In `ShowCast.Tests/Core/PageTests.cs`, add these two tests inside the `PageTests` class (after the existing tests):

```csharp
[Fact]
public void Clone_CopiesTriggerAudioTrackId()
{
    var id = Guid.NewGuid();
    var original = new Page { TriggerAudioTrackId = id };

    var clone = original.Clone();

    Assert.Equal(id, clone.TriggerAudioTrackId);
}

[Fact]
public void TriggerAudioTrackId_DefaultsToEmpty()
{
    var page = new Page();

    Assert.Equal(Guid.Empty, page.TriggerAudioTrackId);
}
```

In `ShowCast.Tests/Core/ShowFileSerializerTests.cs`, add this test at the end of the class (before the closing `}`):

```csharp
[Fact]
public void Page_TriggerAudioTrackId_SurvivesRoundTrip()
{
    var trackId = Guid.NewGuid();
    var file = new ShowFile();
    var show = file.AddShow("Test");
    var package = show.AddPackage("Pkg");
    var page = new Page { TriggerAudioTrackId = trackId };
    package.AddPage(page);

    var options = ShowFileSerializer.CreateSerializerOptions();
    var json = JsonSerializer.Serialize(file, options);
    var loaded = JsonSerializer.Deserialize<ShowFile>(json, options);

    var loadedPage = loaded!.Shows[0].Packages[0].Pages[0];
    Assert.Equal(trackId, loadedPage.TriggerAudioTrackId);
}
```

- [ ] **Step 2: Run tests to verify they fail**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "FullyQualifiedName~TriggerAudioTrackId"
```

Expected: FAIL — `Page` has no `TriggerAudioTrackId` property.

- [ ] **Step 3: Add property to Page and update Clone**

In `Core/Page.cs`, add after `TriggerAudioPlaylistId`:

```csharp
/// <summary>Specific track to play when this page goes live. Guid.Empty = use playlist resume mode.</summary>
public Guid TriggerAudioTrackId { get; set; } = Guid.Empty;
```

In `Page.Clone()`, add after `copy.TriggerAudioPlaylistId = TriggerAudioPlaylistId;`:

```csharp
copy.TriggerAudioTrackId = TriggerAudioTrackId;
```

- [ ] **Step 4: Run tests to verify they pass**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "FullyQualifiedName~TriggerAudioTrackId"
```

Expected: PASS — all three tests green.

- [ ] **Step 5: Run full suite to confirm no regressions**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 6: Commit**

```
git add Core/Page.cs ShowCast.Tests/Core/PageTests.cs ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat: add TriggerAudioTrackId to Page model"
```

---

## Task 2: Update FirePageAudioTrigger in MainViewModel

**Files:**
- Modify: `ViewModels/MainViewModel.cs`

- [ ] **Step 1: Replace FirePageAudioTrigger**

In `ViewModels/MainViewModel.cs`, find the existing `FirePageAudioTrigger` method (it currently looks like this):

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

Replace it entirely with:

```csharp
void FirePageAudioTrigger(Page page)
{
    if (page.TriggerAudioPlaylistId == Guid.Empty) return;
    var playlist = AudioPlayer.Playlists
        .FirstOrDefault(p => p.Id == page.TriggerAudioPlaylistId);
    if (playlist is null) return;

    AudioPlayer.SelectedPlaylist = playlist;  // switches playlist; applies resume logic

    if (page.TriggerAudioTrackId != Guid.Empty)
    {
        var track = playlist.Tracks.FirstOrDefault(t => t.Id == page.TriggerAudioTrackId);
        if (track is not null)
        {
            AudioPlayer.Play(track);  // jumps directly to this track, ignores resume mode
            return;
        }
        // track not found (deleted) — fall through to normal Play()
    }

    AudioPlayer.Play();  // no specific track: use playlist's resume mode
}
```

- [ ] **Step 2: Build to verify no compile errors**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run full test suite**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: fire specific track trigger on GoLive"
```

---

## Task 3: Replace playlist menu items with nested track submenus

**Files:**
- Modify: `Views/PageGridPanel.axaml.cs`

- [ ] **Step 1: Replace the playlist item construction block**

In `Views/PageGridPanel.axaml.cs`, inside `ShowPageContextMenuAsync`, find the block that populates `triggerAudioPlaylistItem` (the `else` branch that iterates `availablePlaylists`). It currently looks like this:

```csharp
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
                    ? Guid.Empty
                    : playlistCopy.Id;
        };
        triggerAudioPlaylistItem.Items.Add(pItem);
    }
}
```

Replace it with:

```csharp
else
{
    foreach (var playlist in availablePlaylists)
    {
        bool playlistChecked = pvm.Model.TriggerAudioPlaylistId == playlist.Id;
        var pItem = new MenuItem
        {
            Header = (playlistChecked ? "✓ " : "   ") + playlist.Name
        };
        var playlistCopy = playlist;

        // "(any track)" — assigns playlist only, clears specific track
        bool anyTrackChecked = playlistChecked && pvm.Model.TriggerAudioTrackId == Guid.Empty;
        var anyTrackItem = new MenuItem
        {
            Header = (anyTrackChecked ? "✓ " : "   ") + "(any track)"
        };
        anyTrackItem.Click += (_, _) =>
        {
            if (pvm.Model.TriggerAudioPlaylistId == playlistCopy.Id &&
                pvm.Model.TriggerAudioTrackId == Guid.Empty)
            {
                // Clicking checked item clears all
                pvm.Model.TriggerAudioPlaylistId = Guid.Empty;
                pvm.Model.TriggerAudioTrackId    = Guid.Empty;
            }
            else
            {
                pvm.Model.TriggerAudioPlaylistId = playlistCopy.Id;
                pvm.Model.TriggerAudioTrackId    = Guid.Empty;
            }
        };
        pItem.Items.Add(anyTrackItem);

        // Individual tracks
        foreach (var track in playlistCopy.Tracks)
        {
            bool trackChecked = playlistChecked && pvm.Model.TriggerAudioTrackId == track.Id;
            var trackCopy = track;
            var tItem = new MenuItem
            {
                Header = (trackChecked ? "✓ " : "   ") + trackCopy.Title
            };
            tItem.Click += (_, _) =>
            {
                if (pvm.Model.TriggerAudioPlaylistId == playlistCopy.Id &&
                    pvm.Model.TriggerAudioTrackId == trackCopy.Id)
                {
                    // Clicking checked item clears all
                    pvm.Model.TriggerAudioPlaylistId = Guid.Empty;
                    pvm.Model.TriggerAudioTrackId    = Guid.Empty;
                }
                else
                {
                    pvm.Model.TriggerAudioPlaylistId = playlistCopy.Id;
                    pvm.Model.TriggerAudioTrackId    = trackCopy.Id;
                }
            };
            pItem.Items.Add(tItem);
        }

        triggerAudioPlaylistItem.Items.Add(pItem);
    }
}
```

- [ ] **Step 2: Build to verify no compile errors**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 3: Run full test suite**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj
```

Expected: All tests pass.

- [ ] **Step 4: Commit**

```
git add Views/PageGridPanel.axaml.cs
git commit -m "feat: add per-track selection to Trigger > Audio > Playlist menu"
```
