# Check for Updates Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add automatic startup update checking and a Help-menu manual trigger that downloads the latest GitHub release installer and optionally launches it.

**Architecture:** A static `UpdateChecker` class handles GitHub API queries and streaming downloads. `UpdatePreferences` persists skip/remind-later state as JSON. Two code-first dialogs (matching `AlertDialog` style) drive the prompt and download flows. `MainWindow` wires everything together via a single `CheckForUpdatesAsync(bool silent)` method.

**Tech Stack:** .NET 8, `System.Net.Http.HttpClient`, `System.Text.Json`, Avalonia UI, xUnit

---

## File Map

| Action | Path | Responsibility |
|--------|------|----------------|
| Modify | `ShowCast.csproj` | Add `<Version>1.1.1</Version>` |
| Modify | `Core/AppFolders.cs` | Add `UpdatePrefsFile` path |
| Create | `Core/UpdatePreferences.cs` | Skip/remind-later state + JSON persistence |
| Create | `Core/UpdateChecker.cs` | GitHub API check + streaming download |
| Create | `Views/UpdateAvailableDialog.cs` | Three-button update prompt |
| Create | `Views/UpdateDownloadDialog.cs` | Progress bar + post-download actions |
| Modify | `Views/MainWindow.axaml` | Add "Check for Updates…" to Help menu |
| Modify | `Views/MainWindow.axaml.cs` | Startup check + menu handler + installer launch |
| Create | `ShowCast.Tests/Core/UpdatePreferencesTests.cs` | ShouldShow logic + roundtrip |
| Create | `ShowCast.Tests/Core/UpdateCheckerTests.cs` | JSON parsing + version comparison |

---

## Task 1: Embed version number and add prefs file path

**Files:**
- Modify: `ShowCast.csproj`
- Modify: `Core/AppFolders.cs`

- [ ] **Step 1: Add `<Version>` to the csproj**

  Open `ShowCast.csproj`. Inside `<PropertyGroup>`, add after the `<RootNamespace>` line:

  ```xml
  <Version>1.1.1</Version>
  ```

- [ ] **Step 2: Add `UpdatePrefsFile` to `AppFolders`**

  Open `Core/AppFolders.cs`. After the `SessionFile` property, add:

  ```csharp
  public static string UpdatePrefsFile => Path.Combine(Configuration, "update_prefs.json");
  ```

- [ ] **Step 3: Build to verify**

  ```
  dotnet build ShowCast.csproj
  ```
  Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

  ```
  git add ShowCast.csproj Core/AppFolders.cs
  git commit -m "chore: embed version 1.1.1 and add update prefs file path"
  ```

---

## Task 2: UpdatePreferences — skip/remind-later persistence

**Files:**
- Create: `Core/UpdatePreferences.cs`
- Create: `ShowCast.Tests/Core/UpdatePreferencesTests.cs`

- [ ] **Step 1: Write the failing tests**

  Create `ShowCast.Tests/Core/UpdatePreferencesTests.cs`:

  ```csharp
  using System;
  using System.IO;
  using ShowCast.Core;
  using Xunit;

  namespace ShowCast.Tests.Core;

  public class UpdatePreferencesTests
  {
      [Fact]
      public void ShouldShow_DefaultPrefs_ReturnsTrue()
      {
          var prefs = new UpdatePreferences();
          Assert.True(prefs.ShouldShow("1.2.0"));
      }

      [Fact]
      public void ShouldShow_SkippedVersionMatches_ReturnsFalse()
      {
          var prefs = new UpdatePreferences { SkippedVersion = "1.2.0" };
          Assert.False(prefs.ShouldShow("1.2.0"));
      }

      [Fact]
      public void ShouldShow_SkippedVersionDiffers_ReturnsTrue()
      {
          var prefs = new UpdatePreferences { SkippedVersion = "1.1.0" };
          Assert.True(prefs.ShouldShow("1.2.0"));
      }

      [Fact]
      public void ShouldShow_RemindAfterInFuture_ReturnsFalse()
      {
          var prefs = new UpdatePreferences { RemindAfter = DateTime.UtcNow.AddDays(1) };
          Assert.False(prefs.ShouldShow("1.2.0"));
      }

      [Fact]
      public void ShouldShow_RemindAfterInPast_ReturnsTrue()
      {
          var prefs = new UpdatePreferences { RemindAfter = DateTime.UtcNow.AddDays(-1) };
          Assert.True(prefs.ShouldShow("1.2.0"));
      }

      [Fact]
      public void SaveLoad_RoundTrip_PreservesValues()
      {
          var path = Path.GetTempFileName();
          try
          {
              var prefs = new UpdatePreferences
              {
                  SkippedVersion = "1.5.0",
                  RemindAfter    = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
              };
              prefs.Save(path);

              var loaded = UpdatePreferences.Load(path);
              Assert.Equal("1.5.0", loaded.SkippedVersion);
              Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), loaded.RemindAfter);
          }
          finally { File.Delete(path); }
      }

      [Fact]
      public void Load_MissingFile_ReturnsDefaults()
      {
          var loaded = UpdatePreferences.Load("/nonexistent/path/prefs.json");
          Assert.Null(loaded.SkippedVersion);
          Assert.Null(loaded.RemindAfter);
      }
  }
  ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```
  dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "UpdatePreferencesTests" -v minimal
  ```
  Expected: compile error — `UpdatePreferences` not found.

- [ ] **Step 3: Create `Core/UpdatePreferences.cs`**

  ```csharp
  using System;
  using System.IO;
  using System.Text.Json;

  namespace ShowCast.Core;

  public class UpdatePreferences
  {
      public string?   SkippedVersion { get; set; }
      public DateTime? RemindAfter    { get; set; }

      public bool ShouldShow(string latestVersion) =>
          SkippedVersion != latestVersion &&
          (RemindAfter is null || DateTime.UtcNow >= RemindAfter);

      public static UpdatePreferences Load(string? path = null)
      {
          path ??= AppFolders.UpdatePrefsFile;
          if (!File.Exists(path)) return new UpdatePreferences();
          try
          {
              var json = File.ReadAllText(path);
              return JsonSerializer.Deserialize<UpdatePreferences>(json)
                  ?? new UpdatePreferences();
          }
          catch { return new UpdatePreferences(); }
      }

      public void Save(string? path = null)
      {
          path ??= AppFolders.UpdatePrefsFile;
          var json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
          File.WriteAllText(path, json);
      }
  }
  ```

- [ ] **Step 4: Run tests to confirm they pass**

  ```
  dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "UpdatePreferencesTests" -v minimal
  ```
  Expected: All 6 tests pass.

- [ ] **Step 5: Commit**

  ```
  git add Core/UpdatePreferences.cs ShowCast.Tests/Core/UpdatePreferencesTests.cs
  git commit -m "feat: add UpdatePreferences with skip and remind-later logic"
  ```

---

## Task 3: UpdateChecker — version check and JSON parsing

**Files:**
- Create: `Core/UpdateChecker.cs`
- Create: `ShowCast.Tests/Core/UpdateCheckerTests.cs`

- [ ] **Step 1: Write the failing tests**

  Create `ShowCast.Tests/Core/UpdateCheckerTests.cs`:

  ```csharp
  using System;
  using ShowCast.Core;
  using Xunit;

  namespace ShowCast.Tests.Core;

  public class UpdateCheckerTests
  {
      const string NewerReleaseJson = """
          {
            "tag_name": "v1.2.0",
            "assets": [
              {
                "name": "ShowCast-1.2.0-win-x64-setup.exe",
                "browser_download_url": "https://example.com/ShowCast-1.2.0-win-x64-setup.exe"
              }
            ]
          }
          """;

      const string SameVersionJson = """
          {
            "tag_name": "v1.1.1",
            "assets": [
              {
                "name": "ShowCast-1.1.1-win-x64-setup.exe",
                "browser_download_url": "https://example.com/ShowCast-1.1.1-win-x64-setup.exe"
              }
            ]
          }
          """;

      const string OlderVersionJson = """
          {
            "tag_name": "v1.0.0",
            "assets": [
              {
                "name": "ShowCast-1.0.0-win-x64-setup.exe",
                "browser_download_url": "https://example.com/ShowCast-1.0.0-win-x64-setup.exe"
              }
            ]
          }
          """;

      const string NoMatchingAssetJson = """
          {
            "tag_name": "v1.2.0",
            "assets": [
              {
                "name": "ShowCast-1.2.0-linux.tar.gz",
                "browser_download_url": "https://example.com/ShowCast-1.2.0-linux.tar.gz"
              }
            ]
          }
          """;

      [Fact]
      public void ParseRelease_NewerVersion_ReturnsInfo()
      {
          var result = UpdateChecker.ParseRelease(NewerReleaseJson, new Version(1, 1, 1, 0));
          Assert.NotNull(result);
          Assert.Equal("1.2.0", result.Version);
          Assert.Equal("ShowCast-1.2.0-win-x64-setup.exe", result.AssetName);
          Assert.Equal("https://example.com/ShowCast-1.2.0-win-x64-setup.exe", result.DownloadUrl);
      }

      [Fact]
      public void ParseRelease_SameVersion_ReturnsNull()
      {
          var result = UpdateChecker.ParseRelease(SameVersionJson, new Version(1, 1, 1, 0));
          Assert.Null(result);
      }

      [Fact]
      public void ParseRelease_OlderVersion_ReturnsNull()
      {
          var result = UpdateChecker.ParseRelease(OlderVersionJson, new Version(1, 1, 1, 0));
          Assert.Null(result);
      }

      [Fact]
      public void ParseRelease_NoMatchingAsset_ReturnsNull()
      {
          var result = UpdateChecker.ParseRelease(NoMatchingAssetJson, new Version(1, 1, 1, 0));
          Assert.Null(result);
      }

      [Fact]
      public void ParseRelease_TagWithVPrefix_ParsesCorrectly()
      {
          var result = UpdateChecker.ParseRelease(NewerReleaseJson, new Version(1, 1, 0, 0));
          Assert.NotNull(result);
          Assert.Equal("1.2.0", result.Version);
      }

      [Theory]
      [InlineData(2, 0, 0, 1, 1, 1, true)]
      [InlineData(1, 2, 0, 1, 1, 1, true)]
      [InlineData(1, 1, 2, 1, 1, 1, true)]
      [InlineData(1, 1, 1, 1, 1, 1, false)]
      [InlineData(1, 1, 0, 1, 1, 1, false)]
      [InlineData(1, 0, 0, 1, 1, 1, false)]
      public void IsNewer_VariousVersions(
          int lMaj, int lMin, int lPatch,
          int cMaj, int cMin, int cPatch,
          bool expected)
      {
          var latest  = new Version(lMaj, lMin, lPatch);
          var current = new Version(cMaj, cMin, cPatch, 0);
          Assert.Equal(expected, UpdateChecker.IsNewer(latest, current));
      }
  }
  ```

- [ ] **Step 2: Run tests to confirm they fail**

  ```
  dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "UpdateCheckerTests" -v minimal
  ```
  Expected: compile error — `UpdateChecker` not found.

- [ ] **Step 3: Create `Core/UpdateChecker.cs`** (check + parsing only; download added in Task 4)

  ```csharp
  using System;
  using System.IO;
  using System.Linq;
  using System.Net.Http;
  using System.Net.Http.Headers;
  using System.Reflection;
  using System.Text.Json;
  using System.Threading;
  using System.Threading.Tasks;

  namespace ShowCast.Core;

  public record UpdateInfo(string Version, string AssetName, string DownloadUrl)
  {
      public string InstallerPath => Path.Combine(Path.GetTempPath(), AssetName);
  }

  public static class UpdateChecker
  {
      static readonly HttpClient _http = new();
      const string ApiUrl = "https://api.github.com/repos/casey-tmc97/ShowCast/releases/latest";

      // Throws on network/HTTP failure; returns null when already up to date.
      public static async Task<UpdateInfo?> CheckAsync()
      {
          _http.DefaultRequestHeaders.UserAgent.Clear();
          _http.DefaultRequestHeaders.UserAgent.Add(
              new ProductInfoHeaderValue("ShowCast",
                  Assembly.GetExecutingAssembly().GetName().Version?.ToString() ?? "0"));

          var json    = await _http.GetStringAsync(ApiUrl);
          var current = Assembly.GetExecutingAssembly().GetName().Version!;
          return ParseRelease(json, current);
      }

      // Internal so tests can call it without touching the network.
      internal static UpdateInfo? ParseRelease(string json, Version currentVersion)
      {
          using var doc  = JsonDocument.Parse(json);
          var root       = doc.RootElement;
          var tag        = root.GetProperty("tag_name").GetString() ?? "";
          var versionStr = tag.TrimStart('v');

          if (!Version.TryParse(versionStr, out var latest)) return null;
          if (!IsNewer(latest, currentVersion)) return null;

          var asset = root.GetProperty("assets").EnumerateArray()
              .FirstOrDefault(a =>
                  a.GetProperty("name").GetString()?.EndsWith("-win-x64-setup.exe") == true);

          if (asset.ValueKind == JsonValueKind.Undefined) return null;

          var name = asset.GetProperty("name").GetString()!;
          var url  = asset.GetProperty("browser_download_url").GetString()!;
          return new UpdateInfo(versionStr, name, url);
      }

      // Internal so tests can call it directly.
      internal static bool IsNewer(Version latest, Version current)
      {
          if (latest.Major != current.Major) return latest.Major > current.Major;
          if (latest.Minor != current.Minor) return latest.Minor > current.Minor;
          return latest.Build > current.Build;
      }

      public static async Task DownloadAsync(
          UpdateInfo info, IProgress<double> progress, CancellationToken ct)
      {
          using var response = await _http.GetAsync(
              info.DownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct);
          response.EnsureSuccessStatusCode();

          var total = response.Content.Headers.ContentLength ?? -1L;

          await using var stream = await response.Content.ReadAsStreamAsync(ct);
          await using var file   = new FileStream(
              info.InstallerPath, FileMode.Create, FileAccess.Write,
              FileShare.None, 81920, useAsync: true);

          var buffer    = new byte[81920];
          long received = 0;
          int  read;
          while ((read = await stream.ReadAsync(buffer, ct)) > 0)
          {
              await file.WriteAsync(buffer.AsMemory(0, read), ct);
              received += read;
              if (total > 0) progress.Report((double)received / total);
          }
      }
  }
  ```

- [ ] **Step 4: Run tests to confirm they pass**

  ```
  dotnet test ShowCast.Tests/ShowCast.Tests.csproj --filter "UpdateCheckerTests" -v minimal
  ```
  Expected: All 10 tests pass.

- [ ] **Step 5: Build to confirm no errors**

  ```
  dotnet build ShowCast.csproj
  ```
  Expected: 0 errors.

- [ ] **Step 6: Commit**

  ```
  git add Core/UpdateChecker.cs ShowCast.Tests/Core/UpdateCheckerTests.cs
  git commit -m "feat: add UpdateChecker with GitHub API check and streaming download"
  ```

---

## Task 4: UpdateAvailableDialog

**Files:**
- Create: `Views/UpdateAvailableDialog.cs`

The dialog follows the exact code-first style of `AlertDialog` — no AXAML, constructed entirely in C#.

- [ ] **Step 1: Create `Views/UpdateAvailableDialog.cs`**

  ```csharp
  using System.Threading.Tasks;
  using Avalonia;
  using Avalonia.Controls;
  using Avalonia.Layout;
  using Avalonia.Media;

  namespace ShowCast.Views;

  public class UpdateAvailableDialog : Window
  {
      public enum UpdateChoice { OK, RemindLater, SkipVersion, Dismissed }

      UpdateChoice _result = UpdateChoice.Dismissed;

      UpdateAvailableDialog(string latestVersion, string currentVersion)
      {
          Title                 = "Update Available";
          Width                 = 460;
          SizeToContent         = SizeToContent.Height;
          CanResize             = false;
          WindowStartupLocation = WindowStartupLocation.CenterOwner;

          var skip = MakeButton("Skip This Version", "#3a3a3a", 130);
          var remind = MakeButton("Remind Me Later",  "#3a3a3a", 120);
          var ok     = MakeButton("OK",               "#555555",  80);

          skip.Click   += (_, _) => { _result = UpdateChoice.SkipVersion;  Close(); };
          remind.Click += (_, _) => { _result = UpdateChoice.RemindLater;  Close(); };
          ok.Click     += (_, _) => { _result = UpdateChoice.OK;           Close(); };

          Content = new Border
          {
              Background = SolidColorBrush.Parse("#2d2d2d"),
              Padding    = new Thickness(20),
              Child      = new StackPanel
              {
                  Spacing  = 16,
                  Children =
                  {
                      new TextBlock
                      {
                          Text         = $"ShowCast {latestVersion} is available",
                          Foreground   = Brushes.White,
                          FontSize     = 15,
                          FontWeight   = FontWeight.Bold
                      },
                      new TextBlock
                      {
                          Text         = $"You have version {currentVersion}. Would you like to download and install the update?",
                          Foreground   = Brushes.White,
                          FontSize     = 13,
                          TextWrapping = TextWrapping.Wrap
                      },
                      new StackPanel
                      {
                          Orientation         = Orientation.Horizontal,
                          HorizontalAlignment = HorizontalAlignment.Right,
                          Spacing             = 8,
                          Children            = { skip, remind, ok }
                      }
                  }
              }
          };
      }

      static Button MakeButton(string label, string bg, double width) => new()
      {
          Content                    = label,
          Width                      = width,
          Height                     = 35,
          CornerRadius               = new CornerRadius(5),
          Background                 = SolidColorBrush.Parse(bg),
          Foreground                 = Brushes.White,
          HorizontalContentAlignment = HorizontalAlignment.Center,
          VerticalContentAlignment   = VerticalAlignment.Center
      };

      Task<UpdateChoice> ShowAsync(Window owner)
      {
          var tcs = new TaskCompletionSource<UpdateChoice>();
          Closed += (_, _) => tcs.SetResult(_result);
          ShowDialog(owner);
          return tcs.Task;
      }

      public static Task<UpdateChoice> ShowAsync(Window owner, string latestVersion, string currentVersion)
          => new UpdateAvailableDialog(latestVersion, currentVersion).ShowAsync(owner);
  }
  ```

- [ ] **Step 2: Build to confirm no errors**

  ```
  dotnet build ShowCast.csproj
  ```
  Expected: 0 errors.

- [ ] **Step 3: Commit**

  ```
  git add Views/UpdateAvailableDialog.cs
  git commit -m "feat: add UpdateAvailableDialog with OK/Remind Later/Skip Version buttons"
  ```

---

## Task 5: UpdateDownloadDialog

**Files:**
- Create: `Views/UpdateDownloadDialog.cs`

- [ ] **Step 1: Create `Views/UpdateDownloadDialog.cs`**

  ```csharp
  using System;
  using System.IO;
  using System.Threading;
  using System.Threading.Tasks;
  using Avalonia;
  using Avalonia.Controls;
  using Avalonia.Layout;
  using Avalonia.Media;
  using Avalonia.Threading;
  using ShowCast.Core;

  namespace ShowCast.Views;

  public class UpdateDownloadDialog : Window
  {
      public enum DownloadDialogResult { None, InstallAndRestart, CloseApp }

      public DownloadDialogResult Result { get; private set; } = DownloadDialogResult.None;

      readonly UpdateInfo    _info;
      readonly Func<Task>    _beforeClose;
      CancellationTokenSource _cts = new();

      readonly ProgressBar _progressBar;
      readonly TextBlock   _statusText;
      readonly StackPanel  _downloadingButtons;
      readonly StackPanel  _completeButtons;
      readonly StackPanel  _errorButtons;
      readonly TextBlock   _errorText;

      public UpdateDownloadDialog(UpdateInfo info, Func<Task> beforeClose)
      {
          _info        = info;
          _beforeClose = beforeClose;

          Title                 = $"Downloading ShowCast {info.Version}";
          Width                 = 460;
          SizeToContent         = SizeToContent.Height;
          CanResize             = false;
          WindowStartupLocation = WindowStartupLocation.CenterOwner;

          _progressBar = new ProgressBar { Minimum = 0, Maximum = 1, Value = 0, Height = 8, CornerRadius = new CornerRadius(4) };
          _statusText  = new TextBlock { Text = "Starting download…", Foreground = SolidColorBrush.Parse("#aaaaaa"), FontSize = 12 };
          _errorText   = new TextBlock { Foreground = SolidColorBrush.Parse("#e07050"), FontSize = 12, TextWrapping = TextWrapping.Wrap };

          var cancelBtn  = MakeButton("Cancel",             "#3a3a3a",  80);
          var installBtn = MakeButton("Install && Restart", "#2e7d32", 140);
          var closeBtn   = MakeButton("Close App",          "#555555",  90);
          var retryBtn   = MakeButton("Try Again",          "#555555",  90);
          var errorClose = MakeButton("Close",              "#3a3a3a",  80);

          cancelBtn.Click  += (_, _) => { _cts.Cancel(); };
          installBtn.Click += async (_, _) =>
          {
              installBtn.IsEnabled = false;
              closeBtn.IsEnabled   = false;
              await _beforeClose();
              Result = DownloadDialogResult.InstallAndRestart;
              Close();
          };
          closeBtn.Click   += (_, _) => { Result = DownloadDialogResult.CloseApp; Close(); };
          retryBtn.Click   += (_, _) => { _ = StartDownloadAsync(); };
          errorClose.Click += (_, _) => Close();

          _downloadingButtons = new StackPanel
          {
              Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
              Children    = { cancelBtn }
          };
          _completeButtons = new StackPanel
          {
              Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
              Spacing     = 8, Children = { closeBtn, installBtn }, IsVisible = false
          };
          _errorButtons = new StackPanel
          {
              Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Right,
              Spacing     = 8, Children = { errorClose, retryBtn }, IsVisible = false
          };

          Content = new Border
          {
              Background = SolidColorBrush.Parse("#2d2d2d"),
              Padding    = new Thickness(20),
              Child      = new StackPanel
              {
                  Spacing  = 12,
                  Children =
                  {
                      new TextBlock
                      {
                          Text       = $"Downloading ShowCast {info.Version}…",
                          Foreground = Brushes.White,
                          FontSize   = 14,
                          FontWeight = FontWeight.Bold
                      },
                      _progressBar,
                      _statusText,
                      _errorText,
                      _downloadingButtons,
                      _completeButtons,
                      _errorButtons
                  }
              }
          };
      }

      static Button MakeButton(string label, string bg, double width) => new()
      {
          Content                    = label,
          Width                      = width,
          Height                     = 35,
          CornerRadius               = new CornerRadius(5),
          Background                 = SolidColorBrush.Parse(bg),
          Foreground                 = Brushes.White,
          HorizontalContentAlignment = HorizontalAlignment.Center,
          VerticalContentAlignment   = VerticalAlignment.Center
      };

      protected override void OnOpened(EventArgs e)
      {
          base.OnOpened(e);
          _ = StartDownloadAsync();
      }

      async Task StartDownloadAsync()
      {
          _cts = new CancellationTokenSource();
          SetState(State.Downloading);

          try
          {
              var progress = new Progress<double>(p =>
              {
                  _progressBar.Value = p;
                  _statusText.Text   = $"{p:P0} downloaded";
              });
              await UpdateChecker.DownloadAsync(_info, progress, _cts.Token);
              SetState(State.Complete);
          }
          catch (OperationCanceledException)
          {
              if (File.Exists(_info.InstallerPath))
                  File.Delete(_info.InstallerPath);
              Close();
          }
          catch (Exception ex)
          {
              SetState(State.Error, ex.Message);
          }
      }

      enum State { Downloading, Complete, Error }

      void SetState(State state, string? errorMessage = null)
      {
          Dispatcher.UIThread.Post(() =>
          {
              _downloadingButtons.IsVisible = state == State.Downloading;
              _completeButtons.IsVisible    = state == State.Complete;
              _errorButtons.IsVisible       = state == State.Error;
              _errorText.IsVisible          = state == State.Error;

              if (state == State.Complete)
              {
                  _progressBar.Value   = 1;
                  _statusText.Text     = "Download complete.";
                  Title                = $"ShowCast {_info.Version} Ready";
              }
              else if (state == State.Error)
              {
                  _errorText.Text  = $"Download failed: {errorMessage}";
                  _statusText.Text = "";
              }
          });
      }
  }
  ```

- [ ] **Step 2: Build to confirm no errors**

  ```
  dotnet build ShowCast.csproj
  ```
  Expected: 0 errors.

- [ ] **Step 3: Commit**

  ```
  git add Views/UpdateDownloadDialog.cs
  git commit -m "feat: add UpdateDownloadDialog with progress bar and install/close actions"
  ```

---

## Task 6: Wire MainWindow — Help menu + startup check + installer launch

**Files:**
- Modify: `Views/MainWindow.axaml`
- Modify: `Views/MainWindow.axaml.cs`

- [ ] **Step 1: Add "Check for Updates…" to the Help menu in `MainWindow.axaml`**

  Find the Help `MenuItem` block (currently only contains "User Manual") and replace it:

  ```xml
  <MenuItem Header="_Help" Foreground="White">
      <MenuItem Header="User Manual" InputGesture="F1" Click="OnManual"/>
      <Separator />
      <MenuItem Header="Check for Updates…" Click="OnCheckForUpdates"/>
  </MenuItem>
  ```

- [ ] **Step 2: Add `_launchInstallerPath` field and update `OnClosing` in `MainWindow.axaml.cs`**

  After the existing `bool _saving;` field declaration, add:

  ```csharp
  string? _launchInstallerPath;
  ```

  In `OnClosing`, just before the final `Close()` call (the one inside the `if (!_saving)` block, after `foreach (var win in _outputWindows.Values.ToList()) win.Close();`), add:

  ```csharp
  if (_launchInstallerPath is not null && System.IO.File.Exists(_launchInstallerPath))
  {
      System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_launchInstallerPath)
      {
          UseShellExecute = true
      });
  }
  ```

  The complete `OnClosing` should then look like:

  ```csharp
  protected override async void OnClosing(WindowClosingEventArgs e)
  {
      if (!_saving)
      {
          e.Cancel = true;
          _saving  = true;
          if (VM is not null)
          {
              var s = VM.ShowFile.Settings;
              s.WindowMaximized = WindowState == WindowState.Maximized;
              if (!s.WindowMaximized)
              {
                  s.WindowWidth  = Width;
                  s.WindowHeight = Height;
                  s.WindowX      = Position.X;
                  s.WindowY      = Position.Y;
              }
              SavePanelSizes(s);
              await VM.SaveSessionAsync(AppFolders.SessionFile);
          }
          foreach (var win in _outputWindows.Values.ToList())
              win.Close();
          if (_launchInstallerPath is not null && System.IO.File.Exists(_launchInstallerPath))
          {
              System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(_launchInstallerPath)
              {
                  UseShellExecute = true
              });
          }
          Close();
      }
      base.OnClosing(e);
  }
  ```

- [ ] **Step 3: Add `CheckForUpdatesAsync` method and event handlers to `MainWindow.axaml.cs`**

  Add these methods to `MainWindow`, after the `OpenManual` method:

  ```csharp
  async void OnCheckForUpdates(object? sender, Avalonia.Interactivity.RoutedEventArgs e)
      => await CheckForUpdatesAsync(silent: false);

  async Task CheckForUpdatesAsync(bool silent)
  {
      Core.UpdateInfo? info;
      try
      {
          info = await Core.UpdateChecker.CheckAsync();
      }
      catch (Exception ex)
      {
          if (!silent)
              await AlertDialog.ShowError(this, "Check for Updates",
                  $"Could not check for updates.\n\n{ex.Message}");
          return;
      }

      if (info is null)
      {
          if (!silent)
          {
              var ver = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
              await AlertDialog.ShowError(this, "Check for Updates",
                  $"You're up to date! ShowCast {ver.Major}.{ver.Minor}.{ver.Build} is the latest version.");
          }
          return;
      }

      var prefs = Core.UpdatePreferences.Load();
      if (silent && !prefs.ShouldShow(info.Version)) return;

      var current = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version!;
      var currentStr = $"{current.Major}.{current.Minor}.{current.Build}";

      var choice = await UpdateAvailableDialog.ShowAsync(this, info.Version, currentStr);

      switch (choice)
      {
          case UpdateAvailableDialog.UpdateChoice.OK:
              var dlg = new UpdateDownloadDialog(info, async () =>
              {
                  if (VM is not null)
                      await VM.SaveSessionAsync(AppFolders.SessionFile);
              });
              await dlg.ShowDialog(this);
              if (dlg.Result == UpdateDownloadDialog.DownloadDialogResult.InstallAndRestart)
              {
                  _launchInstallerPath = info.InstallerPath;
                  Close();
              }
              else if (dlg.Result == UpdateDownloadDialog.DownloadDialogResult.CloseApp)
              {
                  Close();
              }
              break;

          case UpdateAvailableDialog.UpdateChoice.RemindLater:
              prefs.RemindAfter = DateTime.UtcNow.AddDays(3);
              prefs.Save();
              break;

          case UpdateAvailableDialog.UpdateChoice.SkipVersion:
              prefs.SkippedVersion = info.Version;
              prefs.Save();
              break;
      }
  }
  ```

- [ ] **Step 4: Trigger the startup check in `OnOpened`**

  In `MainWindow.axaml.cs`, inside `OnOpened`, after the line `UpdateRightGridLayout();` and before `VM.WhenAnyValue(...)`, add:

  ```csharp
  _ = CheckForUpdatesAsync(silent: true);
  ```

- [ ] **Step 5: Build to confirm no errors**

  ```
  dotnet build ShowCast.csproj
  ```
  Expected: 0 errors.

- [ ] **Step 6: Run full test suite**

  ```
  dotnet test ShowCast.Tests/ShowCast.Tests.csproj -v minimal
  ```
  Expected: All tests pass.

- [ ] **Step 7: Commit**

  ```
  git add Views/MainWindow.axaml Views/MainWindow.axaml.cs
  git commit -m "feat: wire check-for-updates into Help menu and app startup"
  ```

---

## Task 7: Manual smoke test

- [ ] **Step 1: Build and run the app**

  ```
  dotnet run --project ShowCast.csproj
  ```

- [ ] **Startup auto-check:** App opens normally — no update dialog appears (since 1.1.1 is current). No errors in debug output from the update check.

- [ ] **Help menu → Check for Updates (up to date):** Click Help → Check for Updates… → "You're up to date! ShowCast 1.1.1 is the latest version." dialog appears.

- [ ] **Simulate an update:** Temporarily change the version check in `UpdateChecker.ParseRelease` to force a return (e.g., comment out the `if (!IsNewer(...)) return null;` guard), run the app, confirm the `UpdateAvailableDialog` appears with correct text and all three buttons work. Revert the temporary change after testing.

- [ ] **Remind Me Later:** Click Remind Me Later, close and reopen app within the 3-day window — confirm dialog does not reappear. Verify `update_prefs.json` in `Documents/ShowCast/Configuration/` has the correct `RemindAfter` date.

- [ ] **Skip Version:** Click Skip This Version, reopen app — confirm dialog does not reappear. Verify `update_prefs.json` has the correct `SkippedVersion`.

- [ ] **Final commit if any fixups were needed**

  ```
  git add -p
  git commit -m "fix: update check smoke test fixups"
  ```
