using System.Collections.Generic;
using ShowCast.Core;
using Xunit;

namespace ShowCast.Tests.Core;

public class AudioDeviceEnumeratorTests
{
    // ── EnumerateNdi ─────────────────────────────────────────────────────────
    // (EnumerateHardware requires a live LibVLC instance so we only test NDI here)

    [Fact]
    public void EnumerateNdi_NoNdiOutputs_ReturnsEmptyList()
    {
        var showFile = new ShowFile(); // no NDI outputs
        var result   = AudioDeviceEnumerator.EnumerateNdi(showFile);
        Assert.Empty(result);
    }

    [Fact]
    public void EnumerateNdi_WithNdiOutput_ReturnsDestination()
    {
        var showFile  = new ShowFile();
        var ndiConfig = new OutputConfig
        {
            Name          = "ShowCast Main",
            Type          = OutputType.NDI,
            NdiStreamName = "SC-Main"
        };
        showFile.AddOutput(ndiConfig);

        var result = AudioDeviceEnumerator.EnumerateNdi(showFile);

        Assert.Single(result);
        Assert.Equal(AudioRouteType.Ndi,  result[0].Type);
        Assert.Equal("SC-Main",           result[0].DeviceId);
        Assert.Equal("ShowCast Main",     result[0].SystemName);
        Assert.Equal("ShowCast Main",     result[0].DisplayName);
    }

    [Fact]
    public void EnumerateNdi_IgnoresNonNdiOutputs()
    {
        var showFile   = new ShowFile();
        var displayCfg = new OutputConfig { Name = "Program", Type = OutputType.Display };
        showFile.AddOutput(displayCfg);

        var result = AudioDeviceEnumerator.EnumerateNdi(showFile);
        Assert.Empty(result);
    }

    // ── MergeHardware ─────────────────────────────────────────────────────────

    [Fact]
    public void MergeHardware_NewDevice_AddsToList()
    {
        var existing = new List<AudioDestination>();
        var fresh    = new List<AudioDestination>
        {
            new() { DeviceId = "hw-001", SystemName = "Realtek", DisplayName = "Realtek" }
        };

        AudioDeviceEnumerator.MergeHardware(existing, fresh);

        Assert.Single(existing);
        Assert.Equal("hw-001", existing[0].DeviceId);
    }

    [Fact]
    public void MergeHardware_ExistingDevice_UpdatesSystemNameOnly()
    {
        var dest = new AudioDestination
        {
            DeviceId    = "hw-001",
            SystemName  = "Old System Name",
            DisplayName = "My Custom Name"   // user-renamed — must not be touched
        };
        var existing = new List<AudioDestination> { dest };
        var fresh    = new List<AudioDestination>
        {
            new() { DeviceId = "hw-001", SystemName = "New System Name", DisplayName = "New System Name" }
        };

        AudioDeviceEnumerator.MergeHardware(existing, fresh);

        Assert.Single(existing);
        Assert.Equal("New System Name", existing[0].SystemName);
        Assert.Equal("My Custom Name",  existing[0].DisplayName); // user name preserved
    }
}
