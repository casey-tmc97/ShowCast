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
}
