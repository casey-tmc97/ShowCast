# Splash Screen Design

**Date:** 2026-05-11
**Status:** Approved

## Overview

Show a branded splash screen with a real-steps progress bar while the app initializes. The splash appears immediately on launch, tracks four sequential startup tasks, then closes and reveals the main window.

## Visual Design

- **Window size:** 480 × 320 px, no system decorations (`SystemDecorations="None"`)
- **Background:** `Assets/splash.png` fills the entire window (the branded landscape graphic)
- **Progress bar:** 4px tall, white, rounded corners, on a semi-transparent white track — anchored to the bottom of the window
- **Status label:** Small white text below the bar (e.g. "Initializing NDI...")
- **Overlay:** Dark-to-transparent gradient `Border` behind the bar/label so text is legible over the image

## Startup Steps

Four real steps reported in sequence:

| Step | Value | Label |
|------|-------|-------|
| 1/4 | 0.25 | Creating app folders |
| 2/4 | 0.50 | Initializing NDI |
| 3/4 | 0.75 | Loading session |
| 4/4 | 1.00 | Starting up |

## Architecture

**Approach: Progress callback**

`App.OnFrameworkInitializationCompleted` drives the sequence. A `Progress<(double value, string label)>` instance marshals updates to the UI thread. No ViewModel — `SplashWindow` exposes two plain properties (`Progress` and `StatusText`) set directly from the callback.

### New file: `Views/SplashWindow.axaml` + `.axaml.cs`

- Borderless `Window`, 480×320, `CanResize="False"`, `ShowInTaskbar="False"`, `WindowStartupLocation="CenterScreen"`
- `Image` control stretches `splash.png` to fill
- `Grid` overlay at the bottom with a gradient `Border`, a `ProgressBar`, and a `TextBlock`
- Two properties: `Progress` (double 0–1) and `StatusText` (string), set directly — no bindings needed beyond initial wiring

### Modified: `App.axaml.cs`

```
OnFrameworkInitializationCompleted:
  1. Create SplashWindow, set as desktop.MainWindow
     (Avalonia shows it automatically after this method returns)
  2. Subscribe to SplashWindow.Opened — kick off async startup there
     so the window is on screen before work begins:
       - Report 0.25 / "Creating app folders" → AppFolders.EnsureCreated()
       - Report 0.50 / "Initializing NDI"     → Task.Run(() => NDIlib.TryInitialize())
       - Report 0.75 / "Loading session"       → Task.Run(() => new MainViewModel())
       - Report 1.00 / "Starting up"
  3. Back on UI thread: create MainWindow with the ready MainViewModel
  4. Set desktop.MainWindow = MainWindow
  5. MainWindow.Show(), SplashWindow.Close()
```

The heavy work (`TryInitialize`, `MainViewModel` constructor) runs on a background thread via `Task.Run`; progress reports are dispatched to the UI thread via the `Progress<T>` callback. The startup sequence is fired from `SplashWindow.Opened` so the splash is guaranteed visible before any work begins.

## Files Changed

| File | Change |
|------|--------|
| `Views/SplashWindow.axaml` | New |
| `Views/SplashWindow.axaml.cs` | New |
| `App.axaml.cs` | Modified — async startup sequence |
| `ShowCast.csproj` | Add `splash.png` as `AvaloniaResource` if not already included |
