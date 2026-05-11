# Splash Screen Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Show a branded 480×320 splash window with a real-steps progress bar while the app initializes, then swap it out for the main window.

**Architecture:** `SplashWindow` is a borderless Avalonia `Window` that fills its client area with `Assets/splash.png`. `App.axaml.cs` sets `SplashWindow` as `MainWindow`, then runs the startup sequence from `SplashWindow.Opened`. Progress is reported via `IProgress<(double, string)>` which calls a `Report` method directly on the splash window. No ViewModel, no ReactiveUI on the splash.

**Tech Stack:** Avalonia 11.2, C# 13, .NET 9, xUnit (existing test project at `ShowCast.Tests/`)

---

## File Map

| File | Action | Responsibility |
|------|--------|---------------|
| `Views/SplashWindow.axaml` | Create | Window layout — image fill + progress overlay |
| `Views/SplashWindow.axaml.cs` | Create | `Report(double, string)` method; starts startup sequence on `Opened` |
| `App.axaml.cs` | Modify | Replaced `OnFrameworkInitializationCompleted` with splash-first startup |

No `.csproj` changes needed — `Assets\**` is already an `AvaloniaResource` glob.

---

### Task 1: Create `SplashWindow.axaml`

**Files:**
- Create: `Views/SplashWindow.axaml`

- [ ] **Step 1: Create the AXAML file**

```xml
<Window xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml"
        x:Class="ShowCast.Views.SplashWindow"
        Width="480" Height="320"
        CanResize="False"
        ShowInTaskbar="False"
        SystemDecorations="None"
        WindowStartupLocation="CenterScreen"
        Background="#03327A">

    <Grid>

        <!-- Splash graphic fills entire window -->
        <Image Source="avares://ShowCast/Assets/splash.png"
               Stretch="Fill" />

        <!-- Progress overlay anchored to bottom -->
        <Border VerticalAlignment="Bottom" Height="64">
            <Border.Background>
                <LinearGradientBrush StartPoint="0%,0%" EndPoint="0%,100%">
                    <GradientStop Color="Transparent" Offset="0"/>
                    <GradientStop Color="#CC000A1E"   Offset="1"/>
                </LinearGradientBrush>
            </Border.Background>
            <StackPanel Margin="24,0,24,14" VerticalAlignment="Bottom">
                <ProgressBar x:Name="SplashProgressBar"
                             Minimum="0" Maximum="1" Value="0"
                             Height="4"
                             CornerRadius="2"
                             Foreground="White"
                             Background="#33FFFFFF" />
                <TextBlock x:Name="SplashStatusLabel"
                           Text="Starting..."
                           Foreground="#88FFFFFF"
                           FontSize="11"
                           Margin="0,5,0,0" />
            </StackPanel>
        </Border>

    </Grid>
</Window>
```

- [ ] **Step 2: Verify it compiles**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeds. If Avalonia complains about `CornerRadius` on `ProgressBar`, remove that property — it's accepted in 11.2 but falls back gracefully if not.

---

### Task 2: Create `SplashWindow.axaml.cs`

**Files:**
- Create: `Views/SplashWindow.axaml.cs`

- [ ] **Step 1: Create the code-behind**

```csharp
using System;
using Avalonia.Controls;

namespace ShowCast.Views;

public partial class SplashWindow : Window
{
    public SplashWindow() => InitializeComponent();

    public void Report(double value, string label)
    {
        SplashProgressBar.Value = value;
        SplashStatusLabel.Text  = label;
    }
}
```

`Report` is always called on the UI thread (via `Progress<T>`'s captured `SynchronizationContext`), so no dispatcher wrapping needed here.

- [ ] **Step 2: Build to confirm**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeds, no warnings about the partial class.

---

### Task 3: Modify `App.axaml.cs`

**Files:**
- Modify: `App.axaml.cs`

The current file is:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    ShowCast.Core.AppFolders.EnsureCreated();
    NdiAvailable = NewTek.NDIlib.TryInitialize();
    if (!NdiAvailable)
        System.Diagnostics.Debug.WriteLine(
            "[App] NDI library failed to initialize — NDI outputs will not function.");

    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        desktop.MainWindow = new MainWindow { DataContext = new MainViewModel() };
        desktop.Exit += (_, _) => { if (NdiAvailable) NewTek.NDIlib.destroy(); };
    }
    base.OnFrameworkInitializationCompleted();
}
```

- [ ] **Step 1: Replace `OnFrameworkInitializationCompleted` with the splash-first sequence**

Replace the entire method body with:

```csharp
public override void OnFrameworkInitializationCompleted()
{
    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
    {
        var splash = new SplashWindow();
        desktop.MainWindow = splash;

        splash.Opened += async (_, _) =>
        {
            var progress = new Progress<(double value, string label)>(
                p => splash.Report(p.value, p.label));
            var p = (IProgress<(double value, string label)>)progress;

            p.Report((0.25, "Creating app folders"));
            Core.AppFolders.EnsureCreated();

            p.Report((0.50, "Initializing NDI"));
            NdiAvailable = await Task.Run(() => NewTek.NDIlib.TryInitialize());
            if (!NdiAvailable)
                System.Diagnostics.Debug.WriteLine(
                    "[App] NDI library failed to initialize — NDI outputs will not function.");

            p.Report((0.75, "Preparing workspace"));
            var vm = new MainViewModel();

            p.Report((1.00, "Starting up"));

            var mainWindow = new MainWindow { DataContext = vm };
            desktop.Exit += (_, _) => { if (NdiAvailable) NewTek.NDIlib.destroy(); };
            desktop.MainWindow = mainWindow;
            mainWindow.Show();
            splash.Close();
        };
    }

    base.OnFrameworkInitializationCompleted();
}
```

Add `using System.Threading.Tasks;` to the top of the file if it isn't already there.

The full updated file should look like:

```csharp
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using ShowCast.ViewModels;
using ShowCast.Views;

namespace ShowCast;

public class App : Application
{
    /// <summary>False if the NDI runtime library failed to load at startup.</summary>
    public static bool NdiAvailable { get; private set; } = true;

    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var splash = new SplashWindow();
            desktop.MainWindow = splash;

            splash.Opened += async (_, _) =>
            {
                var progress = new Progress<(double value, string label)>(
                    p => splash.Report(p.value, p.label));
                var p = (IProgress<(double value, string label)>)progress;

                p.Report((0.25, "Creating app folders"));
                Core.AppFolders.EnsureCreated();

                p.Report((0.50, "Initializing NDI"));
                NdiAvailable = await Task.Run(() => NewTek.NDIlib.TryInitialize());
                if (!NdiAvailable)
                    System.Diagnostics.Debug.WriteLine(
                        "[App] NDI library failed to initialize — NDI outputs will not function.");

                p.Report((0.75, "Preparing workspace"));
                var vm = new MainViewModel();

                p.Report((1.00, "Starting up"));

                var mainWindow = new MainWindow { DataContext = vm };
                desktop.Exit += (_, _) => { if (NdiAvailable) NewTek.NDIlib.destroy(); };
                desktop.MainWindow = mainWindow;
                mainWindow.Show();
                splash.Close();
            };
        }

        base.OnFrameworkInitializationCompleted();
    }
}
```

- [ ] **Step 2: Build**

```
dotnet build ShowCast.csproj
```

Expected: Build succeeds with no errors.

---

### Task 4: Smoke Test

- [ ] **Step 1: Run the app**

```
dotnet run --project ShowCast.csproj
```

Expected:
- Splash appears centered on screen at 480×320
- Image fills the window
- Progress bar advances through four steps (folders → NDI → workspace → starting up)
- Main window appears, splash closes
- App is fully functional (no regressions in show loading, page grid, outputs)

- [ ] **Step 2: Check edge case — NDI unavailable**

Temporarily rename `Processing.NDI.Lib.x64.dll` (or unplug network), rerun. Expected: splash still completes, main window opens, NDI warning in debug output. Restore the DLL afterward.

---

### Task 5: Commit

- [ ] **Step 1: Stage and commit**

```bash
git add Views/SplashWindow.axaml Views/SplashWindow.axaml.cs App.axaml.cs
git commit -m "feat: add splash screen with real-steps progress bar"
```
