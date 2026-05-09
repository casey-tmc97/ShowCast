using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;

namespace NewTek;

/// <summary>
/// Minimal P/Invoke wrapper for the NDI 6 native SDK.
/// All entry points match NDIlib_* in Processing.NDI.Lib.x64.dll.
/// </summary>
public static class NDIlib
{
    const string Dll = "Processing.NDI.Lib.x64";

    public const int FourCC_BGRA             = 0x41524742;
    public const int FrameFormat_Progressive = 1;

    // Probe order: exe dir, exe/NDI subdir (copied from project), then SDK/Runtime installs
    static readonly string[] _searchPaths;

    static NDIlib()
    {
        var exeDir = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? string.Empty;
        _searchPaths =
        [
            exeDir,
            Path.Combine(exeDir, "NDI"),
            @"C:\Program Files\NDI\NDI 6 Runtime\v6",
            @"C:\Program Files\NDI\NDI 6 SDK\Bin\x64",
        ];
        NativeLibrary.SetDllImportResolver(typeof(NDIlib).Assembly, Resolve);
    }

    static IntPtr Resolve(string libraryName, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (libraryName != Dll) return IntPtr.Zero;

        foreach (var dir in _searchPaths)
        {
            var full = Path.Combine(dir, Dll + ".dll");
            if (File.Exists(full) && NativeLibrary.TryLoad(full, out var handle))
                return handle;
        }

        // Fall back to default OS resolution
        NativeLibrary.TryLoad(Dll, assembly, searchPath, out var fallback);
        return fallback;
    }

    // ── Availability ──────────────────────────────────────────────────────────

    static bool? _available;

    /// <summary>True once initialize() has succeeded; false if the DLL is missing.</summary>
    public static bool IsAvailable => _available ?? false;

    /// <summary>
    /// Call once at startup. Returns false (and logs) if the NDI runtime is not installed;
    /// the app continues running — NDI outputs will simply stay dark.
    /// </summary>
    public static bool TryInitialize()
    {
        try
        {
            _available = initialize();
            return _available.Value;
        }
        catch (DllNotFoundException ex)
        {
            Console.Error.WriteLine($"[NDI] Runtime not found — NDI output disabled. ({ex.Message})");
            _available = false;
            return false;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"[NDI] initialize() failed: {ex.Message}");
            _available = false;
            return false;
        }
    }

    // ── Structs ───────────────────────────────────────────────────────────────

    [StructLayout(LayoutKind.Sequential)]
    public struct send_create_t
    {
        public IntPtr p_ndi_name;
        public IntPtr p_groups;
        [MarshalAs(UnmanagedType.U1)] public bool clock_video;
        [MarshalAs(UnmanagedType.U1)] public bool clock_audio;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct video_frame_v2_t
    {
        public int    xres;
        public int    yres;
        public int    FourCC;
        public int    frame_rate_N;
        public int    frame_rate_D;
        public float  picture_aspect_ratio;
        public int    frame_format_type;
        private int   _pad;               // align timecode to 8 bytes
        public long   timecode;
        public IntPtr p_data;
        public int    line_stride_in_bytes;
        private int   _pad2;              // align p_metadata to 8 bytes
        public IntPtr p_metadata;
        public long   timestamp;
    }

    // ── Initialisation ────────────────────────────────────────────────────────

    [DllImport(Dll, EntryPoint = "NDIlib_initialize")]
    public static extern bool initialize();

    [DllImport(Dll, EntryPoint = "NDIlib_destroy")]
    public static extern void destroy();

    // ── Send ──────────────────────────────────────────────────────────────────

    [DllImport(Dll, EntryPoint = "NDIlib_send_create")]
    public static extern IntPtr send_create(ref send_create_t p_create_settings);

    [DllImport(Dll, EntryPoint = "NDIlib_send_destroy")]
    public static extern void send_destroy(IntPtr p_instance);

    [DllImport(Dll, EntryPoint = "NDIlib_send_send_video_v2")]
    public static extern void send_send_video_v2(IntPtr p_instance,
                                                  ref video_frame_v2_t p_video_data);

    [DllImport(Dll, EntryPoint = "NDIlib_send_get_no_connections")]
    public static extern int send_get_no_connections(IntPtr p_instance, uint timeout_in_ms);
}
