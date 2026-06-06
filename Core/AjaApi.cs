using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace ShowCast.Core;

// P/Invoke declarations for ShowCastAjaAdapter.dll (the C wrapper around NTV2 SDK).
// All native methods are private and accessed only through the static API methods below.
static class AjaApiNative
{
    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern bool aja_initialize();

    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void aja_shutdown();

    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int aja_device_count();

    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void aja_device_name(int index, byte[] buf, int bufLen);

    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern IntPtr aja_open(int deviceIndex, int width, int height, int fpsN, int fpsD);

    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern void aja_close(IntPtr handle);

    [DllImport("ShowCastAjaAdapter", CallingConvention = CallingConvention.Cdecl)]
    internal static extern int aja_submit_frame(IntPtr handle, IntPtr pixels, int byteCount);
}

public static class AjaApi
{
    static bool? _available;
    public static bool IsAvailable => _available ?? false;

    // Drop-frame frame rates map to integer fraction form (30000/1001, 60000/1001, 24000/1001).
    // All others are integer fps / 1.
    static readonly Dictionary<int, (int n, int d)> _fpsMap = new()
    {
        { 23976, (24000, 1001) },
        { 24000, (24,    1)    },
        { 25000, (25,    1)    },
        { 29970, (30000, 1001) },
        { 30000, (30,    1)    },
        { 50000, (50,    1)    },
        { 59940, (60000, 1001) },
        { 60000, (60,    1)    },
    };

    public static bool TryInitialize()
    {
        try
        {
            bool ok = AjaApiNative.aja_initialize();
            _available = ok;
            if (!ok)
                Console.Error.WriteLine("[AJA] aja_initialize() returned false — AJA output disabled.");
            return ok;
        }
        catch (DllNotFoundException)
        {
            Console.Error.WriteLine(
                "[AJA] ShowCastAjaAdapter.dll not found — AJA output disabled. " +
                "(Build the C wrapper; see Docs/aja-integration.md)");
            _available = false;
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AJA] TryInitialize failed: {ex.Message}");
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
            int count = AjaApiNative.aja_device_count();
            var buf   = new byte[256];
            for (int i = 0; i < count; i++)
            {
                AjaApiNative.aja_device_name(i, buf, buf.Length);
                result.Add(Encoding.UTF8.GetString(buf).TrimEnd('\0'));
            }
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[AJA] EnumerateDevices failed: {ex.Message}");
        }
        return result;
    }

    public static (int fpsN, int fpsD) GetFpsComponents(double fps)
    {
        int key = (int)Math.Round(fps * 1000);
        return _fpsMap.TryGetValue(key, out var pair) ? pair : ((int)Math.Round(fps), 1);
    }

    internal static IntPtr Open(int deviceIndex, int w, int h, double fps)
    {
        var (n, d) = GetFpsComponents(fps);
        return AjaApiNative.aja_open(deviceIndex, w, h, n, d);
    }

    internal static void Close(IntPtr handle)   => AjaApiNative.aja_close(handle);

    internal static int SubmitFrame(IntPtr handle, IntPtr pixels, int byteCount)
        => AjaApiNative.aja_submit_frame(handle, pixels, byteCount);
}
