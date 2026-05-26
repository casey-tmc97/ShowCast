using System.Text.Json;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class ShowFileSerializerTests
{
    [Fact]
    public void ShowFile_DefaultVersion_IsCurrentVersion()
    {
        var file = new ShowFile();
        Assert.Equal(ShowFile.CurrentVersion, file.Version);
    }

    [Fact]
    public void ShowFile_UnknownFields_CapturesExtraJsonProperties()
    {
        // Arrange – JSON with a field that ShowFile doesn't know about
        var json = """
            {
                "Version": 1,
                "FutureField": "hello"
            }
            """;

        // Act
        var options = ShowFileSerializer.CreateSerializerOptions();
        var file = JsonSerializer.Deserialize<ShowFile>(json, options);

        // Assert
        Assert.NotNull(file);
        Assert.NotNull(file.UnknownFields);
        Assert.True(file.UnknownFields.ContainsKey("FutureField"));
    }

    [Fact]
    public async Task LoadAsync_CurrentVersion_ReturnsNeedsMigrationFalse()
    {
        // Arrange
        var path = Path.GetTempFileName();
        try
        {
            var json = $$"""{ "Version": {{ShowFile.CurrentVersion}} }""";
            await File.WriteAllTextAsync(path, json);

            // Act
            var result = await ShowFileSerializer.LoadAsync(path);

            // Assert
            Assert.NotNull(result);
            Assert.False(result.NeedsMigration);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LoadAsync_OlderVersion_ReturnsNeedsMigrationTrue()
    {
        // Arrange
        var path = Path.GetTempFileName();
        try
        {
            var json = """{ "Version": 0 }""";
            await File.WriteAllTextAsync(path, json);

            // Act
            var result = await ShowFileSerializer.LoadAsync(path);

            // Assert
            Assert.NotNull(result);
            Assert.True(result.NeedsMigration);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public async Task LoadAsync_NewerVersion_ThrowsShowFileVersionTooNewException()
    {
        // Arrange
        var path = Path.GetTempFileName();
        try
        {
            var json = $$$"""{ "Version": {{{ShowFile.CurrentVersion + 1}}} }""";
            await File.WriteAllTextAsync(path, json);

            // Act & Assert
            await Assert.ThrowsAsync<ShowFileVersionTooNewException>(
                () => ShowFileSerializer.LoadAsync(path));
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void ApplyMigration_AlreadyCurrentVersion_DoesNothing()
    {
        var file = new ShowFile { Version = ShowFile.CurrentVersion };
        ShowFileSerializer.ApplyMigration(file);
        Assert.Equal(ShowFile.CurrentVersion, file.Version);
    }

    [Fact]
    public void ApplyMigration_OlderVersion_SetsCurrentVersion()
    {
        // Version 0 represents a legacy file with no version field (pre-versioning)
        var file = new ShowFile { Version = 0 };
        ShowFileSerializer.ApplyMigration(file);
        Assert.Equal(ShowFile.CurrentVersion, file.Version);
    }

    [Fact]
    public async Task SaveAsync_ClearsUnknownFieldsBeforeSerializing()
    {
        // Arrange – a file that has captured unknown fields (as if loaded from a future version)
        var file = new ShowFile
        {
            UnknownFields = new Dictionary<string, System.Text.Json.JsonElement>
            {
                ["FutureField"] = System.Text.Json.JsonDocument.Parse("\"hello\"").RootElement
            }
        };

        var path = Path.GetTempFileName();
        try
        {
            // Act
            await ShowFileSerializer.SaveAsync(file, path);

            // Assert – the saved file must NOT contain the foreign field
            var json = await File.ReadAllTextAsync(path);
            Assert.DoesNotContain("FutureField", json);

            // And UnknownFields must be null after saving (side-effect is acceptable)
            Assert.Null(file.UnknownFields);
        }
        finally
        {
            File.Delete(path);
            File.Delete(path + ".tmp"); // clean up .tmp if move failed
        }
    }

    [Fact]
    public async Task SaveAsync_LoadAsync_RoundTripsAudioPlaylists()
    {
        var file = new ShowFile();
        var playlist = new AudioPlaylist { Name = "Sunday Set" };
        playlist.Tracks.Add(new AudioTrack { Title = "Song 1", RelativePath = "song1.mp3", DurationMs = 180000 });
        file.AudioPlaylists.Add(playlist);

        var path = Path.GetTempFileName();
        try
        {
            await ShowFileSerializer.SaveAsync(file, path);
            var result = await ShowFileSerializer.LoadAsync(path);

            Assert.NotNull(result);
            Assert.Single(result.File.AudioPlaylists);
            Assert.Equal("Sunday Set", result.File.AudioPlaylists[0].Name);
            Assert.Single(result.File.AudioPlaylists[0].Tracks);
            Assert.Equal("Song 1", result.File.AudioPlaylists[0].Tracks[0].Title);
            Assert.Equal(180000L, result.File.AudioPlaylists[0].Tracks[0].DurationMs);
        }
        finally { File.Delete(path); File.Delete(path + ".tmp"); }
    }

    [Fact]
    public async Task LoadAsync_OldFile_GetsEmptyAudioPlaylists()
    {
        var path = Path.GetTempFileName();
        try
        {
            // Old file with no AudioPlaylists field
            var json = """{ "Version": 1 }""";
            await File.WriteAllTextAsync(path, json);

            var result = await ShowFileSerializer.LoadAsync(path);

            Assert.NotNull(result);
            Assert.Empty(result.File.AudioPlaylists);
        }
        finally { File.Delete(path); }
    }

    [Fact]
    public void Page_TriggerAudioPlaylistId_SurvivesRoundTrip()
    {
        var triggerId = Guid.NewGuid();
        var file = new ShowFile();
        var show = file.AddShow("Test");
        var package = show.AddPackage("Pkg");
        var page = new Page { TriggerAudioPlaylistId = triggerId };
        package.AddPage(page);

        var options = ShowFileSerializer.CreateSerializerOptions();
        var json = JsonSerializer.Serialize(file, options);
        var loaded = JsonSerializer.Deserialize<ShowFile>(json, options);

        var loadedPage = loaded!.Shows[0].Packages[0].Pages[0];
        Assert.Equal(triggerId, loadedPage.TriggerAudioPlaylistId);
    }

    [Fact]
    public void Page_TriggerAudioTrackId_SurvivesRoundTrip()
    {
        var trackId = Guid.NewGuid();
        var file = new ShowFile();
        var show = file.AddShow("Test");
        var package = show.AddPackage("Pkg");
        var page = new Page { TriggerAudioTrackId = trackId };
        package.AddPage(page);

        var options = ShowFileSerializer.CreateSerializerOptions();
        var json = JsonSerializer.Serialize(file, options);
        var loaded = JsonSerializer.Deserialize<ShowFile>(json, options);

        var loadedPage = loaded!.Shows[0].Packages[0].Pages[0];
        Assert.Equal(trackId, loadedPage.TriggerAudioTrackId);
    }
}
