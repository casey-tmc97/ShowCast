# Page Audio Playlist Trigger — Design Spec

**Date:** 2026-05-25  
**Branch:** Video-Player  
**Status:** Approved

---

## Overview

Add an **Audio → Playlist** sub-menu to the page right-click context menu under the existing **Trigger** item. When a page goes live, if it has an audio playlist trigger assigned, the audio player switches to that playlist and starts playback (respecting the playlist's own resume mode).

---

## 1. Data Model

**File:** `Core/Page.cs`

Add one property to `Page`:

```csharp
/// <summary>Audio playlist to switch to and play when this page goes live. Guid.Empty = none.</summary>
public Guid TriggerAudioPlaylistId { get; set; } = Guid.Empty;
```

Update `Page.Clone()` to copy it:

```csharp
copy.TriggerAudioPlaylistId = TriggerAudioPlaylistId;
```

**Serialization:** No changes needed — `ShowFileSerializer` uses STJ with `JsonObjectCreationHandling.Populate`, which picks up new properties automatically. Old files that lack the field deserialize to `Guid.Empty` (no trigger), which is correct.

---

## 2. Context Menu

**File:** `Views/PageGridPanel.axaml.cs` — `ShowPageContextMenuAsync`

Extend the existing `triggerItem` to include an **Audio** sub-menu with a **Playlist** sub-sub-menu:

```
Trigger
  ├── Timer          (existing)
  └── Audio
        └── Playlist
              ├── ✓ Morning Worship   ← checked = currently assigned; click to clear
              ├──    Offertory
              └──    (no playlists)   ← disabled placeholder if none exist
```

**Behaviour:**
- Playlist list sourced from `VM.AudioPlayer.Playlists`
- Checked item = `pvm.Model.TriggerAudioPlaylistId == playlist.Id`
- Clicking a checked item sets `TriggerAudioPlaylistId = Guid.Empty` (clears trigger)
- Clicking an unchecked item sets `TriggerAudioPlaylistId = playlist.Id`
- Both the flat grid view and grouped rundown view share this menu via the existing `ShowPageContextMenuAsync` method — no additional wiring needed

---

## 3. Trigger Execution

**File:** `ViewModels/MainViewModel.cs`

Add a new private method:

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

Call it in both `GoLive()` and `GoLiveFromGroup()` immediately after `FirePageTriggerTimers(...)`:

```csharp
FirePageTriggerTimers(page);
FirePageAudioTrigger(page);   // new
```

**Notes:**
- `AudioPlayer.SelectedPlaylist = playlist` already calls `ApplyResume()` before `Play()`, so the playlist's own resume mode (FromTop / FromLastPosition) is honoured.
- If the assigned playlist has been deleted from the show file, `FirstOrDefault` returns null and the method is a no-op.

---

## 4. Out of Scope

- No badge/indicator on the page thumbnail for audio trigger (mirrors timer badge approach — timer gets a badge, audio trigger does not, keeping the thumbnail clean)
- No per-page volume or fade controls
- No "stop audio" trigger option

---

## Files Changed

| File | Change |
|------|--------|
| `Core/Page.cs` | Add `TriggerAudioPlaylistId` property + clone it |
| `Views/PageGridPanel.axaml.cs` | Extend `ShowPageContextMenuAsync` with Audio sub-menu |
| `ViewModels/MainViewModel.cs` | Add `FirePageAudioTrigger`, call from `GoLive` and `GoLiveFromGroup` |
