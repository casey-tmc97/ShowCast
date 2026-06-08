using System.Text.Json;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class CompanionServerTests
{
    [Fact]
    public void NetworkSettings_Defaults_AreCorrect()
    {
        var s = new NetworkSettings();
        Assert.False(s.TcpEnabled);
        Assert.Equal(5100, s.TcpPort);
        Assert.Equal("", s.TcpPassword);
        Assert.Equal("", s.BindAdapterName);
    }

    [Fact]
    public void AppSettings_ContainsNetworkSettings()
    {
        var settings = new AppSettings();
        Assert.NotNull(settings.Network);
        Assert.IsType<NetworkSettings>(settings.Network);
    }
}
