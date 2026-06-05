# Blackmagic & AJA Hardware Output Design

**Date:** 2026-06-05
**Scope:** Add Blackmagic DeckLink (full) and AJA NTV2 (stub) as output types in ShowCast.

---

## Background

`OutputType` already defines `AJA`, `Blackmagic`, and `BirdDog` enum values and `OutputConfig.DeviceSerial` is already present. Neither type is wired up. `BirdDog` is out of scope and will not be exposed in the UI.

---

## Scope & Cleanup

- `BirdDog` stays in the `OutputType` enum (no serialization break) but is removed from `OutputEditViewModel.TypeLabels/TypeValues` so it never appears in the config dialog.
- `OutputConfig.DeviceSerial` (existing field, currently unused) is repurposed as the device name for Blackmagic and AJA — serialized from the device dropdown selection.

---

## Approach: Option B

Blackmagic DeckLink is fully implemented (COM P/Invoke + `BlackmagicSender` background thread). AJA gets the full lifecycle skeleton but `AjaApi.TryInitialize()` always returns false — no video is ever sent. Both degrade gracefully if their SDK/driver is absent, matching the existing NDI pattern.

---

## 1. DeckLink COM Wrapper (`Blackmagic/DeckLinkApi.cs`)

### COM Interfaces

```
IDeckLinkIterator   — CoCreateInstance entry; Next() → IDeckLink
IDeckLink           — one card; GetDisplayName() → string; QI → IDeckLinkOutput
IDeckLinkOutput     — EnableVideoOutput / CreateVideoFrame / DisplayVideoFrameSync / DisableVideoOutput
IDeckLinkMutableVideoFrame — GetBytes() → IntPtr (BGRA pixel buffer)
```

All decorated with `[ComImport]`, `[Guid(...)]`, `[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]`. Each method returns `int` (HRESULT); callers use `Marshal.ThrowExceptionForHR`.

The exact interface GUIDs are taken from `DeckLinkAPI_i.c` in the DeckLink SDK. Key values:

- `CLSID_CDeckLinkIterator`: `{36A5F770-PE4C-11D5-A0E8-00A024CA8EB1}` *(verify against SDK headers on first SDK-available build)*
- Pixel format: `bmdFormat8BitBGRA = 0x42475241` — matches existing BGRA pipeline, no conversion needed
- Frame flags: `bmdFrameFlagDefault = 0`
- Video output flags: `bmdVideoOutputFlagDefault = 0`

### Display Mode Lookup

A `static readonly Dictionary<(int w, int h, int fpsRounded), int>` maps common resolutions to `bmdMode*` constants (1080p23.976 through 2160p60). Unrecognized combinations log a warning and fall back to `bmdModeHD1080p5994`.

### Static `DeckLinkApi` class

```
bool TryInitialize()     — CoCreateInstance(CLSID_CDeckLinkIterator), catches COMException; sets IsAvailable
bool IsAvailable         — true after successful TryInitialize
List<string> EnumerateDevices()  — walks IDeckLinkIterator, returns display names
```

`TryInitialize()` is called once at app startup alongside `NDIlib.TryInitialize()`.

---

## 2. `BlackmagicSender` (`Core/BlackmagicSender.cs`)

Mirrors `NdiSender` in structure and lifecycle.

### Constructor

1. Match `Config.DeviceSerial` against `DeckLinkApi.EnumerateDevices()` by name; fall back to device index 0 if not found (logs warning).
2. QI `IDeckLink` → `IDeckLinkOutput`.
3. Call `EnableVideoOutput(displayMode, flags)` using the display mode lookup.
4. Call `CreateVideoFrame(w, h, stride, bmdFormat8BitBGRA, bmdFrameFlagDefault)` → store as `_frame`.
5. Allocate `byte[] _buffer`, pin with `GCHandle`.
6. Set `output.VideoRegistry = new VideoFrameRegistry(...)`.
7. Start background thread `"DeckLink:{name}"`.

### Send Loop (background thread)

```
DetectPageChange()     — identical to NdiSender
RenderFrame()          — identical to NdiSender (renders into _buffer)
GetBytes(_frame)       — get IDeckLinkMutableVideoFrame pixel pointer
Marshal.Copy(_buffer → frame pointer)
DisplayVideoFrameSync(_frame)   — blocks for frame pacing (replaces NDI's clock_video sleep)
```

On exception: log and `Thread.Sleep(33)`, same as `NdiSender`.

### Dispose

`_running = false` → `_thread.Join(250)` → `DisableVideoOutput()` → `Marshal.ReleaseComObject` on frame and output interfaces → clear `output.VideoRegistry` → `_pin.Free()`.

### Audio

Not implemented. DeckLink audio requires `IDeckLinkAudioOutput` (a separate QI). Left as `// TODO: DeckLink audio via IDeckLinkAudioOutput`.

---

## 3. AJA Stub (`Core/AjaSender.cs`)

```csharp
static class AjaApi
{
    public static bool IsAvailable => false;
    public static bool TryInitialize() { /* log */ return false; }
    public static List<string> EnumerateDevices() => [];
}

sealed class AjaSender : IDisposable
{
    public AjaSender(...) => throw new InvalidOperationException("AJA not available");
    public void Dispose() { }
}
```

A `Docs/aja-integration.md` note documents what the C wrapper needs to expose when NTV2 SDK integration is built.

---

## 4. UI — `OutputEditViewModel`

- `TypeLabels`: `{ "Display", "NDI", "Blackmagic", "AJA", "Preview" }`
- `TypeValues`: `{ Display, NDI, Blackmagic, AJA, Preview }`
- New computed properties: `IsBlackmagic`, `IsAja`
- `AvailableHardwareDevices` (`List<string>`) — shared; populated on type switch
- `HardwareDeviceIndex` (`int`) — index into `AvailableHardwareDevices`
- `LoadFrom`: maps `Config.DeviceSerial` → `HardwareDeviceIndex` by name match
- `WriteTo`: maps `HardwareDeviceIndex` → `Config.DeviceSerial` by name lookup

On type switch to Blackmagic: call `DeckLinkApi.EnumerateDevices()`. Empty list shows `"No DeckLink devices found"`.
On type switch to AJA: list is `["AJA not available"]` (IsAvailable always false).

---

## 5. UI — `ScreenConfigDialog`

The existing settings panel already conditionally shows fields based on output type (monitor picker for Display, NDI stream name for NDI). The same pattern gains:

- A "Device" `ComboBox` bound to `AvailableHardwareDevices` / `HardwareDeviceIndex`, visible when `IsBlackmagic || IsAja`.
- The NDI stream name field, monitor picker, and device picker are mutually exclusive (only one shown at a time).

---

## 6. `MainViewModel` Wiring

```
_blackmagicSenders: Dictionary<Guid, BlackmagicSender>
_ajaSenders:        Dictionary<Guid, AjaSender>          // always empty; guards on IsAvailable

StartBlackmagicFor(OutputState)    // guards: IsAvailable && Enabled && type == Blackmagic
StopBlackmagicFor(OutputState)
StopAllBlackmagicSenders()

StartAjaFor(OutputState)           // no-op; IsAvailable == false
StopAjaFor(OutputState)
StopAllAjaSenders()
```

`NotifyOutputConfigsChanged()` and the show-load path gain parallel Blackmagic/AJA blocks alongside the existing NDI block. `StopAll*` called on show close alongside `StopAllNdiSenders`.

---

## Files Changed

| File | Change |
|------|--------|
| `Blackmagic/DeckLinkApi.cs` | New — COM wrapper |
| `Core/BlackmagicSender.cs` | New — background thread sender |
| `Core/AjaSender.cs` | New — stub |
| `Docs/aja-integration.md` | New — C wrapper notes |
| `ViewModels/OutputEditViewModel.cs` | Add Blackmagic/AJA types + device picker fields |
| `Views/ScreenConfigDialog.axaml` | Add device dropdown |
| `Views/ScreenConfigDialog.axaml.cs` | Populate device list on type switch |
| `ViewModels/MainViewModel.cs` | Lifecycle wiring for both sender types |
| `App.axaml.cs` | Call DeckLinkApi.TryInitialize() and AjaApi.TryInitialize() at startup |

---

## Out of Scope

- BirdDog output (removed from feature scope)
- DeckLink audio output
- AJA NTV2 SDK native C wrapper
- NDI audio routing to/from hardware outputs
- Output preview for hardware types (VideoRegistry is set; preview bridge works as-is)
