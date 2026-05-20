namespace ShowCast.Core;

public class AudioTrack
{
    public Guid   Id           { get; set; } = Guid.NewGuid();
    public string Title        { get; set; } = "";
    /// <summary>Filename only — resolved against AppFolders.Media at runtime.</summary>
    public string RelativePath { get; set; } = "";
    public long   DurationMs   { get; set; }
}
