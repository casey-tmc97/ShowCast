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
