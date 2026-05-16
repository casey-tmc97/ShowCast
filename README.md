# ShowCast

A cross-platform broadcast graphics and live presentation application for churches, conferences, concerts, and streaming studios.

ShowCast combines slide-based presentation control (like ProPresenter), a layer-based canvas editor (like Photoshop), and real-time compositing — in a single tool built for live production.

---

## Features

- **Slide presentation engine** — playlist and rundown management with keyboard-driven live control
- **Canvas editor** — layer-based slide design with text, images, shapes, and blend modes
- **Real-time transitions** — per-slide transition types (cut, fade, wipe, push, zoom) with configurable duration and easing
- **Layer animations** — per-layer entry/exit animations with delay and hold timing
- **NDI output** — stream any output as an NDI source on your local network
- **Multi-output routing** — independent Program, Confidence Monitor, Stage Display, and Overlay outputs
- **Rundowns** — ordered playlists with per-entry output assignment
- **Scheduled events** — time-based auto-fire with daily/weekly/monthly repeat
- **Timers** — countdown/up timers bindable to text layers with page-triggered start
- **Undo/redo** — full history in the editor and for page reordering

## Requirements

- Windows 10/11 (x64)
- [.NET 9 Runtime](https://dotnet.microsoft.com/en-us/download/dotnet/9.0)
- [NDI 6 Runtime](https://ndi.video/download-ndi-sdk/) *(optional — required only for NDI output)*

## Building from source

```bash
git clone https://github.com/casey-tmc97/ShowCast.git
cd ShowCast
dotnet build
dotnet run
```

Running the tests:

```bash
dotnet test ShowCast.Tests
```

## Tech stack

| Layer | Technology |
|---|---|
| UI framework | [Avalonia 11](https://avaloniaui.net/) |
| MVVM | ReactiveUI |
| 2D rendering | SkiaSharp |
| NDI streaming | NDI 6 SDK (NewTek/Vizrt) |
| Serialization | System.Text.Json |
| Target runtime | .NET 9 |

## Status

Active development — core presentation engine, editor, and NDI output are functional. Broadcast hardware output (AJA/Blackmagic), video playback, and scripting are planned for future milestones.

## License

TBD
