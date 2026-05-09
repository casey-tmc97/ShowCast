# File Format Versioning Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a `Version` field to ShowCast's `.scf` JSON format so the serializer can detect and migrate old files and refuse files from future app versions.

**Architecture:** `ShowFile` gains a `Version` int and a `[JsonExtensionData]` bag. `ShowFileSerializer.LoadAsync` returns a `LoadResult` record that includes a `NeedsMigration` flag instead of a raw `ShowFile?`. A new `ApplyMigration` method runs a typed migration chain. `MainWindow` passes dialog callbacks into `LoadSessionAsync` so the UI prompt lives in the View layer.

**Tech Stack:** C# 13 / .NET 9, System.Text.Json, xUnit 2.9.2, Avalonia 11.2.2 (dialog only)

---

## File Map

| Action | Path | Responsibility |
|---|---|---|
| Create | `ShowCast.Tests/ShowCast.Tests.csproj` | xUnit test project referencing main project |
| Create | `ShowCast.Tests/Core/ShowFileSerializerTests.cs` | All serializer + migration tests |
| Modify | `Core/ShowFile.cs` | Add `CurrentVersion`, `Version`, `UnknownFields` |
| Modify | `Core/ShowFileSerializer.cs` | Add `LoadResult`, `ShowFileVersionTooNewException`, `Migrations`, `ApplyMigration`; change `LoadAsync` return type; update `SaveAsync` |
| Create | `Views/AlertDialog.cs` | Code-only Avalonia dialog for OK and Yes/No prompts |
| Modify | `ViewModels/MainViewModel.cs` | Change `LoadSessionAsync` to accept dialog callbacks, handle `LoadResult` |
| Modify | `Views/MainWindow.axaml.cs` | Pass dialog callbacks to `LoadSessionAsync` |

---

### Task 1: Create the xUnit test project

**Files:**
- Create: `ShowCast.Tests/ShowCast.Tests.csproj`

- [ ] **Step 1: Create the test project file**

Create `ShowCast.Tests/ShowCast.Tests.csproj` with this content:

```xml
<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <TargetFramework>net9.0</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <IsPackable>false</IsPackable>
  </PropertyGroup>
  <ItemGroup>
    <PackageReference Include="Microsoft.NET.Test.Sdk"        Version="17.11.1" />
    <PackageReference Include="xunit"                         Version="2.9.2"   />
    <PackageReference Include="xunit.runner.visualstudio"     Version="2.8.2"   />
  </ItemGroup>
  <ItemGroup>
    <ProjectReference Include="..\ShowCast.csproj" />
  </ItemGroup>
</Project>
```

- [ ] **Step 2: Restore packages**

```
cd ShowCast.Tests
dotnet restore
```

Expected: packages restored, no errors.

- [ ] **Step 3: Verify the project builds with no test files yet**

```
dotnet build ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected: `Build succeeded. 0 Error(s)`

---

### Task 2: Add `Version` and `UnknownFields` to `ShowFile`

**Files:**
- Modify: `Core/ShowFile.cs`
- Test: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Step 1: Create the test file with the first failing test**

Create `ShowCast.Tests/Core/ShowFileSerializerTests.cs`:

```csharp
using System.Text.Json;
using ShowCast.Core;

namespace ShowCast.Tests.Core;

public class ShowFileSerializerTests
{
    // ── Helpers ───────────────────────────────────────────────────────────────

    /// <summary>Write minimal valid ShowFile JSON with an explicit version number.</summary>
    static string TempScf(int version)
    {
        var path = Path.GetTempFileName() + ".scf";
        File.WriteAllText(path, $$$"""
            {
              "Version": {{{version}}},
              "Settings": {},
              "Timers": [],
              "Shows": [],
              "RundownFolders": [],
              "Rundowns": [],
              "Outputs": [],
              "ScheduledEvents": []
            }
            """);
        return path;
    }

    // ── ShowFile model ────────────────────────────────────────────────────────

    [Fact]
    public void ShowFile_DefaultVersion_EqualsCurrentVersion()
    {
        var file = new ShowFile();
        Assert.Equal(ShowFile.CurrentVersion, file.Version);
    }

    [Fact]
    public void ShowFile_CurrentVersion_IsPositive()
    {
        Assert.True(ShowFile.CurrentVersion >= 1);
    }
}
```

- [ ] **Step 2: Run – expect compile failure because `ShowFile.CurrentVersion` doesn't exist yet**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --no-build 2>&1 | head -20
```

Expected: build error `'ShowFile' does not contain a definition for 'CurrentVersion'`

- [ ] **Step 3: Add `CurrentVersion`, `Version`, and `UnknownFields` to `ShowFile`**

Open `Core/ShowFile.cs`. Add these three members directly after the class declaration, before all existing properties:

```csharp
public class ShowFile
{
    /// <summary>Bump this when making breaking changes to the model. Run dotnet build to catch callsites.</summary>
    public const int CurrentVersion = 1;

    /// <summary>Format version written into every saved file. Checked on load.</summary>
    public int Version { get; set; } = CurrentVersion;

    /// <summary>Captures unknown JSON fields from newer-format files for error reporting.</summary>
    [System.Text.Json.Serialization.JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? UnknownFields { get; set; }

    // ... all existing properties follow unchanged
```

- [ ] **Step 4: Run the tests – expect PASS**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 2, Skipped: 0
```

- [ ] **Step 5: Commit**

```
git add Core/ShowFile.cs ShowCast.Tests/ShowCast.Tests.csproj ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat: add Version and UnknownFields to ShowFile, add test project"
```

---

### Task 3: Add `LoadResult`, `ShowFileVersionTooNewException`, extend `LoadAsync`

**Files:**
- Modify: `Core/ShowFileSerializer.cs`
- Modify: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Step 1: Add three failing tests to `ShowFileSerializerTests.cs`**

Append inside the class body (after the existing tests):

```csharp
    // ── LoadAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task LoadAsync_CurrentVersion_ReturnsNeedsMigrationFalse()
    {
        var path = TempScf(ShowFile.CurrentVersion);
        try
        {
            var result = await ShowFileSerializer.LoadAsync(path);
            Assert.NotNull(result);
            Assert.False(result.NeedsMigration);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LoadAsync_OlderVersion_ReturnsNeedsMigrationTrue()
    {
        var path = TempScf(ShowFile.CurrentVersion - 1);
        try
        {
            var result = await ShowFileSerializer.LoadAsync(path);
            Assert.NotNull(result);
            Assert.True(result.NeedsMigration);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LoadAsync_NewerVersion_ThrowsVersionTooNew()
    {
        var path = TempScf(ShowFile.CurrentVersion + 1);
        try
        {
            await Assert.ThrowsAsync<ShowFileVersionTooNewException>(
                () => ShowFileSerializer.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }
```

- [ ] **Step 2: Run – expect compile failure (types don't exist yet)**

```
dotnet build ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | head -10
```

Expected: errors for `LoadResult`, `ShowFileVersionTooNewException`

- [ ] **Step 3: Add `LoadResult`, `ShowFileVersionTooNewException`, and update `LoadAsync` in `ShowFileSerializer.cs`**

At the top of `Core/ShowFileSerializer.cs`, before the `ShowFileSerializer` class, add:

```csharp
/// <summary>Return value of LoadAsync. Check NeedsMigration before using File.</summary>
public record LoadResult(ShowFile File, bool NeedsMigration);

/// <summary>Thrown when the file's Version exceeds the app's CurrentVersion.</summary>
public sealed class ShowFileVersionTooNewException : Exception
{
    public int FileVersion { get; }
    public ShowFileVersionTooNewException(int fileVersion)
        : base($"This file requires ShowCast file format v{fileVersion}, " +
               $"but this version of ShowCast only supports up to v{ShowFile.CurrentVersion}. " +
               $"Please update ShowCast to open this file.")
    {
        FileVersion = fileVersion;
    }
}
```

Then replace the existing `LoadAsync` method body:

```csharp
public static async Task<LoadResult?> LoadAsync(string path)
{
    await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                                            FileShare.Read, 65536, true);
    var file = await JsonSerializer.DeserializeAsync<ShowFile>(stream, _opts);
    if (file is null) return null;

    if (file.Version > ShowFile.CurrentVersion)
        throw new ShowFileVersionTooNewException(file.Version);

    return new LoadResult(file, NeedsMigration: file.Version < ShowFile.CurrentVersion);
}
```

- [ ] **Step 4: Run – expect PASS**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 5, Skipped: 0
```

Note: the `OlderVersion` test requires `CurrentVersion >= 2` to produce a version=0 file that's "older". If `CurrentVersion` is 1, version 0 is still < 1 and triggers `NeedsMigration`. This is correct — version 0 is treated as a pre-versioned legacy file.

- [ ] **Step 5: Commit**

```
git add Core/ShowFileSerializer.cs ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat: add LoadResult and ShowFileVersionTooNewException, update LoadAsync return type"
```

---

### Task 4: Add `ApplyMigration`

**Files:**
- Modify: `Core/ShowFileSerializer.cs`
- Modify: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Step 1: Add two failing tests**

Append inside `ShowFileSerializerTests`:

```csharp
    // ── ApplyMigration ────────────────────────────────────────────────────────

    [Fact]
    public void ApplyMigration_SetsVersionToCurrentVersion()
    {
        // Version 0 represents a pre-versioned legacy file; clamped to 1 internally.
        var file = new ShowFile { Version = 0 };
        ShowFileSerializer.ApplyMigration(file);
        Assert.Equal(ShowFile.CurrentVersion, file.Version);
    }

    [Fact]
    public void ApplyMigration_AlreadyCurrent_DoesNotThrow()
    {
        var file = new ShowFile { Version = ShowFile.CurrentVersion };
        var ex = Record.Exception(() => ShowFileSerializer.ApplyMigration(file));
        Assert.Null(ex);
        Assert.Equal(ShowFile.CurrentVersion, file.Version);
    }
```

- [ ] **Step 2: Run – expect compile failure (`ApplyMigration` not defined)**

```
dotnet build ShowCast.Tests/ShowCast.Tests.csproj -v minimal 2>&1 | head -5
```

Expected: `'ShowFileSerializer' does not contain a definition for 'ApplyMigration'`

- [ ] **Step 3: Add `Migrations` list and `ApplyMigration` to `ShowFileSerializer.cs`**

Inside the `ShowFileSerializer` class, add these two members (place them just before `SaveAsync`):

```csharp
    /// <summary>
    /// One entry per version step. Index 0 upgrades v1→v2, index 1 upgrades v2→v3, etc.
    /// Add a new entry here whenever CurrentVersion is bumped.
    /// </summary>
    static readonly List<Action<ShowFile>> Migrations = new()
    {
        // (empty — no breaking changes yet; add entries as CurrentVersion increases)
    };

    /// <summary>
    /// Runs all migration steps from file.Version up to CurrentVersion.
    /// Call only after the user has confirmed the upgrade prompt.
    /// </summary>
    public static void ApplyMigration(ShowFile file)
    {
        // Clamp to 1: version 0 means pre-versioned; treat as v1 with no migrations needed.
        int from = Math.Max(file.Version, 1);
        for (int v = from; v < ShowFile.CurrentVersion; v++)
            Migrations[v - 1](file);
        file.Version = ShowFile.CurrentVersion;
    }
```

- [ ] **Step 4: Run – expect PASS**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 7, Skipped: 0
```

- [ ] **Step 5: Commit**

```
git add Core/ShowFileSerializer.cs ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat: add ApplyMigration with empty migration chain"
```

---

### Task 5: Update `SaveAsync` to clear `UnknownFields`

**Files:**
- Modify: `Core/ShowFileSerializer.cs`
- Modify: `ShowCast.Tests/Core/ShowFileSerializerTests.cs`

- [ ] **Step 1: Add a failing round-trip test**

Append inside `ShowFileSerializerTests`:

```csharp
    // ── SaveAsync ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task SaveAsync_DoesNotRoundTripUnknownFields()
    {
        // Simulate a file that was loaded from a newer format (has unknown fields)
        // by writing JSON with an extra field, loading it, then saving + reloading.
        var loadPath = Path.GetTempFileName() + ".scf";
        var savePath = Path.GetTempFileName() + ".scf";
        File.WriteAllText(loadPath, """
            {
              "Version": 1,
              "FutureFeature": "should be dropped on save",
              "Settings": {},
              "Timers": [],
              "Shows": [],
              "RundownFolders": [],
              "Rundowns": [],
              "Outputs": [],
              "ScheduledEvents": []
            }
            """);

        try
        {
            var loaded = await ShowFileSerializer.LoadAsync(loadPath);
            Assert.NotNull(loaded);
            Assert.NotNull(loaded.File.UnknownFields);       // captured on load
            Assert.True(loaded.File.UnknownFields!.ContainsKey("FutureFeature"));

            await ShowFileSerializer.SaveAsync(loaded.File, savePath);

            var savedJson = await File.ReadAllTextAsync(savePath);
            Assert.DoesNotContain("FutureFeature", savedJson);  // stripped on save
        }
        finally
        {
            File.Delete(loadPath);
            File.Delete(savePath);
        }
    }
```

- [ ] **Step 2: Run – expect FAIL (unknown fields currently round-trip)**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "SaveAsync_DoesNotRoundTripUnknownFields" -v minimal
```

Expected: `Failed: 1` — `Assert.DoesNotContain` fails because `FutureFeature` is in the saved JSON.

- [ ] **Step 3: Update `SaveAsync` in `ShowFileSerializer.cs`**

Replace the existing `SaveAsync` method body:

```csharp
    public static async Task SaveAsync(ShowFile file, string path)
    {
        // Strip captured unknown fields so they are not round-tripped back into the file.
        file.UnknownFields = null;

        var tmp = path + ".tmp";
        {
            await using var stream = new FileStream(tmp, FileMode.Create, FileAccess.Write,
                                                    FileShare.None, 65536, true);
            await JsonSerializer.SerializeAsync(stream, file, _opts);
        }
        File.Move(tmp, path, overwrite: true);
    }
```

- [ ] **Step 4: Run all tests – expect PASS**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 8, Skipped: 0
```

- [ ] **Step 5: Commit**

```
git add Core/ShowFileSerializer.cs ShowCast.Tests/Core/ShowFileSerializerTests.cs
git commit -m "feat: clear UnknownFields in SaveAsync to prevent unknown-field round-trip"
```

---

### Task 6: Create `AlertDialog`

**Files:**
- Create: `Views/AlertDialog.cs`

This dialog handles two modes: a single-button OK (for errors) and a two-button Yes/Cancel (for prompts). It follows the same code-only pattern as `TextInputDialog.cs`.

- [ ] **Step 1: Create `Views/AlertDialog.cs`**

```csharp
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;

namespace ShowCast.Views;

/// <summary>
/// Code-only modal dialog for error messages (OK) and confirmation prompts (Yes/Cancel).
/// </summary>
public sealed class AlertDialog : Window
{
    bool _confirmed;

    AlertDialog(string title, string message, bool showCancel)
    {
        Title                 = title;
        Width                 = 420;
        SizeToContent         = SizeToContent.Height;
        CanResize             = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;

        var ok = new Button
        {
            Content                  = showCancel ? "Yes" : "OK",
            Width                    = 80,
            Height                   = 35,
            CornerRadius             = new CornerRadius(5),
            Background               = Avalonia.Media.SolidColorBrush.Parse("#555555"),
            Foreground               = Avalonia.Media.Brushes.White,
            FontWeight               = Avalonia.Media.FontWeight.Bold,
            HorizontalContentAlignment = HorizontalAlignment.Center,
            VerticalContentAlignment   = VerticalAlignment.Center,
        };
        ok.Click += (_, _) => { _confirmed = true; Close(); };

        var buttons = new StackPanel
        {
            Orientation             = Orientation.Horizontal,
            HorizontalAlignment     = HorizontalAlignment.Right,
            Spacing                 = 8,
        };

        if (showCancel)
        {
            var cancel = new Button
            {
                Content                  = "Cancel",
                Width                    = 80,
                Height                   = 35,
                CornerRadius             = new CornerRadius(5),
                Background               = Avalonia.Media.SolidColorBrush.Parse("#3a3a3a"),
                Foreground               = Avalonia.Media.Brushes.White,
                HorizontalContentAlignment = HorizontalAlignment.Center,
                VerticalContentAlignment   = VerticalAlignment.Center,
            };
            cancel.Click += (_, _) => Close();
            buttons.Children.Add(cancel);
        }

        buttons.Children.Add(ok);

        Content = new Border
        {
            Background = Avalonia.Media.SolidColorBrush.Parse("#2d2d2d"),
            Padding    = new Thickness(20),
            Child      = new StackPanel
            {
                Spacing = 16,
                Children =
                {
                    new TextBlock
                    {
                        Text           = message,
                        Foreground     = Avalonia.Media.Brushes.White,
                        FontSize       = 14,
                        TextWrapping   = Avalonia.Media.TextWrapping.Wrap,
                        MaxWidth       = 380,
                    },
                    buttons,
                }
            }
        };
    }

    Task<bool> ShowAsync(Window owner)
    {
        var tcs = new TaskCompletionSource<bool>();
        Closed += (_, _) => tcs.SetResult(_confirmed);
        ShowDialog(owner);
        return tcs.Task;
    }

    /// <summary>Show a non-blocking error notification. Awaiting it waits for the user to dismiss.</summary>
    public static Task ShowError(Window owner, string title, string message)
        => new AlertDialog(title, message, showCancel: false).ShowAsync(owner);

    /// <summary>Ask the user a yes/cancel question. Returns true if they chose Yes.</summary>
    public static Task<bool> ShowConfirm(Window owner, string title, string message)
        => new AlertDialog(title, message, showCancel: true).ShowAsync(owner);
}
```

- [ ] **Step 2: Build the main project to verify no compile errors**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 3: Commit**

```
git add Views/AlertDialog.cs
git commit -m "feat: add AlertDialog (code-only) for error and confirm prompts"
```

---

### Task 7: Update `LoadSessionAsync` in `MainViewModel`

**Files:**
- Modify: `ViewModels/MainViewModel.cs` (lines 45–55)

- [ ] **Step 1: Replace `LoadSessionAsync` (lines 45–55)**

The current method:

```csharp
public async Task<bool> LoadSessionAsync(string path)
{
    var loaded = await ShowFileSerializer.LoadAsync(path);
    if (loaded is null) return false;
    PageRenderer.ClearImageCache();
    _showFile = loaded;
    MigratePageNames(_showFile);
    RebuildFromShowFile();
    RestoreSettings();
    return true;
}
```

Replace it with:

```csharp
/// <param name="confirmMigration">
///   Called when the file needs migration. Return true to proceed, false to cancel.
///   Null = auto-migrate without prompting (used by tests and future headless callers).
/// </param>
/// <param name="reportVersionTooNew">
///   Called when the file's format version exceeds what this app supports.
///   Null = silently return false.
/// </param>
public async Task<bool> LoadSessionAsync(
    string path,
    Func<Task<bool>>? confirmMigration  = null,
    Func<string, Task>? reportVersionTooNew = null)
{
    LoadResult? result;
    try
    {
        result = await ShowFileSerializer.LoadAsync(path);
    }
    catch (ShowFileVersionTooNewException ex)
    {
        if (reportVersionTooNew is not null)
            await reportVersionTooNew(ex.Message);
        return false;
    }

    if (result is null) return false;

    if (result.NeedsMigration)
    {
        bool proceed = confirmMigration is null || await confirmMigration();
        if (!proceed) return false;
        ShowFileSerializer.ApplyMigration(result.File);
    }

    PageRenderer.ClearImageCache();
    _showFile = result.File;
    MigratePageNames(_showFile);
    RebuildFromShowFile();
    RestoreSettings();
    return true;
}
```

- [ ] **Step 2: Build the main project to verify no compile errors**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: `Build succeeded. 0 Error(s)`

Note: `MainWindow.axaml.cs` line 31 still calls `LoadSessionAsync(path)` with no callbacks — that still compiles because the callbacks are optional parameters. The build will succeed.

- [ ] **Step 3: Run all tests to confirm nothing regressed**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 8, Skipped: 0
```

- [ ] **Step 4: Commit**

```
git add ViewModels/MainViewModel.cs
git commit -m "feat: update LoadSessionAsync to handle LoadResult, migration prompt, and version-too-new error"
```

---

### Task 8: Pass dialog callbacks from `MainWindow`

**Files:**
- Modify: `Views/MainWindow.axaml.cs` (line 31)

- [ ] **Step 1: Update `OnOpened` to pass dialog callbacks**

Current code in `OnOpened` (around line 29–33):

```csharp
if (File.Exists(AppFolders.SessionFile))
{
    await VM.LoadSessionAsync(AppFolders.SessionFile);
    RestoreWindowState(VM.ShowFile.Settings);
}
```

Replace with:

```csharp
if (File.Exists(AppFolders.SessionFile))
{
    var loaded = await VM.LoadSessionAsync(
        AppFolders.SessionFile,
        confirmMigration: () => AlertDialog.ShowConfirm(
            this,
            "Upgrade File Format",
            "This file was saved with an older version of ShowCast.\n\nUpgrade it to the current format?\n\n(The original file will not be overwritten until you save.)"),
        reportVersionTooNew: msg => AlertDialog.ShowError(
            this,
            "File Too New",
            msg));

    if (loaded)
        RestoreWindowState(VM.ShowFile.Settings);
}
```

The `if (loaded)` guard prevents `RestoreWindowState` from running on a cancelled or failed load (which would crash on a null `ShowFile`).

- [ ] **Step 2: Add the `using` for `AlertDialog`**

`AlertDialog` is in the same `ShowCast.Views` namespace as `MainWindow`, so no additional `using` is needed.

- [ ] **Step 3: Build the main project**

```
dotnet build ShowCast.csproj -v minimal
```

Expected: `Build succeeded. 0 Error(s)`

- [ ] **Step 4: Run all tests**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
```

Expected:
```
Passed! - Failed: 0, Passed: 8, Skipped: 0
```

- [ ] **Step 5: Commit**

```
git add Views/MainWindow.axaml.cs
git commit -m "feat: wire migration prompt and version-too-new error dialogs in MainWindow"
```

---

### Task 9: Smoke test and final verification

- [ ] **Step 1: Run the app and verify normal startup**

```
dotnet run --project ShowCast.csproj
```

Expected: app opens normally, existing session file (if any) loads without dialogs.

- [ ] **Step 2: Verify a fresh file saves with `Version: 1`**

After the app opens, create a new show (or use the demo content), then save the session. Open the saved `.scf` file in a text editor. Verify the JSON contains:

```json
"Version": 1,
```

and does NOT contain `"UnknownFields"`.

- [ ] **Step 3: Verify the version-too-new path (manual)**

Create a test file by editing a saved `.scf` in a text editor, changing `"Version": 1` to `"Version": 99`. Open ShowCast and try to load that file (File → Open or by placing it as `session.scf`).

Expected: a dialog appears saying "This file requires ShowCast file format v99 … Please update ShowCast to open this file." The app does not crash or load partial content.

- [ ] **Step 4: Run full test suite one final time**

```
dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v normal
```

Expected:
```
Passed! - Failed: 0, Passed: 8, Skipped: 0
```

- [ ] **Step 5: Final commit**

```
git add -A
git commit -m "feat: file format versioning complete — version gate, migration chain, dialogs"
```
