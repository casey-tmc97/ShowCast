using System;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class UpdateCheckerTests
{
    const string NewerReleaseJson = """
        {
          "tag_name": "v1.2.0",
          "assets": [
            {
              "name": "ShowCast-1.2.0-win-x64-setup.exe",
              "browser_download_url": "https://example.com/ShowCast-1.2.0-win-x64-setup.exe"
            }
          ]
        }
        """;

    const string SameVersionJson = """
        {
          "tag_name": "v1.1.1",
          "assets": [
            {
              "name": "ShowCast-1.1.1-win-x64-setup.exe",
              "browser_download_url": "https://example.com/ShowCast-1.1.1-win-x64-setup.exe"
            }
          ]
        }
        """;

    const string OlderVersionJson = """
        {
          "tag_name": "v1.0.0",
          "assets": [
            {
              "name": "ShowCast-1.0.0-win-x64-setup.exe",
              "browser_download_url": "https://example.com/ShowCast-1.0.0-win-x64-setup.exe"
            }
          ]
        }
        """;

    const string NoMatchingAssetJson = """
        {
          "tag_name": "v1.2.0",
          "assets": [
            {
              "name": "ShowCast-1.2.0-linux.tar.gz",
              "browser_download_url": "https://example.com/ShowCast-1.2.0-linux.tar.gz"
            }
          ]
        }
        """;

    [Fact]
    public void ParseRelease_NewerVersion_ReturnsInfo()
    {
        var result = UpdateChecker.ParseRelease(NewerReleaseJson, new Version(1, 1, 1, 0));
        Assert.NotNull(result);
        Assert.Equal("1.2.0", result.Version);
        Assert.Equal("ShowCast-1.2.0-win-x64-setup.exe", result.AssetName);
        Assert.Equal("https://example.com/ShowCast-1.2.0-win-x64-setup.exe", result.DownloadUrl);
    }

    [Fact]
    public void ParseRelease_SameVersion_ReturnsNull()
    {
        var result = UpdateChecker.ParseRelease(SameVersionJson, new Version(1, 1, 1, 0));
        Assert.Null(result);
    }

    [Fact]
    public void ParseRelease_OlderVersion_ReturnsNull()
    {
        var result = UpdateChecker.ParseRelease(OlderVersionJson, new Version(1, 1, 1, 0));
        Assert.Null(result);
    }

    [Fact]
    public void ParseRelease_NoMatchingAsset_ReturnsNull()
    {
        var result = UpdateChecker.ParseRelease(NoMatchingAssetJson, new Version(1, 1, 1, 0));
        Assert.Null(result);
    }

    [Fact]
    public void ParseRelease_TagWithVPrefix_ParsesCorrectly()
    {
        var result = UpdateChecker.ParseRelease(NewerReleaseJson, new Version(1, 1, 0, 0));
        Assert.NotNull(result);
        Assert.Equal("1.2.0", result.Version);
    }

    [Theory]
    [InlineData(2, 0, 0, 1, 1, 1, true)]
    [InlineData(1, 2, 0, 1, 1, 1, true)]
    [InlineData(1, 1, 2, 1, 1, 1, true)]
    [InlineData(1, 1, 1, 1, 1, 1, false)]
    [InlineData(1, 1, 0, 1, 1, 1, false)]
    [InlineData(1, 0, 0, 1, 1, 1, false)]
    public void IsNewer_VariousVersions(
        int lMaj, int lMin, int lPatch,
        int cMaj, int cMin, int cPatch,
        bool expected)
    {
        var latest  = new Version(lMaj, lMin, lPatch);
        var current = new Version(cMaj, cMin, cPatch, 0);
        Assert.Equal(expected, UpdateChecker.IsNewer(latest, current));
    }
}
