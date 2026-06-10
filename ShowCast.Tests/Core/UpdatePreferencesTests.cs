using System;
using System.IO;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class UpdatePreferencesTests
{
    [Fact]
    public void ShouldShow_DefaultPrefs_ReturnsTrue()
    {
        var prefs = new UpdatePreferences();
        Assert.True(prefs.ShouldShow("1.2.0"));
    }

    [Fact]
    public void ShouldShow_SkippedVersionMatches_ReturnsFalse()
    {
        var prefs = new UpdatePreferences { SkippedVersion = "1.2.0" };
        Assert.False(prefs.ShouldShow("1.2.0"));
    }

    [Fact]
    public void ShouldShow_SkippedVersionDiffers_ReturnsTrue()
    {
        var prefs = new UpdatePreferences { SkippedVersion = "1.1.0" };
        Assert.True(prefs.ShouldShow("1.2.0"));
    }

    [Fact]
    public void ShouldShow_RemindAfterInFuture_ReturnsFalse()
    {
        var prefs = new UpdatePreferences { RemindAfter = DateTime.UtcNow.AddDays(1) };
        Assert.False(prefs.ShouldShow("1.2.0"));
    }

    [Fact]
    public void ShouldShow_RemindAfterInPast_ReturnsTrue()
    {
        var prefs = new UpdatePreferences { RemindAfter = DateTime.UtcNow.AddDays(-1) };
        Assert.True(prefs.ShouldShow("1.2.0"));
    }

    [Fact]
    public void SaveLoad_RoundTrip_PreservesValues()
    {
        var path = Path.GetTempFileName();
        try
        {
            var prefs = new UpdatePreferences
            {
                SkippedVersion = "1.5.0",
                RemindAfter    = new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc)
            };
            prefs.Save(path);

            var loaded = UpdatePreferences.Load(path);
            Assert.Equal("1.5.0", loaded.SkippedVersion);
            Assert.Equal(new DateTime(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc), loaded.RemindAfter);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Load_MissingFile_ReturnsDefaults()
    {
        var loaded = UpdatePreferences.Load("/nonexistent/path/prefs.json");
        Assert.Null(loaded.SkippedVersion);
        Assert.Null(loaded.RemindAfter);
    }
}
