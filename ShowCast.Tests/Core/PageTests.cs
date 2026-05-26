using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class PageTests
{
    [Fact]
    public void Clone_CopiesTriggerAudioPlaylistId()
    {
        var id = Guid.NewGuid();
        var original = new Page { TriggerAudioPlaylistId = id };

        var clone = original.Clone();

        Assert.Equal(id, clone.TriggerAudioPlaylistId);
    }

    [Fact]
    public void TriggerAudioPlaylistId_DefaultsToEmpty()
    {
        var page = new Page();

        Assert.Equal(Guid.Empty, page.TriggerAudioPlaylistId);
    }
}
