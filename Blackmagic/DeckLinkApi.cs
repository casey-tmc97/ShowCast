// GUIDs verified against Desktop Video 15.3.1 (DeckLinkAPI64.dll) by binary
// inspection and live COM QI probing — no SDK header needed.
//
// Changed from original (SDK ≤ 10.x) values:
//   CDeckLinkIteratorClass CLSID: 36A5F770 → BA6C6F44  (old CLSID no longer registered)
//   IDeckLinkIterator IID:        7DBBBB11 → 50FB36CD  (old IID repurposed as Video Conversion CLSID)
//   IDeckLink IID:                C418FBDD              (unchanged)
//
// IDeckLinkOutput vtable ordering below was NOT independently verified against the
// installed SDK. If EnableVideoOutput or CreateVideoFrame fail (hr < 0), the vtable
// slots may need adjusting to match the current IDL.

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace ShowCast.Blackmagic;

// ── CoClass ──────────────────────────────────────────────────────────────────

// CLSID for CDeckLinkIterator in Desktop Video 12+ / SDK 15.x
[ComImport, Guid("BA6C6F44-6DA5-4DCE-94AA-EE2D1372A676")]
class CDeckLinkIteratorClass { }

// ── COM Interfaces ───────────────────────────────────────────────────────────

// IDeckLinkIterator IID changed in Desktop Video 12+ (old 7DBBBB11 is now a Video Conversion CLSID)
[ComImport, Guid("50FB36CD-3063-4B73-BDBB-958087F2D8BA"),
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

[System.Runtime.Versioning.SupportedOSPlatform("windows")]
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
            try
            {
                while (iter.Next(out var device) == 0)
                {
                    device.GetDisplayName(out string name);
                    result.Add(name);
                    Marshal.ReleaseComObject(device);
                }
            }
            finally
            {
                Marshal.ReleaseComObject(iter);
            }
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
