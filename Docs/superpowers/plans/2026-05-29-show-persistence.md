# Show Persistence (File Save / Open) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add File → Save Show and File → Open Show menu items that persist and restore all show data (Config/Settings, Libraries/Shows, Playlists/Rundowns, Media references) to/from a user-chosen `.scf` file path. The existing auto-save to `AppFolders.SessionFile` on window close is preserved.

**Architecture:** `SaveSessionAsync` and `LoadSessionAsync` already serialize the complete `ShowFile` (which includes Shows, Rundowns, Outputs, Timers, AudioPlaylists, Settings). We only need to add menu items and handlers that call those methods with a user-chosen path from Avalonia's `StorageProvider`.

**Tech Stack:** C#/.NET 9, Avalonia StorageProvider, existing ShowFileSerializer

---

### Task 1: Add File → Save Show menu item and handler

**Files:**
- Modify: `Views/MainWindow.axaml:45` (File menu)
- Modify: `Views/MainWindow.axaml.cs` (add `OnSaveShow` handler)

- [ ] **Step 1: Add the AXAML menu item**

In `Views/MainWindow.axaml`, replace the `_File` menu block:

```xml
<MenuItem Header="_File" Foreground="White">
    <MenuItem Header="_New Show" InputGesture="Ctrl+N" Click="OnNew"/>
    <Separator />
    <MenuItem Header="_Quit" Click="OnQuit"/>
</MenuItem>
```

With:

```xml
<MenuItem Header="_File" Foreground="White">
    <MenuItem Header="_New Show"  InputGesture="Ctrl+N"  Click="OnNew"/>
    <Separator />
    <MenuItem Header="_Open Show…" InputGesture="Ctrl+O" Click="OnOpenShow"/>
    <MenuItem Header="_Save Show"  InputGesture="Ctrl+S" Click="OnSaveShow"/>
    <MenuItem Header="Save Show _As…"                    Click="OnSaveShowAs"/>
    <Separator />
    <MenuItem Header="_Quit" Click="OnQuit"/>
</MenuItem>
```

- [ ] **Step 2: Add a `_currentShowPath` field and the Save handler to MainWindow.axaml.cs**

Add to `Views/MainWindow.axaml.cs`, inside the `MainWindow` class, below the existing fields:

```csharp
string? _currentShowPath;
```

Add the `OnSaveShow` handler (saves to `_currentShowPath` if known, otherwise calls SaveAs):

```csharp
async void OnSaveShow(object? sender, RoutedEventArgs e)
{
    if (VM is null) return;
    if (_currentShowPath is not null)
    {
        await VM.SaveSessionAsync(_currentShowPath);
    }
    else
    {
        await SaveShowAsAsync();
    }
}
```

Add the `OnSaveShowAs` handler:

```csharp
async void OnSaveShowAs(object? sender, RoutedEventArgs e)
{
    await SaveShowAsAsync();
}

async Task SaveShowAsAsync()
{
    if (VM is null) return;
    var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
    {
        Title              = "Save Show",
        DefaultExtension   = ShowCast.Core.ShowFileSerializer.Extension,
        SuggestedFileName  = "show",
        FileTypeChoices    = new[]
        {
            new FilePickerFileType("ShowCast File")
            {
                Patterns = new[] { $"*{ShowCast.Core.ShowFileSerializer.Extension}" }
            }
        }
    });
    if (file is null) return;

    _currentShowPath = file.Path.LocalPath;
    await VM.SaveSessionAsync(_currentShowPath);
}
```

- [ ] **Step 3: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 4: Manual test — Save Show**

1. Launch app, create a show with a few packages and pages
2. File → Save Show As… → choose a path (e.g. `Desktop/myshow.scf`)
3. Verify the file exists and is non-empty JSON
4. File → Save Show (Ctrl+S) → verify it saves to the same path without a dialog

- [ ] **Step 5: Commit**

```bash
git add Views/MainWindow.axaml Views/MainWindow.axaml.cs
git commit -m "feat(file): add File → Save Show and Save Show As menu items"
```

---

### Task 2: Add File → Open Show menu item and handler

**Files:**
- Modify: `Views/MainWindow.axaml.cs` (add `OnOpenShow` handler)

The AXAML change was already done in Task 1 Step 1.

- [ ] **Step 1: Write a serializer round-trip test to verify all data survives save/load**

```csharp
// ShowCast.Tests/Core/ShowFileSerializerTests.cs  (add to existing file)

[Fact]
public async Task SaveAsync_ThenLoadAsync_PreservesShowsRundownsAndTimers()
{
    var file = new ShowFile();
    var show = file.AddShow("TestShow");
    var pkg  = show.AddPackage("PkgA");
    pkg.AddPage(new Page { Name = "P1" });

    var rd = file.AddRundown("RD1");
    rd.AddEntry(new RundownEntry { PackageId = pkg.Id });

    file.Timers.Add(new TimerDef { Name = "MyTimer", StartSeconds = 60 });

    var path = Path.Combine(Path.GetTempPath(), $"showcast_test_{Guid.NewGuid()}.scf");
    try
    {
        await ShowFileSerializer.SaveAsync(file, path);
        var result = await ShowFileSerializer.LoadAsync(path);

        Assert.NotNull(result);
        Assert.Single(result!.File.Shows);
        Assert.Equal("TestShow", result.File.Shows[0].Name);
        Assert.Single(result.File.Shows[0].Packages);
        Assert.Single(result.File.Rundowns);
        Assert.Equal("RD1", result.File.Rundowns[0].Name);
        Assert.Single(result.File.Timers);
        Assert.Equal("MyTimer", result.File.Timers[0].Name);
    }
    finally
    {
        File.Delete(path);
    }
}
```

- [ ] **Step 2: Run test to verify it passes (confirms serializer handles all data)**

Run: `dotnet test ShowCast.Tests/ --filter "SaveAsync_ThenLoadAsync_PreservesShowsRundownsAndTimers" -v minimal`
Expected: PASS.

- [ ] **Step 3: Add the OnOpenShow handler**

In `Views/MainWindow.axaml.cs`, add:

```csharp
async void OnOpenShow(object? sender, RoutedEventArgs e)
{
    if (VM is null) return;

    var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
    {
        Title          = "Open Show",
        AllowMultiple  = false,
        FileTypeFilter = new[]
        {
            new FilePickerFileType("ShowCast File")
            {
                Patterns = new[] { $"*{ShowCast.Core.ShowFileSerializer.Extension}" }
            },
            new FilePickerFileType("All Files") { Patterns = new[] { "*.*" } }
        }
    });

    var file = files.FirstOrDefault();
    if (file is null) return;

    var path = file.Path.LocalPath;
    bool loaded = await VM.LoadSessionAsync(
        path,
        confirmMigration: () => AlertDialog.ShowConfirm(
            this,
            "Upgrade File Format",
            "This file was saved with an older version of ShowCast. Upgrade it to the current format?"),
        showError: msg => AlertDialog.ShowError(
            this,
            "Cannot Open File",
            msg));

    if (loaded)
    {
        _currentShowPath = path;
        RestoreWindowState(VM.ShowFile.Settings);
    }
}
```

- [ ] **Step 4: Add Ctrl+O / Ctrl+S keyboard shortcuts to MainWindow.OnKeyDown**

In `Views/MainWindow.axaml.cs`, in `OnKeyDown`, add before the existing `ctrl && e.Key == Key.Z` block:

```csharp
if (ctrl && e.Key == Key.S) { OnSaveShow(null, null!); e.Handled = true; return; }
if (ctrl && e.Key == Key.O) { OnOpenShow(null, null!); e.Handled = true; return; }
```

- [ ] **Step 5: Run build to verify no compile errors**

Run: `dotnet build ShowCast/ -c Debug --no-restore -v minimal`
Expected: Build succeeded with 0 errors.

- [ ] **Step 6: Manual test — Open Show**

1. Launch app, make changes (add a show, add packages, add pages)
2. File → Save Show As… → `test.scf`
3. File → New Show (resets to blank)
4. File → Open Show… → select `test.scf`
5. Verify all shows, packages, pages, rundowns, timers are restored

- [ ] **Step 7: Commit**

```bash
git add Views/MainWindow.axaml.cs ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat(file): add File → Open Show with path restoration and Ctrl+O/S shortcuts"
```

---

### Task 3: Persist current show path across auto-save / session restore

**Context:** On startup, the app auto-loads `AppFolders.SessionFile`. After loading that, `_currentShowPath` should remain `null` (the auto-save path is separate from user-chosen show paths). When the user opens a `.scf` file with "Open Show", `_currentShowPath` is set to that path. Ctrl+S then saves to that path, not to the session file.

This task verifies the interaction is correct; no code change required if Task 2 is implemented correctly.

- [ ] **Step 1: Manual test — Ctrl+S after Open Show**

1. Launch app (auto-load from session.scf)
2. Ctrl+O → open a user `.scf` file
3. Make a change (rename a package)
4. Ctrl+S → verify it saves to the user `.scf`, NOT session.scf (check file timestamps)
5. Restart app → verify session.scf reloads as before, user `.scf` has the change

- [ ] **Step 2: Manual test — Ctrl+S on fresh launch**

1. Launch app (no prior `_currentShowPath`)
2. Ctrl+S → SaveAs dialog should appear (because `_currentShowPath` is null)

- [ ] **Step 3: Commit any remaining fixes**

```bash
git commit -m "fix(file): Ctrl+S on fresh launch opens SaveAs dialog" --allow-empty
```
