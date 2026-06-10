# Check for Updates — Design Spec
_Date: 2026-06-10_

## Overview

Add automatic and manual update checking to ShowCast. On startup the app silently queries the GitHub releases API; if a newer version exists and the user hasn't suppressed it, a prompt is shown. The Help menu exposes a manual trigger at any time. Accepting an update downloads the Inno Setup installer in the background and then offers to launch it.

---

## Architecture

### New files

| File | Purpose |
|------|---------|
| `Core/UpdateChecker.cs` | GitHub API query; background download with progress reporting |
| `Core/UpdatePreferences.cs` | Persists skip/remind state to JSON |
| `Views/UpdateAvailableDialog.cs` | Code-first dialog — three-button prompt |
| `Views/UpdateDownloadDialog.cs` | Code-first dialog — progress bar + post-download actions |

### Modified files

| File | Change |
|------|--------|
| `ShowCast.csproj` | Add `<Version>1.1.1</Version>` |
| `Views/MainWindow.axaml` | Add "Check for Updates…" to Help menu |
| `Views/MainWindow.axaml.cs` | Startup check + menu handler |
| `Core/AppFolders.cs` | Add `UpdatePrefsFile` path property |

---

## Components

### `UpdateChecker`

Static class with two public methods:

```
Task<UpdateInfo?> CheckAsync()
```
- GETs `https://api.github.com/repos/casey-tmc97/ShowCast/releases/latest`
- Required header: `User-Agent: ShowCast`
- Parses `tag_name` (strip leading `v`) and finds the asset whose name matches `ShowCast-*-win-x64-setup.exe`
- Returns `null` if the request fails or the asset is not found
- Returns `null` if the latest version ≤ current assembly version

```
Task DownloadAsync(UpdateInfo info, IProgress<double> progress, CancellationToken ct)
```
- Streams asset to `Path.Combine(Path.GetTempPath(), info.AssetName)`
- Reports 0.0–1.0 progress via `IProgress<double>`
- Exposes `InstallerPath` on `UpdateInfo` after completion

```csharp
record UpdateInfo(string Version, string AssetName, string DownloadUrl)
{
    public string InstallerPath => Path.Combine(Path.GetTempPath(), AssetName);
}
```

**Version comparison:**
```csharp
var current = Assembly.GetExecutingAssembly().GetName().Version!;
var latest  = Version.Parse(tagName.TrimStart('v'));
if (latest <= current) return null;
```

---

### `UpdatePreferences`

```csharp
class UpdatePreferences
{
    public string?   SkippedVersion { get; set; }
    public DateTime? RemindAfter    { get; set; }

    public static UpdatePreferences Load();
    public void Save();
    public bool ShouldShow(string latestVersion);
}
```

- Serialized as JSON to `AppFolders.UpdatePrefsFile` (`Configuration/update_prefs.json`)
- `ShouldShow` returns `false` if `SkippedVersion == latestVersion` or `RemindAfter > DateTime.UtcNow`

---

### `UpdateAvailableDialog`

Code-first, matches `AlertDialog` styling (dark `#2d2d2d` background, white text, rounded buttons).

```
┌─────────────────────────────────────────────┐
│  ShowCast 1.1.2 is available                │
│                                             │
│  You have version 1.1.1. Would you like to  │
│  download and install the update?           │
│                                             │
│  [Skip This Version]  [Remind Me Later]  [OK] │
└─────────────────────────────────────────────┘
```

- **OK** → opens `UpdateDownloadDialog`, starts download
- **Remind Me Later** → sets `RemindAfter = DateTime.UtcNow.AddDays(3)`, saves prefs, closes
- **Skip This Version** → sets `SkippedVersion = latestVersion`, saves prefs, closes

---

### `UpdateDownloadDialog`

Code-first. Shows a `ProgressBar` (0–100%) and status text while downloading.

**Downloading state:**
```
┌────────────────────────────────┐
│  Downloading ShowCast 1.1.2…   │
│  ████████░░░░░░░░  52%         │
│                       [Cancel] │
└────────────────────────────────┘
```

**Complete state** (ProgressBar replaced by success message):
```
┌───────────────────────────────────────────┐
│  Download complete.                        │
│                                           │
│        [Close App]  [Install & Restart]   │
└───────────────────────────────────────────┘
```

**Error state:**
```
┌──────────────────────────────────────────┐
│  Download failed: <error message>        │
│                                          │
│                     [Close]  [Try Again] │
└──────────────────────────────────────────┘
```

- **Install & Restart** → invokes a `Func<Task> beforeClose` callback (provided by `MainWindow` when opening the dialog) that saves the current session, then launches installer via `Process.Start(new ProcessStartInfo(path) { UseShellExecute = true })`, then closes MainWindow
- **Close App** → closes MainWindow without launching installer (installer `.exe` remains in `%TEMP%`)
- **Cancel** → cancels the `CancellationToken`, deletes partial download, closes dialog

---

## Startup Integration (`MainWindow.OnOpened`)

After the existing session-load block, add a fire-and-forget background check:

```csharp
_ = CheckForUpdatesAsync(silent: true);
```

`CheckForUpdatesAsync(bool silent)`:
1. Call `UpdateChecker.CheckAsync()` — network errors are caught and ignored when `silent == true`
2. Load `UpdatePreferences`, call `ShouldShow(info.Version)` — if false, return
3. Dispatch to UI thread, show `UpdateAvailableDialog`
4. When `silent == false` (manual trigger) and no update found, show "You're up to date" via `AlertDialog.ShowError` (reused as an info dialog with title "Check for Updates")

---

## Help Menu Integration

```xml
<MenuItem Header="_Help" Foreground="White">
    <MenuItem Header="User Manual"       InputGesture="F1"  Click="OnManual"/>
    <Separator />
    <MenuItem Header="Check for Updates…"                   Click="OnCheckForUpdates"/>
</MenuItem>
```

Handler calls `CheckForUpdatesAsync(silent: false)`.

---

## Preferences File

Path: `%DOCUMENTS%\ShowCast\Configuration\update_prefs.json`

```json
{
  "SkippedVersion": null,
  "RemindAfter": null
}
```

File is created on first write. Missing file = no preferences (show updates normally).

---

## Error Handling

| Scenario | Behavior |
|----------|---------|
| No internet / GitHub API down (auto-check) | Silently swallowed, no dialog |
| No internet / GitHub API down (manual check) | "Could not check for updates. Check your internet connection." |
| No matching asset in release | Treated as no update available |
| Download failure | Error state in `UpdateDownloadDialog` with Try Again option |
| Installer not found after download | Error dialog before launching |

---

## Out of Scope

- Delta / incremental updates
- Rollback
- Linux / macOS builds
- Silent (no-UI) installs
