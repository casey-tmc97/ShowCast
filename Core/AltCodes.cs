using System.Runtime.InteropServices;

namespace ShowCast.Core;

internal static class AltCodes
{
    // CP1252 characters for the 0x80–0x9F range (not direct Unicode code points)
    internal static readonly ushort[] Cp1252Special =
    [
        0x20AC, 0,      0x201A, 0x0192, 0x201E, 0x2026, 0x2020, 0x2021,
        0x02C6, 0x2030, 0x0160, 0x2039, 0x0152, 0,      0x017D, 0,
        0,      0x2018, 0x2019, 0x201C, 0x201D, 0x2022, 0x2013, 0x2014,
        0x02DC, 0x2122, 0x0161, 0x203A, 0x0153, 0,      0x017E, 0x0178
    ];

    [DllImport("kernel32.dll", SetLastError = false)]
    static extern int MultiByteToWideChar(uint codePage, uint flags,
        [In] byte[] src, int srcLen, [Out] char[] dst, int dstLen);

    // Standard Alt code (no leading zero) — OEM code page 437
    internal static char? StandardToChar(int code)
    {
        if (code <= 0 || code > 255) return null;
        var src = new byte[] { (byte)code };
        var dst = new char[2];
        int n = MultiByteToWideChar(437, 0, src, 1, dst, 2);
        return n > 0 ? dst[0] : null;
    }

    // Extended Alt code (leading zero) — Windows-1252
    internal static char? ExtendedToChar(int code)
    {
        if (code <= 0 || code > 255) return null;
        if (code < 0x80) return (char)code;
        if (code <= 0x9F)
        {
            ushort cp = Cp1252Special[code - 0x80];
            return cp == 0 ? null : (char)cp;
        }
        return (char)code;  // 0xA0–0xFF: Unicode code points match CP1252
    }

    internal static char? ToChar(int code, bool extended) =>
        extended ? ExtendedToChar(code) : StandardToChar(code);
}
