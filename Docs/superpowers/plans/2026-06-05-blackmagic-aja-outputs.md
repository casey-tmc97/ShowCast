# Blackmagic & AJA Hardware Outputs Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add Blackmagic DeckLink (full COM P/Invoke sender) and AJA NTV2 (graceful stub) as output types, mirroring the existing NDI sender pattern.

**Architecture:** Each hardware type gets a static API class (`DeckLinkApi` / `AjaApi`) for init and device enumeration, and a sender class (`BlackmagicSender` / `AjaSender`) that runs a background render loop. `MainViewModel` manages sender lifecycles identically to how it manages `NdiSender`. `OutputEditViewModel` gains two new type entries and a shared device-picker property pair.

**Tech Stack:** C# COM P/Invoke (`[ComImport]`), SkiaSharp (render), xUnit (tests). DeckLink SDK GUIDs are taken from `DeckLinkAPI_i.c` in the SDK — comments flag any that need re-verification once the SDK is installed.

---

## File Map

| File | Action | Responsibility |
|------|--------|----------------|
| `Blackmagic/DeckLinkApi.cs` | Create | COM interfaces + static init/enumerate |
| `Core/BlackmagicSender.cs` | Create | Background thread DeckLink sender |
| `Core/AjaSender.cs` | Create | AJA stub (always unavailable) |
| `Docs/aja-integration.md` | Create | Notes for future NTV2 C wrapper |
| `ShowCast.Tests/Blackmagic/DeckLinkApiTests.cs` | Create | DeckLinkApi graceful-fail tests |
| `ShowCast.Tests/Core/AjaSenderTests.cs` | Create | AjaApi always-false tests |
| `ViewModels/OutputEditViewModel.cs` | Modify | New types + device picker properties |
| `ShowCast.Tests/ViewModels/OutputEditViewModelHardwareTests.cs` | Create | ViewModel hardware-type tests |
| `Views/ScreenConfigDialog.axaml` | Modify | Add type items + device dropdown panel |
| `Views/ScreenConfigDialog.axaml.cs` | Modify | Populate device list on type/selection change |
| `ViewModels/MainViewModel.cs` | Modify | Sender lifecycle (Blackmagic + AJA) |
| `ShowCast.Tests/ViewModels/MainViewModelHardwareOutputTests.cs` | Create | Lifecycle guard tests |
| `App.axaml.cs` | Modify | Call TryInitialize for both APIs at startup |

---

## Task 1: AJA Stub + Doc

**Files:**
- Create: `Core/AjaSender.cs`
- Create: `Docs/aja-integration.md`
- Create: `ShowCast.Tests/Core/AjaSenderTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// ShowCast.Tests/Core/AjaSenderTests.cs
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class AjaSenderTests
{
    [Fact]
    public void AjaApi_TryInitialize_AlwaysReturnsFalse()
    {
        Assert.False(AjaApi.TryInitialize());
    }

    [Fact]
    public void AjaApi_IsAvailable_AlwaysFalse()
    {
        AjaApi.TryInitialize();
        Assert.False(AjaApi.IsAvailable);
    }

    [Fact]
    public void AjaApi_EnumerateDevices_ReturnsEmpty()
    {
        Assert.Empty(AjaApi.EnumerateDevices());
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```
dotnet test ShowCast.Tests --filter "AjaSenderTests" -v minimal
```
Expected: build error — `AjaApi` not found.

- [ ] **Step 3: Create `Core/AjaSender.cs`**

```csharp
using System;
using System.Collections.Generic;

namespace ShowCast.Core;

public static class AjaApi
{
    public static bool IsAvailable => false;

    public static bool TryInitialize()
    {
        Console.Error.WriteLine(
            "[AJA] NTV2 SDK not available — AJA output disabled. " +
            "(NTV2 requires a native C wrapper; see Docs/aja-integration.md)");
        return false;
    }

    public static List<string> EnumerateDevices() => [];
}

public sealed class AjaSender : IDisposable
{
    public AjaSender(OutputState output)
        => throw new InvalidOperationException("AJA not available — check AjaApi.IsAvailable before constructing.");

    public void Dispose() { }
}
```

- [ ] **Step 4: Create `Docs/aja-integration.md`**

```markdown
# AJA NTV2 Integration Notes

AJA hardware output is stubbed in `Core/AjaSender.cs`. To implement:

## Why a C Wrapper Is Required

The AJA NTV2 SDK exposes a C++ class library (`CNTV2Card`, etc.) with no flat C API.
C# cannot P/Invoke into C++ class methods directly. A thin native adapter DLL is needed.

## What the Adapter Must Expose (flat C API)

```c
// ShowCastAjaAdapter.dll
bool  aja_initialize();
void  aja_shutdown();
int   aja_device_count();
void  aja_device_name(int index, char* buf, int bufLen);
void* aja_open(int deviceIndex, int width, int height, int fpsN, int fpsD);
void  aja_close(void* handle);
int   aja_submit_frame(void* handle, const uint8_t* bgraPixels, int byteCount);
```

## Build Steps (when SDK is available)

1. Install AJA NTV2 SDK.
2. Create a C++ DLL project (`ShowCastAjaAdapter`) referencing NTV2 headers.
3. Implement the flat API above using `CNTV2Card` and `NTV2FormatDesc`.
4. Replace `AjaApi.TryInitialize()` stub with a P/Invoke loader matching the NDIlib pattern.
5. Implement `AjaSender` mirroring `BlackmagicSender`.
```

- [ ] **Step 5: Run tests — they should pass**

```
dotnet test ShowCast.Tests --filter "AjaSenderTests" -v minimal
```
Expected: 3 tests pass.

- [ ] **Step 6: Commit**

```
git add Core/AjaSender.cs Docs/aja-integration.md ShowCast.Tests/Core/AjaSenderTests.cs
git commit -m "feat: add AJA stub output type with integration notes"
```

---

## Task 2: DeckLink COM Wrapper

**Files:**
- Create: `Blackmagic/DeckLinkApi.cs`
- Create: `ShowCast.Tests/Blackmagic/DeckLinkApiTests.cs`

- [ ] **Step 1: Write the failing tests**

```csharp
// ShowCast.Tests/Blackmagic/DeckLinkApiTests.cs
using ShowCast.Blackmagic;
using Xunit;

namespace ShowCast.Tests.Blackmagic;

public class DeckLinkApiTests
{
    [Fact]
    public void TryInitialize_DoesNotThrow()
    {
        // Must not throw whether or not the DeckLink driver is installed.
        var ex = Record.Exception(() => DeckLinkApi.TryInitialize());
        Assert.Null(ex);
    }

    [Fact]
    public void EnumerateDevices_ReturnsEmptyWhenUnavailable()
    {
        DeckLinkApi.TryInitialize();
        if (!DeckLinkApi.IsAvailable)
            Assert.Empty(DeckLinkApi.EnumerateDevices());
        // If driver IS available this test is a no-op (result may be non-empty).
    }

    [Fact]
    public void GetDisplayMode_KnownResolution_ReturnsNonZero()
    {
        int mode = DeckLinkApi.GetDisplayMode(1920, 1080, 59.94);
        Assert.NotEqual(0, mode);
    }

    [Fact]
    public void GetDisplayMode_UnknownResolution_ReturnsFallback()
    {
        // 800x600 is not a broadcast mode — must return the 1080p59.94 fallback.
        int mode = DeckLinkApi.GetDisplayMode(800, 600, 30.0);
        Assert.Equal(0x48703539, mode); // bmdModeHD1080p5994
    }
}
```

- [ ] **Step 2: Run to confirm build failure**

```
dotnet test ShowCast.Tests --filter "DeckLinkApiTests" -v minimal
```
Expected: build error — `ShowCast.Blackmagic` not found.

- [ ] **Step 3: Create `Blackmagic/DeckLinkApi.cs`**

```csharp
// IMPORTANT: COM interface GUIDs are from DeckLink SDK 12 DeckLinkAPI_i.c.
// Verify against the installed SDK headers before testing with hardware.
// Method vtable order must match the SDK IDL exactly.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ShowCast.Blackmagic;

// ── CoClass ──────────────────────────────────────────────────────────────────

[ComImport, Guid("36A5F770-004C-11D5-A0E8-00A024CA8EB1")]
class CDeckLinkIteratorClass { }

// ── COM Interfaces ───────────────────────────────────────────────────────────

[ComImport, Guid("7DBBBB11-5B7B-467D-AEA4-CEA468FD368C"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeckLinkIterator
{
    [PreserveSig] int Next([MarshalAs(UnmanagedType.Interface)] out IDeckLink deckLinkInstance);
}

[ComImport, Guid("C418FBDD-0587-48ED-8FE5-640F0A14AF91"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeckLink
{
    [PreserveSig] int GetModelName([MarshalAs(UnmanagedType.BStr)] out string modelName);
    [PreserveSig] int GetDisplayName([MarshalAs(UnmanagedType.BStr)] out string displayName);
}

[ComImport, Guid("3F716FE0-F023-4111-BE5D-EF4414C05B17"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeckLinkVideoFrame
{
    // Vtable order matches IDeckLinkVideoFrame in SDK IDL.
    [PreserveSig] int GetWidth();
    [PreserveSig] int GetHeight();
    [PreserveSig] int GetRowBytes();
    [PreserveSig] int GetPixelFormat();
    [PreserveSig] int GetFlags();
    [PreserveSig] int GetBytes(out IntPtr buffer);
    [PreserveSig] int GetTimecode(int format, out IntPtr timecode);
    [PreserveSig] int GetAncillaryData(out IntPtr ancillary);
}

[ComImport, Guid("69E2639F-40DA-4E19-B6F2-20ACE815C390"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeckLinkMutableVideoFrame
{
    // IDeckLinkVideoFrame base methods must come first in vtable order.
    [PreserveSig] int GetWidth();
    [PreserveSig] int GetHeight();
    [PreserveSig] int GetRowBytes();
    [PreserveSig] int GetPixelFormat();
    [PreserveSig] int GetFlags();
    [PreserveSig] int GetBytes(out IntPtr buffer);
    [PreserveSig] int GetTimecode(int format, out IntPtr timecode);
    [PreserveSig] int GetAncillaryData(out IntPtr ancillary);
    // IDeckLinkMutableVideoFrame-specific methods.
    [PreserveSig] int SetFlags(int newFlags);
    [PreserveSig] int SetTimecode(int format, IntPtr timecode);
    [PreserveSig] int SetTimecodeFromComponents(int format, uint hours, uint minutes, uint secs, uint frames, int flags);
    [PreserveSig] int SetAncillaryData(IntPtr ancillary);
    [PreserveSig] int SetTimecodeUserBits(int format, uint userBits);
}

[ComImport, Guid("CC5C8A6E-3F2F-4B3A-87EA-FD78AF300564"),
 InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
interface IDeckLinkOutput
{
    // Vtable order matches IDeckLinkOutput in DeckLink SDK 12 IDL.
    [PreserveSig] int DoesSupportVideoMode(int connection, int requestedMode, int pixelFormat, int conversionMode, int flags, out int actualMode, [MarshalAs(UnmanagedType.Bool)] out bool isSupported);
    [PreserveSig] int GetDisplayModeIterator(out IntPtr iterator);
    [PreserveSig] int SetScreenPreviewCallback(IntPtr previewCallback);
    [PreserveSig] int EnableVideoOutput(int displayMode, int flags);
    [PreserveSig] int EnableAudioOutput(int audioSampleRate, int audioSampleType, uint audioChannelCount, int streamType);
    [PreserveSig] int DisableVideoOutput();
    [PreserveSig] int DisableAudioOutput();
    [PreserveSig] int GetDisplayMode(int displayMode, out IntPtr iterator);
    [PreserveSig] int CreateVideoFrame(int width, int height, int rowBytes, int pixelFormat, int flags,
        [MarshalAs(UnmanagedType.Interface)] out IDeckLinkMutableVideoFrame outFrame);
    [PreserveSig] int CreateAncillaryData(int pixelFormat, out IntPtr outBuffer);
    [PreserveSig] int DisplayVideoFrameSync([MarshalAs(UnmanagedType.Interface)] IDeckLinkVideoFrame theFrame);
}

// ── Static API ───────────────────────────────────────────────────────────────

public static class DeckLinkApi
{
    public const int PixelFormat_8BitBGRA = 0x42475241; // 'BGRA'

    // Display mode 4CC constants (BMDDisplayMode, big-endian uint32).
    // Verify against DeckLinkAPI.idl if new resolutions are needed.
    static readonly Dictionary<(int w, int h, int fpsX1000), int> _modes = new()
    {
        { (1920, 1080, 23976), 0x32337073 }, // bmdModeHD1080p2398 '23ps'
        { (1920, 1080, 24000), 0x32347073 }, // bmdModeHD1080p24   '24ps'
        { (1920, 1080, 25000), 0x48703235 }, // bmdModeHD1080p25   'Hp25'
        { (1920, 1080, 29970), 0x48703239 }, // bmdModeHD1080p2997 'Hp29'
        { (1920, 1080, 30000), 0x48703330 }, // bmdModeHD1080p30   'Hp30'
        { (1920, 1080, 50000), 0x48703530 }, // bmdModeHD1080p50   'Hp50'
        { (1920, 1080, 59940), 0x48703539 }, // bmdModeHD1080p5994 'Hp59'
        { (1920, 1080, 60000), 0x48703630 }, // bmdModeHD1080p60   'Hp60'
        { (1280,  720, 50000), 0x68703530 }, // bmdModeHD720p50    'hp50'
        { (1280,  720, 59940), 0x68703539 }, // bmdModeHD720p5994  'hp59'
        { (1280,  720, 60000), 0x68703630 }, // bmdModeHD720p60    'hp60'
        { (3840, 2160, 23976), 0x346B3233 }, // bmdMode4K2160p2398 '4k23'
        { (3840, 2160, 24000), 0x346B3234 }, // bmdMode4K2160p24   '4k24'
        { (3840, 2160, 25000), 0x346B3235 }, // bmdMode4K2160p25   '4k25'
        { (3840, 2160, 29970), 0x346B3239 }, // bmdMode4K2160p2997 '4k29'
        { (3840, 2160, 30000), 0x346B3330 }, // bmdMode4K2160p30   '4k30'
        { (3840, 2160, 50000), 0x346B3530 }, // bmdMode4K2160p50   '4k50'
        { (3840, 2160, 59940), 0x346B3539 }, // bmdMode4K2160p5994 '4k59'
        { (3840, 2160, 60000), 0x346B3630 }, // bmdMode4K2160p60   '4k60'
    };

    static bool? _available;
    public static bool IsAvailable => _available ?? false;

    public static bool TryInitialize()
    {
        try
        {
            var iter = (IDeckLinkIterator)new CDeckLinkIteratorClass();
            Marshal.ReleaseComObject(iter);
            _available = true;
            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(
                $"[DeckLink] Driver not found — Blackmagic output disabled. ({ex.Message})");
            _available = false;
            return false;
        }
    }

    public static List<string> EnumerateDevices()
    {
        if (!IsAvailable) return [];
        var result = new List<string>();
        try
        {
            var iter = (IDeckLinkIterator)new CDeckLinkIteratorClass();
            while (iter.Next(out var device) == 0)
            {
                device.GetDisplayName(out string name);
                result.Add(name);
                Marshal.ReleaseComObject(device);
            }
            Marshal.ReleaseComObject(iter);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[DeckLink] EnumerateDevices failed: {ex.Message}");
        }
        return result;
    }

    public static int GetDisplayMode(int w, int h, double fps)
    {
        int key = (int)Math.Round(fps * 1000);
        if (_modes.TryGetValue((w, h, key), out int mode)) return mode;
        Console.Error.WriteLine(
            $"[DeckLink] No mode for {w}x{h}@{fps} — falling back to 1080p59.94");
        return 0x48703539; // bmdModeHD1080p5994
    }
}
```

- [ ] **Step 4: Run tests — they should pass**

```
dotnet test ShowCast.Tests --filter "DeckLinkApiTests" -v minimal
```
Expected: 4 tests pass (driver absent on dev machine — `TryInitialize_DoesNotThrow` passes, `EnumerateDevices_ReturnsEmptyWhenUnavailable` passes because IsAvailable=false, display mode tests pass).

- [ ] **Step 5: Commit**

```
git add Blackmagic/DeckLinkApi.cs ShowCast.Tests/Blackmagic/DeckLinkApiTests.cs
git commit -m "feat: add DeckLink COM wrapper with graceful fallback"
```

---

## Task 3: BlackmagicSender

**Files:**
- Create: `Core/BlackmagicSender.cs`

No unit tests for the send loop (requires hardware). The guard — "no sender created when unavailable" — is tested in Task 7 (MainViewModel level).

- [ ] **Step 1: Create `Core/BlackmagicSender.cs`**

```csharp
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ShowCast.Blackmagic;
using ShowCast.Engine;
using SkiaSharp;

namespace ShowCast.Core;

/// <summary>
/// Owns one DeckLink output instance and streams the live page on a background thread.
/// DisplayVideoFrameSync blocks until the frame is displayed, providing frame pacing.
/// </summary>
public sealed class BlackmagicSender : IDisposable
{
    readonly OutputState             _output;
    readonly int                     _w, _h, _stride;
    readonly byte[]                  _buffer;
    readonly GCHandle                _pin;
    readonly VideoFrameRegistry      _videoRegistry;

    IDeckLinkOutput?             _deckLinkOutput;
    IDeckLinkMutableVideoFrame?  _frame;
    Thread?                      _thread;

    volatile bool _running = true;

    // Transition + animation state (background-thread-only)
    Page?    _prevLive;
    Page?    _fromPage;
    DateTime _transStartTime;
    DateTime _pageStartTime;

    public BlackmagicSender(OutputState output, IReadOnlyList<AudioDestination> audioDestinations,
                            Func<string, NdiSender?>? ndiLookup = null)
    {
        _output  = output;
        _w       = output.Config.Width;
        _h       = output.Config.Height;
        _stride  = _w * 4;
        _buffer  = new byte[_stride * _h];
        _pin     = GCHandle.Alloc(_buffer, GCHandleType.Pinned);
        _videoRegistry = new VideoFrameRegistry(audioDestinations, ndiLookup: ndiLookup);
        output.VideoRegistry = _videoRegistry;

        if (!DeckLinkApi.IsAvailable) return;

        // Resolve device by name; fall back to first device if serial not found.
        var devices = DeckLinkApi.EnumerateDevices();
        int deviceIndex = devices.IndexOf(output.Config.DeviceSerial);
        if (deviceIndex < 0 && devices.Count > 0) deviceIndex = 0;
        if (deviceIndex < 0)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] No DeckLink device found.");
            return;
        }

        // Walk the iterator to the chosen device.
        var iter  = (IDeckLinkIterator)new CDeckLinkIteratorClass();
        IDeckLink? card = null;
        for (int i = 0; iter.Next(out var dev) == 0; i++)
        {
            if (i == deviceIndex) { card = dev; break; }
            Marshal.ReleaseComObject(dev);
        }
        Marshal.ReleaseComObject(iter);

        if (card is null)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] Could not acquire device.");
            return;
        }

        _deckLinkOutput = card as IDeckLinkOutput;
        Marshal.ReleaseComObject(card);

        if (_deckLinkOutput is null)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] Device has no output capability.");
            return;
        }

        int displayMode = DeckLinkApi.GetDisplayMode(_w, _h, output.Config.FrameRate);
        int hr = _deckLinkOutput.EnableVideoOutput(displayMode, 0 /* bmdVideoOutputFlagDefault */);
        if (hr < 0)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] EnableVideoOutput failed: 0x{hr:X8}");
            Marshal.ReleaseComObject(_deckLinkOutput);
            _deckLinkOutput = null;
            return;
        }

        hr = _deckLinkOutput.CreateVideoFrame(_w, _h, _stride, DeckLinkApi.PixelFormat_8BitBGRA, 0, out _frame);
        if (hr < 0)
        {
            Console.Error.WriteLine($"[DeckLink:{output.Config.Name}] CreateVideoFrame failed: 0x{hr:X8}");
            _deckLinkOutput.DisableVideoOutput();
            Marshal.ReleaseComObject(_deckLinkOutput);
            _deckLinkOutput = null;
            return;
        }

        _thread = new Thread(SendLoop)
        {
            Name         = $"DeckLink:{output.Config.Name}",
            IsBackground = true
        };
        _thread.Start();
    }

    // ── Send loop (background thread) ─────────────────────────────────────────

    void SendLoop()
    {
        if (_deckLinkOutput is null || _frame is null) return;

        while (_running)
        {
            try
            {
                DetectPageChange();
                RenderFrame();

                _frame.GetBytes(out IntPtr ptr);
                Marshal.Copy(_buffer, 0, ptr, _buffer.Length);

                // Cast triggers COM QI → IDeckLinkVideoFrame vtable pointer.
                _deckLinkOutput.DisplayVideoFrameSync((IDeckLinkVideoFrame)_frame);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"[DeckLink:{_output.Config.Name}] frame error: {ex.GetType().Name}: {ex.Message}");
                Thread.Sleep(33);
            }
        }
    }

    void DetectPageChange()
    {
        var currentLive = _output.LivePage;
        if (currentLive == _prevLive) return;

        bool skipAnims = _output.PendingSkipEntryAnimations;
        bool hasTransition = !skipAnims
                          && _prevLive is not null && currentLive is not null
                          && _output.PendingTransitionType != TransitionType.Cut
                          && _output.PendingTransitionDuration > 0;

        _fromPage      = hasTransition ? _prevLive : null;
        _pageStartTime = skipAnims ? DateTime.UtcNow.AddSeconds(-10) : DateTime.UtcNow;
        if (hasTransition) _transStartTime = DateTime.UtcNow;
        _prevLive = currentLive;
        _videoRegistry.UpdateSlide(currentLive);
    }

    void RenderFrame()
    {
        var info = new SKImageInfo(_w, _h, SKColorType.Bgra8888);

        if (_fromPage is not null && _output.LivePage is not null)
        {
            double trans = (DateTime.UtcNow - _transStartTime).TotalMilliseconds;
            float  prog  = _output.PendingTransitionDuration > 0
                ? (float)(trans / _output.PendingTransitionDuration) : 1f;

            if (prog < 1f)
            {
                using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
                TransitionCompositor.Composite(surface.Canvas, _fromPage, _output.LivePage,
                    _output.Roles, _output.PendingTransitionType,
                    prog, _output.PendingTransitionEasing, _w, _h, trans);
                return;
            }
            _fromPage      = null;
            _pageStartTime = DateTime.UtcNow;
        }

        if (_output.LivePage is { } page)
        {
            double elapsed = (DateTime.UtcNow - _pageStartTime).TotalMilliseconds;
            using var surface = SKSurface.Create(info, _pin.AddrOfPinnedObject(), _stride);
            PageRenderer.Render(surface.Canvas, page, _output.Roles, _w, _h, elapsed,
                                getVideoFrame: _videoRegistry.TryGetFrame);
        }
        else
        {
            Array.Clear(_buffer);
        }
    }

    // ── Disposal ──────────────────────────────────────────────────────────────

    public void Dispose()
    {
        _running = false;
        _thread?.Join(250);

        if (_deckLinkOutput is not null)
        {
            try { _deckLinkOutput.DisableVideoOutput(); } catch { }
            Marshal.ReleaseComObject(_deckLinkOutput);
            _deckLinkOutput = null;
        }
        if (_frame is not null)
        {
            Marshal.ReleaseComObject(_frame);
            _frame = null;
        }

        _output.VideoRegistry = null;
        _videoRegistry.Dispose();
        _pin.Free();
    }
}
```

Note: `CDeckLinkIteratorClass` is defined in `Blackmagic/DeckLinkApi.cs` and is visible to `Core/BlackmagicSender.cs` because it's in the same assembly.

- [ ] **Step 2: Build to confirm no compile errors**

```
dotnet build ShowCast.sln -v minimal
```
Expected: build succeeds with no errors.

- [ ] **Step 3: Commit**

```
git add Core/BlackmagicSender.cs
git commit -m "feat: add BlackmagicSender background thread DeckLink output"
```

---

## Task 4: App Startup

**Files:**
- Modify: `App.axaml.cs`

- [ ] **Step 1: Add TryInitialize calls to the splash startup sequence**

In `App.axaml.cs`, inside the `splash.Opened` handler, add two calls alongside the existing NDI init:

```csharp
// Replace the existing startup block (lines ~33-49) with:
progress.Report((0.25, "Creating app folders"));
Core.AppFolders.EnsureCreated();

progress.Report((0.40, "Initializing NDI"));
NdiAvailable = await Task.Run(() => NewTek.NDIlib.TryInitialize());
if (!NdiAvailable)
    System.Diagnostics.Debug.WriteLine(
        "[App] NDI library not found — NDI outputs disabled.");

progress.Report((0.55, "Initializing Blackmagic"));
BlackmagicAvailable = await Task.Run(() => Blackmagic.DeckLinkApi.TryInitialize());
if (!BlackmagicAvailable)
    System.Diagnostics.Debug.WriteLine(
        "[App] DeckLink driver not found — Blackmagic outputs disabled.");

progress.Report((0.65, "Initializing AJA"));
await Task.Run(() => Core.AjaApi.TryInitialize()); // always false; logs the message

progress.Report((0.80, "Preparing workspace"));
var vm = new MainViewModel();

progress.Report((1.00, "Starting up"));
```

Also add the `BlackmagicAvailable` property and update the `Exit` handler:

```csharp
public static bool NdiAvailable        { get; private set; } = true;
public static bool BlackmagicAvailable { get; private set; } = false;
```

The `Exit` handler doesn't need to change for Blackmagic (DeckLink has no global destroy call — COM objects are released per-sender).

- [ ] **Step 2: Build to confirm no errors**

```
dotnet build ShowCast.sln -v minimal
```

- [ ] **Step 3: Commit**

```
git add App.axaml.cs
git commit -m "feat: initialize DeckLink and AJA APIs at app startup"
```

---

## Task 5: OutputEditViewModel — Hardware Type Support

**Files:**
- Modify: `ViewModels/OutputEditViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/OutputEditViewModelHardwareTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// ShowCast.Tests/ViewModels/OutputEditViewModelHardwareTests.cs
using System;
using System.Collections.Generic;
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class OutputEditViewModelHardwareTests
{
    static int BlackmagicIndex => Array.IndexOf(OutputEditViewModel.TypeLabels, "Blackmagic");
    static int AjaIndex        => Array.IndexOf(OutputEditViewModel.TypeLabels, "AJA");

    [Fact]
    public void TypeLabels_ContainsBlackmagicAndAja()
    {
        Assert.Contains("Blackmagic", OutputEditViewModel.TypeLabels);
        Assert.Contains("AJA",        OutputEditViewModel.TypeLabels);
    }

    [Fact]
    public void IsBlackmagic_TrueWhenTypeIndexIsBlackmagic()
    {
        var vm = new OutputEditViewModel();
        vm.TypeIndex = BlackmagicIndex;
        Assert.True(vm.IsBlackmagic);
        Assert.False(vm.IsAja);
        Assert.False(vm.IsDisplay);
        Assert.False(vm.IsNDI);
    }

    [Fact]
    public void IsAja_TrueWhenTypeIndexIsAja()
    {
        var vm = new OutputEditViewModel();
        vm.TypeIndex = AjaIndex;
        Assert.True(vm.IsAja);
        Assert.False(vm.IsBlackmagic);
    }

    [Fact]
    public void WriteTo_BlackmagicType_SetsDeviceSerialFromList()
    {
        var vm = new OutputEditViewModel();
        vm.TypeIndex = BlackmagicIndex;
        vm.AvailableHardwareDevices = new List<string> { "DeckLink 8K Pro", "DeckLink Duo 2" };
        vm.HardwareDeviceIndex = 1;
        var cfg = new OutputConfig();
        vm.WriteTo(cfg);
        Assert.Equal(OutputType.Blackmagic, cfg.Type);
        Assert.Equal("DeckLink Duo 2", cfg.DeviceSerial);
    }

    [Fact]
    public void LoadFrom_BlackmagicType_SetsHardwareDeviceIndexBySerial()
    {
        var vm = new OutputEditViewModel();
        vm.AvailableHardwareDevices = new List<string> { "DeckLink 8K Pro", "DeckLink Duo 2" };
        var cfg = new OutputConfig { Type = OutputType.Blackmagic, DeviceSerial = "DeckLink Duo 2" };
        vm.LoadFrom(cfg, 1);
        Assert.Equal(BlackmagicIndex, vm.TypeIndex);
        Assert.Equal(1, vm.HardwareDeviceIndex);
    }

    [Fact]
    public void LoadFrom_BlackmagicType_UnknownSerial_FallsBackToIndex0()
    {
        var vm = new OutputEditViewModel();
        vm.AvailableHardwareDevices = new List<string> { "DeckLink 8K Pro" };
        var cfg = new OutputConfig { Type = OutputType.Blackmagic, DeviceSerial = "NonExistent" };
        vm.LoadFrom(cfg, 1);
        Assert.Equal(0, vm.HardwareDeviceIndex);
    }
}
```

- [ ] **Step 2: Run to confirm build failure**

```
dotnet test ShowCast.Tests --filter "OutputEditViewModelHardwareTests" -v minimal
```
Expected: build error — `IsBlackmagic`, `IsAja`, `AvailableHardwareDevices`, `HardwareDeviceIndex` not found.

- [ ] **Step 3: Modify `ViewModels/OutputEditViewModel.cs`**

**3a.** Update `TypeLabels` and `TypeValues` (add Blackmagic + AJA before Preview):

```csharp
// Replace existing TypeLabels, TypeValues lines:
public static readonly string[] TypeLabels = { "Display", "NDI", "Blackmagic", "AJA", "Preview" };
static readonly OutputType[]    TypeValues = { OutputType.Display, OutputType.NDI, OutputType.Blackmagic, OutputType.AJA, OutputType.Preview };
```

**3b.** Update the `TypeIndex` setter to raise the two new properties:

```csharp
set
{
    this.RaiseAndSetIfChanged(ref _typeIndex, value);
    this.RaisePropertyChanged(nameof(IsDisplay));
    this.RaisePropertyChanged(nameof(IsNDI));
    this.RaisePropertyChanged(nameof(IsBlackmagic));
    this.RaisePropertyChanged(nameof(IsAja));
    if (IsNDI && string.IsNullOrWhiteSpace(NdiStreamName))
        NdiStreamName = AutoNdiName(Name);
}
```

**3c.** Update `IsDisplay` and `IsNDI`, add `IsBlackmagic` and `IsAja`:

```csharp
public bool IsDisplay    => TypeIndex == 0;
public bool IsNDI        => TypeIndex == 1;
public bool IsBlackmagic => TypeIndex == 2;
public bool IsAja        => TypeIndex == 3;
```

**3d.** Add the hardware device picker properties after the NDI section:

```csharp
// ── Hardware device (Blackmagic / AJA) ───────────────────────────────────────

private List<string> _availableHardwareDevices = new();
public List<string> AvailableHardwareDevices
{
    get => _availableHardwareDevices;
    set => this.RaiseAndSetIfChanged(ref _availableHardwareDevices, value);
}

private int _hardwareDeviceIndex;
public int HardwareDeviceIndex
{
    get => _hardwareDeviceIndex;
    set => this.RaiseAndSetIfChanged(ref _hardwareDeviceIndex, value);
}
```

**3e.** Update `LoadFrom` — add hardware device resolution after the existing field loads:

```csharp
// After: int fpsIdx = ...
// After: Fullscreen = ..., Enabled = ...

// Map DeviceSerial → HardwareDeviceIndex for hardware output types.
if (cfg.Type == OutputType.Blackmagic || cfg.Type == OutputType.AJA)
{
    int devIdx = AvailableHardwareDevices.IndexOf(cfg.DeviceSerial);
    HardwareDeviceIndex = devIdx >= 0 ? devIdx : 0;
}
```

**3f.** Update `WriteTo` — write DeviceSerial for hardware types:

```csharp
// After: cfg.Enabled = Enabled;
if ((cfg.Type == OutputType.Blackmagic || cfg.Type == OutputType.AJA)
    && HardwareDeviceIndex >= 0 && HardwareDeviceIndex < AvailableHardwareDevices.Count)
    cfg.DeviceSerial = AvailableHardwareDevices[HardwareDeviceIndex];
```

- [ ] **Step 4: Run tests — they should pass**

```
dotnet test ShowCast.Tests --filter "OutputEditViewModelHardwareTests" -v minimal
```
Expected: 6 tests pass.

- [ ] **Step 5: Commit**

```
git add ViewModels/OutputEditViewModel.cs ShowCast.Tests/ViewModels/OutputEditViewModelHardwareTests.cs
git commit -m "feat: add Blackmagic/AJA type options and device picker to OutputEditViewModel"
```

---

## Task 6: ScreenConfigDialog UI

**Files:**
- Modify: `Views/ScreenConfigDialog.axaml`
- Modify: `Views/ScreenConfigDialog.axaml.cs`

- [ ] **Step 1: Update `Views/ScreenConfigDialog.axaml`**

**1a.** Add Blackmagic and AJA items to the Type ComboBox (after NDI, before Preview):

```xml
<!-- Replace the existing Type ComboBox content: -->
<ComboBox SelectedIndex="{Binding TypeIndex}"
          HorizontalAlignment="Left" MinWidth="180"
          Background="#3a3a3a" Foreground="White">
    <ComboBoxItem Content="Display"/>
    <ComboBoxItem Content="NDI"/>
    <ComboBoxItem Content="Blackmagic"/>
    <ComboBoxItem Content="AJA"/>
    <ComboBoxItem Content="Preview"/>
</ComboBox>
```

**1b.** Add a hardware device panel after the NDI stream name panel (and before the Resolution panel):

```xml
<!-- ── Hardware Device (Blackmagic / AJA only) ── -->
<StackPanel Spacing="4">
    <StackPanel.IsVisible>
        <MultiBinding Converter="{x:Static BoolConverters.Or}">
            <Binding Path="IsBlackmagic"/>
            <Binding Path="IsAja"/>
        </MultiBinding>
    </StackPanel.IsVisible>
    <TextBlock Text="DEVICE" FontSize="10" FontWeight="Bold"
               Foreground="#888888" LetterSpacing="1"/>
    <ComboBox ItemsSource="{Binding AvailableHardwareDevices}"
              SelectedIndex="{Binding HardwareDeviceIndex}"
              HorizontalAlignment="Stretch"
              Background="#3a3a3a" Foreground="White"/>
</StackPanel>
```

- [ ] **Step 2: Update `Views/ScreenConfigDialog.axaml.cs`**

**2a.** Add `using ShowCast.Blackmagic;` to the top of `ScreenConfigDialog.axaml.cs` (alongside the existing using statements).

**2b.** Add a `RefreshHardwareDevices` helper that populates `AvailableHardwareDevices` based on the current or specified type, and a call in `OnOutputSelected`:

```csharp
// Replace the existing OnOutputSelected with:
void OnOutputSelected(object? sender, SelectionChangedEventArgs e)
{
    CommitCurrent();
    _current = OutputList.SelectedItem as OutputState;
    if (_current is null) return;
    RefreshHardwareDevicesFor(_current.Config.Type);
    _editVm.LoadFrom(_current.Config, _editVm.AvailableMonitors.Count);
}

void RefreshHardwareDevicesFor(OutputType type)
{
    if (type == OutputType.Blackmagic)
    {
        var devices = DeckLinkApi.EnumerateDevices();
        _editVm.AvailableHardwareDevices = devices.Count > 0
            ? devices
            : new System.Collections.Generic.List<string> { "No DeckLink devices found" };
    }
    else if (type == OutputType.AJA)
    {
        _editVm.AvailableHardwareDevices =
            new System.Collections.Generic.List<string> { "AJA not available" };
    }
    else
    {
        _editVm.AvailableHardwareDevices = new System.Collections.Generic.List<string>();
    }
}
```

**2c.** In `OnEditVmPropertyChanged`, add handling for `IsBlackmagic` and `IsAja` type switches:

```csharp
// Add these two cases alongside the existing IsDisplay case:
else if (e.PropertyName == nameof(OutputEditViewModel.IsBlackmagic) && _editVm.IsBlackmagic)
    RefreshHardwareDevicesFor(OutputType.Blackmagic);
else if (e.PropertyName == nameof(OutputEditViewModel.IsAja) && _editVm.IsAja)
    RefreshHardwareDevicesFor(OutputType.AJA);
```

- [ ] **Step 3: Build to confirm no errors**

```
dotnet build ShowCast.sln -v minimal
```

- [ ] **Step 4: Commit**

```
git add Views/ScreenConfigDialog.axaml Views/ScreenConfigDialog.axaml.cs
git commit -m "feat: add Blackmagic/AJA device picker to ScreenConfigDialog"
```

---

## Task 7: MainViewModel Sender Lifecycle

**Files:**
- Modify: `ViewModels/MainViewModel.cs`
- Create: `ShowCast.Tests/ViewModels/MainViewModelHardwareOutputTests.cs`

- [ ] **Step 1: Write failing tests**

```csharp
// ShowCast.Tests/ViewModels/MainViewModelHardwareOutputTests.cs
using ShowCast.Core;
using ShowCast.ViewModels;
using Xunit;

namespace ShowCast.Tests.ViewModels;

public class MainViewModelHardwareOutputTests
{
    [Fact]
    public void NotifyOutputConfigsChanged_BlackmagicOutputEnabled_DoesNotThrow()
    {
        var vm  = new MainViewModel();
        var cfg = new OutputConfig { Name = "DL Out", Type = OutputType.Blackmagic, Enabled = true };
        vm.ShowFile.AddOutput(cfg);
        vm.OutputStates.Add(new OutputState(cfg));

        var ex = Record.Exception(() => vm.NotifyOutputConfigsChanged());
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyOutputConfigsChanged_AjaOutputEnabled_DoesNotThrow()
    {
        var vm  = new MainViewModel();
        var cfg = new OutputConfig { Name = "AJA Out", Type = OutputType.AJA, Enabled = true };
        vm.ShowFile.AddOutput(cfg);
        vm.OutputStates.Add(new OutputState(cfg));

        var ex = Record.Exception(() => vm.NotifyOutputConfigsChanged());
        Assert.Null(ex);
    }

    [Fact]
    public void NotifyOutputConfigsChanged_BlackmagicOutputDisabled_DoesNotThrow()
    {
        var vm  = new MainViewModel();
        var cfg = new OutputConfig { Name = "DL Out", Type = OutputType.Blackmagic, Enabled = false };
        vm.ShowFile.AddOutput(cfg);
        vm.OutputStates.Add(new OutputState(cfg));

        var ex = Record.Exception(() => vm.NotifyOutputConfigsChanged());
        Assert.Null(ex);
    }
}
```

- [ ] **Step 2: Run to confirm the tests already pass (or fail)**

```
dotnet test ShowCast.Tests --filter "MainViewModelHardwareOutputTests" -v minimal
```
These may already pass if `NotifyOutputConfigsChanged` silently ignores unknown types. If they fail (exception thrown), that's the bug these tests catch — proceed to step 3.

- [ ] **Step 3: Modify `ViewModels/MainViewModel.cs`**

**3a.** Add the Blackmagic and AJA sender dictionaries alongside the NDI one:

```csharp
// After: readonly Dictionary<Guid, ShowCast.Core.NdiSender> _ndiSenders = new();
readonly Dictionary<Guid, ShowCast.Core.BlackmagicSender> _blackmagicSenders = new();
// AJA dict present for symmetry; always empty since AjaApi.IsAvailable == false.
readonly Dictionary<Guid, ShowCast.Core.AjaSender>        _ajaSenders        = new();
```

**3b.** Add Start/Stop methods for Blackmagic (after `StopAllNdiSenders`):

```csharp
void StartBlackmagicFor(OutputState o)
{
    if (o.Config.Type != OutputType.Blackmagic || !o.Config.Enabled) return;
    if (!ShowCast.Blackmagic.DeckLinkApi.IsAvailable) return;
    _blackmagicSenders[o.Config.Id] = new ShowCast.Core.BlackmagicSender(
        o, _showFile.Settings.AudioDestinations, FindNdiSender);
}

void StopBlackmagicFor(OutputState o)
{
    if (_blackmagicSenders.Remove(o.Config.Id, out var sender))
        sender.Dispose();
}

void StopAllBlackmagicSenders()
{
    foreach (var s in _blackmagicSenders.Values) s.Dispose();
    _blackmagicSenders.Clear();
}

void StartAjaFor(OutputState o)
{
    // AjaApi.IsAvailable is always false — this is intentionally a no-op.
    if (o.Config.Type != OutputType.AJA || !o.Config.Enabled) return;
    if (!ShowCast.Core.AjaApi.IsAvailable) return;
}

void StopAjaFor(OutputState o)
{
    if (_ajaSenders.Remove(o.Config.Id, out var sender))
        sender.Dispose();
}

void StopAllAjaSenders()
{
    foreach (var s in _ajaSenders.Values) s.Dispose();
    _ajaSenders.Clear();
}
```

**3c.** Update `NotifyOutputConfigsChanged` — add Blackmagic and AJA reconciliation after the NDI block:

```csharp
public void NotifyOutputConfigsChanged()
{
    // Reconcile NDI senders.
    foreach (var o in OutputStates)
    {
        bool hasSender = _ndiSenders.ContainsKey(o.Config.Id);
        if (o.Config.Type == OutputType.NDI && o.Config.Enabled && !hasSender)
            StartNdiFor(o);
        else if ((o.Config.Type != OutputType.NDI || !o.Config.Enabled) && hasSender)
            StopNdiFor(o);
    }

    // Reconcile Blackmagic senders.
    foreach (var o in OutputStates)
    {
        bool hasSender = _blackmagicSenders.ContainsKey(o.Config.Id);
        if (o.Config.Type == OutputType.Blackmagic && o.Config.Enabled && !hasSender)
            StartBlackmagicFor(o);
        else if ((o.Config.Type != OutputType.Blackmagic || !o.Config.Enabled) && hasSender)
            StopBlackmagicFor(o);
    }

    // Reconcile AJA senders (always no-op while AjaApi.IsAvailable == false).
    foreach (var o in OutputStates)
    {
        bool hasSender = _ajaSenders.ContainsKey(o.Config.Id);
        if (o.Config.Type == OutputType.AJA && o.Config.Enabled && !hasSender)
            StartAjaFor(o);
        else if ((o.Config.Type != OutputType.AJA || !o.Config.Enabled) && hasSender)
            StopAjaFor(o);
    }

    OutputConfigsChanged?.Invoke();
}
```

**3d.** Update the show-load path: find the loop that calls `StartNdiFor` for each output and add parallel calls:

```csharp
// In RebuildFromShowFile (or wherever StartNdiFor is called for each output):
foreach (var o in OutputStates)
    StartNdiFor(o);
foreach (var o in OutputStates)
    StartBlackmagicFor(o);
// AJA: StartAjaFor is always a no-op; omit for clarity.
```

**3e.** Update show-close: find `StopAllNdiSenders()` call and add:

```csharp
StopAllNdiSenders();
StopAllBlackmagicSenders();
StopAllAjaSenders();
```

- [ ] **Step 4: Run all tests**

```
dotnet test ShowCast.Tests -v minimal
```
Expected: all tests pass.

- [ ] **Step 5: Commit**

```
git add ViewModels/MainViewModel.cs ShowCast.Tests/ViewModels/MainViewModelHardwareOutputTests.cs
git commit -m "feat: wire Blackmagic/AJA sender lifecycle into MainViewModel"
```

---

## Self-Review Notes

- COM GUIDs in `DeckLinkApi.cs` are flagged for verification against `DeckLinkAPI_i.c` in the DeckLink SDK 12 package. If the driver is installed later and devices don't enumerate, this is the first thing to check.
- `CDeckLinkIteratorClass` is `internal` (default) to the assembly — it's only used in `DeckLinkApi.cs` and `BlackmagicSender.cs`, both in the same project. No access modifier issue.
- `IDeckLinkOutput.CreateVideoFrame` parameter order matches SDK IDL: `(width, height, rowBytes, pixelFormat, flags, outFrame)`.
- `DisplayVideoFrameSync` cast `(IDeckLinkVideoFrame)_frame` triggers COM QI at runtime; the DeckLink COM object implements both interfaces so QI will succeed.
- `AjaSender` constructor guards are correct: `MainViewModel.StartAjaFor` checks `AjaApi.IsAvailable` before constructing, so the `InvalidOperationException` in the stub constructor is never reached.
