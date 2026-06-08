using ShowCast.Core;
using System.IO;
using Xunit;

namespace ShowCast.Tests.Core;

public class CompanionServerTests
{
    [Fact]
    public void OutputState_Blank_ClearsLivePage()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);

        state.Blank();

        Assert.Null(state.LivePage);
    }

    [Fact]
    public void OutputState_Unblank_RestoresLivePage()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);
        state.Blank();

        state.Unblank();

        Assert.Equal(page, state.LivePage);
        Assert.Equal(0, state.LivePageIndex);
    }

    [Fact]
    public void OutputState_Unblank_WithoutPriorBlank_DoesNothing()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);

        state.Unblank(); // must not throw

        Assert.Null(state.LivePage);
    }

    [Fact]
    public void OutputState_Blank_WhenAlreadyBlank_DoesNotOverwriteSavedState()
    {
        var cfg   = new OutputConfig();
        var state = new OutputState(cfg);
        var page  = new Page();
        state.GoLive(page, 0);
        state.Blank(); // first blank saves page

        state.Blank(); // second blank should be a no-op

        state.Unblank();
        Assert.Equal(page, state.LivePage); // original page still restored
    }

    [Fact]
    public async Task CompanionServer_Start_StatusBecomesListening()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15100 };

        server.Start(settings);
        await Task.Delay(50);

        Assert.Equal(ServerStatus.Listening, server.Status);
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_AuthWithCorrectPassword_Succeeds()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15101, TcpPassword = "secret" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15101);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":"secret"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("auth_ok", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_AuthWithWrongPassword_Fails()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15102, TcpPassword = "secret" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15102);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":"wrong"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("auth_fail", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_NoPassword_AnyAuthSucceeds()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15103, TcpPassword = "" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15103);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":"anything"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("auth_ok", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_CommandBeforeAuth_ReturnsError()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15104, TcpPassword = "secret" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15104);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"page_advance"}""");
        string? response = await reader.ReadLineAsync();

        Assert.Contains("error", response ?? "");
        server.Stop();
    }

    [Fact]
    public async Task CompanionServer_PushState_DeliveredToAuthenticatedClient()
    {
        var server   = new CompanionServer();
        var settings = new NetworkSettings { TcpEnabled = true, TcpPort = 15105, TcpPassword = "" };
        server.Start(settings);
        await Task.Delay(50);

        using var client = new System.Net.Sockets.TcpClient();
        await client.ConnectAsync("127.0.0.1", 15105);
        using var reader = new StreamReader(client.GetStream());
        using var writer = new StreamWriter(client.GetStream()) { AutoFlush = true };

        await writer.WriteLineAsync("""{"type":"auth","password":""}""");
        await reader.ReadLineAsync(); // consume auth_ok

        server.PushState("""{"type":"state","page":{"name":"Test"}}""");
        string? pushed = await reader.ReadLineAsync();

        Assert.Contains("state", pushed ?? "");
        server.Stop();
    }
}
