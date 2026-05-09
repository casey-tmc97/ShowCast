namespace ShowCast.Core;

public enum OutputType
{
    Display,        // Native OS monitor
    NDI,            // NDI network stream
    AJA,            // AJA video card
    Blackmagic,     // Blackmagic Design card
    BirdDog,        // BirdDog NDI device
    Preview         // Internal preview (no hardware)
}

public enum OutputMode
{
    Linked,         // Follows the master rundown
    Independent     // Controlled separately
}

/// <summary>
/// Defines a video output destination and what it renders.
/// Answers "how" — the Rundown answers "what".
/// </summary>
public class OutputConfig
{
    public Guid       Id           { get; init; } = Guid.NewGuid();
    public string     Name         { get; set; }  = "Output";
    public OutputType Type         { get; set; }  = OutputType.Display;
    public OutputMode Mode         { get; set; }  = OutputMode.Linked;

    /// <summary>Which layer roles this output renders.</summary>
    public LayerRole  RoleFilter   { get; set; }  = LayerRole.All;

    // Hardware / network targets
    public int    DisplayIndex    { get; set; } = 0;
    public string NdiStreamName   { get; set; } = string.Empty;
    public string DeviceSerial    { get; set; } = string.Empty;

    // Output resolution (may differ from canvas)
    public int    Width           { get; set; } = 1920;
    public int    Height          { get; set; } = 1080;
    public double FrameRate       { get; set; } = 59.94;

    public bool   Enabled         { get; set; } = true;
    public bool   Fullscreen      { get; set; } = false;

    /// <summary>ID of the Package last loaded to this output; restored on session load.</summary>
    public Guid   ActivePackageId { get; set; } = Guid.Empty;
}
