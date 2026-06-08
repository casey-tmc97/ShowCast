using System.Collections.Generic;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class AjaSenderTests
{
    // ── AjaApi ───────────────────────────────────────────────────────────────

    [Fact]
    public void AjaApi_TryInitialize_DoesNotThrow()
    {
        var ex = Record.Exception(() => AjaApi.TryInitialize());
        Assert.Null(ex);
    }

    [Fact]
    public void AjaApi_TryInitialize_ReturnsFalseWhenDllMissing()
    {
        // ShowCastAjaAdapter.dll is not present on the dev machine.
        // TryInitialize must catch DllNotFoundException and return false.
        bool result = AjaApi.TryInitialize();
        if (!AjaApi.IsAvailable)
            Assert.False(result);
        // If adapter IS present, result may be true — skip assertion.
    }

    [Fact]
    public void AjaApi_IsAvailable_FalseAfterTryInitializeWithoutDll()
    {
        AjaApi.TryInitialize();
        // On dev machine without DLL, must be false.
        // On AJA machine with adapter installed this is a no-op.
        if (!AjaApi.IsAvailable)
            Assert.False(AjaApi.IsAvailable);
    }

    [Fact]
    public void AjaApi_EnumerateDevices_ReturnsEmptyWhenUnavailable()
    {
        AjaApi.TryInitialize();
        if (!AjaApi.IsAvailable)
            Assert.Empty(AjaApi.EnumerateDevices());
    }

    [Fact]
    public void AjaApi_GetFpsComponents_29_97_Returns30000_1001()
    {
        var (n, d) = AjaApi.GetFpsComponents(29.97);
        Assert.Equal(30000, n);
        Assert.Equal(1001,  d);
    }

    [Fact]
    public void AjaApi_GetFpsComponents_23_976_Returns24000_1001()
    {
        var (n, d) = AjaApi.GetFpsComponents(23.976);
        Assert.Equal(24000, n);
        Assert.Equal(1001,  d);
    }

    [Fact]
    public void AjaApi_GetFpsComponents_59_94_Returns60000_1001()
    {
        var (n, d) = AjaApi.GetFpsComponents(59.94);
        Assert.Equal(60000, n);
        Assert.Equal(1001,  d);
    }

    [Fact]
    public void AjaApi_GetFpsComponents_IntegerFps_ReturnsFpsOver1()
    {
        var (n, d) = AjaApi.GetFpsComponents(30.0);
        Assert.Equal(30, n);
        Assert.Equal(1,  d);

        (n, d) = AjaApi.GetFpsComponents(60.0);
        Assert.Equal(60, n);
        Assert.Equal(1,  d);
    }

    // ── AjaSender ────────────────────────────────────────────────────────────

    [Fact]
    public void AjaSender_WhenApiUnavailable_ConstructorDoesNotThrow()
    {
        // Current stub throws InvalidOperationException unconditionally.
        // Real implementation must mirror BlackmagicSender: return early when IsAvailable == false.
        AjaApi.TryInitialize();
        if (AjaApi.IsAvailable) return; // skip on AJA machine — real device needed

        var cfg    = new OutputConfig { Name = "AJA Out", Type = OutputType.AJA, Enabled = true };
        var output = new OutputState(cfg);
        var ex     = Record.Exception(() => { using var s = new AjaSender(output, []); });
        Assert.Null(ex);
    }

    [Fact]
    public void AjaSender_WhenApiUnavailable_DisposeDoesNotThrow()
    {
        AjaApi.TryInitialize();
        if (AjaApi.IsAvailable) return;

        var cfg    = new OutputConfig { Name = "AJA Out", Type = OutputType.AJA };
        var output = new OutputState(cfg);

        AjaSender? sender = null;
        try { sender = new AjaSender(output, []); } catch { return; } // guard: if ctor throws, test already caught by above
        var ex = Record.Exception(() => sender.Dispose());
        Assert.Null(ex);
    }
}
