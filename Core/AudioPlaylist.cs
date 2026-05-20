namespace ShowCast.Core;

public enum RepeatMode { None, One, All }
public enum ResumeMode { FromTop, FromLastPosition }

public class AudioPlaylist
{
    public Guid             Id             { get; set; } = Guid.NewGuid();
    public string           Name           { get; set; } = "New Playlist";
    public List<AudioTrack> Tracks         { get; set; } = new();
    public bool             AutoAdvance    { get; set; } = true;
    public bool             Shuffle        { get; set; } = false;
    public RepeatMode       Repeat         { get; set; } = RepeatMode.None;
    public float            Speed          { get; set; } = 1.0f;
    public ResumeMode       ResumeMode     { get; set; } = ResumeMode.FromTop;
    public Guid             LastTrackId    { get; set; }
    public long             LastPositionMs { get; set; }
}
