namespace ShowCast.Core;

public enum AudioRouteType { Hardware, Ndi }

/// <summary>
/// A named, user-configurable audio output destination (hardware device or NDI).
/// Stored in AppSettings (machine-specific) so routing survives show file changes.
/// </summary>
public class AudioDestination
{
    public Guid           Id          { get; set; } = Guid.NewGuid();
    /// <summary>User-editable display name. Defaults to SystemName on first discovery.</summary>
    public string         DisplayName { get; set; } = "";
    public AudioRouteType Type        { get; set; }
    /// <summary>WASAPI device ID (hardware) or NDI stream name.</summary>
    public string         DeviceId    { get; set; } = "";
    /// <summary>System-assigned name. Auto-updated on Refresh; never user-editable.</summary>
    public string         SystemName  { get; set; } = "";
}

/// <summary>
/// A named audio channel, each backed by its own MediaPlayer.
/// Stored in AppSettings (machine-specific).
/// </summary>
public class AudioChannel
{
    public Guid   Id                  { get; set; } = Guid.NewGuid();
    public string Name                { get; set; } = "New Channel";
    /// <summary>Id of the AudioDestination this channel routes to. Null = OS default device.</summary>
    public Guid?  ActiveDestinationId { get; set; }
    /// <summary>Last selected playlist on this channel (restored on load).</summary>
    public Guid   SelectedPlaylistId  { get; set; } = Guid.Empty;
}
