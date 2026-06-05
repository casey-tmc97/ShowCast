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
