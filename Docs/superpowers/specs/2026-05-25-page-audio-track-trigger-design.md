# Page Audio Track Trigger — Design Spec

**Date:** 2026-05-25
**Branch:** Video-Player
**Status:** Approved
**Depends on:** `2026-05-25-page-audio-playlist-trigger-design.md` (already shipped)

---

## Overview

Extend the existing audio playlist trigger so that a specific track within the playlist can also be designated. When a page goes live and a track is configured, the audio player switches to the playlist and jumps directly to that track — bypassing the playlist's resume mode.

---

## 1. Data Model

**File:** `Core/Page.cs`

Add one property alongside the existing `TriggerAudioPlaylistId`:

```csharp
/// <summary>Specific track to play when this page goes live. Guid.Empty = use playlist resume mode.</summary>
public Guid TriggerAudioTrackId { get; set; } = Guid.Empty;
```

Update `Page.Clone()` to copy it (after `TriggerAudioPlaylistId`):

```csharp
copy.TriggerAudioTrackId = TriggerAudioTrackId;
```

**Serialization:** Automatic via STJ. Old files missing this field deserialize to `Guid.Empty` (no specific track), which is correct.

---

## 2. Context Menu

**File:** `Views/PageGridPanel.axaml.cs` — `ShowPageContextMenuAsync`

The playlist items in the existing Trigger → Audio → Playlist submenu become parent nodes for a track submenu. Track selection is always done via the submenu (avoids Avalonia's behavior of opening submenus rather than firing Click on parent items with children).

### Menu structure

```
Trigger
  └── Audio
        └── Playlist
              ├── ✓ Morning Worship  ▶
              │     ├── ✓ (any track)      ← Guid.Empty track; click = assign playlist, clear track
              │     ├──    Amazing Grace   ← click = assign playlist + this track
              │     └──    How Great Thou Art
              └── Offertory         ▶
                    ├──    (any track)
                    └──    In Christ Alone
```

### Checkmark logic

| Item | Shows ✓ when |
|------|-------------|
| Playlist item | `TriggerAudioPlaylistId == playlist.Id` |
| `(any track)` | Playlist is assigned AND `TriggerAudioTrackId == Guid.Empty` |
| Track item | Playlist is assigned AND `TriggerAudioTrackId == track.Id` |

### Click actions

| Clicked item | Result |
|---|---|
| `(any track)` (unchecked) | `TriggerAudioPlaylistId = playlist.Id`, `TriggerAudioTrackId = Guid.Empty` |
| Specific track (unchecked) | `TriggerAudioPlaylistId = playlist.Id`, `TriggerAudioTrackId = track.Id` |
| `(any track)` (checked) | `TriggerAudioPlaylistId = Guid.Empty`, `TriggerAudioTrackId = Guid.Empty` (clears all) |
| Specific track (checked) | `TriggerAudioPlaylistId = Guid.Empty`, `TriggerAudioTrackId = Guid.Empty` (clears all) |

**Empty playlist:** If a playlist has no tracks, show only `(any track)` in the submenu.

---

## 3. Trigger Execution

**File:** `ViewModels/MainViewModel.cs`

Replace the existing `FirePageAudioTrigger` method:

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

**Edge cases:**
- Track deleted from playlist → `FirstOrDefault` returns null → falls through to normal `Play()`
- Playlist deleted → existing null-guard on `playlist is null` handles it, no change
- `TriggerAudioTrackId` set but `TriggerAudioPlaylistId` empty → unreachable (early return on empty playlist ID)

---

## 4. Out of Scope

- No indicator on the page thumbnail for track trigger
- No "stop audio" action
- No per-page volume or fade

---

## Files Changed

| File | Change |
|------|--------|
| `Core/Page.cs` | Add `TriggerAudioTrackId` property + clone it |
| `Views/PageGridPanel.axaml.cs` | Replace playlist menu items with nested track submenus |
| `ViewModels/MainViewModel.cs` | Update `FirePageAudioTrigger` to handle specific track |
