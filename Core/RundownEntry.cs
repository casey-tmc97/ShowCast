namespace ShowCast.Core;

/// <summary>
/// One entry in a Rundown — a reference to a Package plus optional cue metadata.
/// </summary>
public class RundownEntry
{
    public Guid   Id               { get; init; } = Guid.NewGuid();
    public Guid   PackageId        { get; set; }
    public Guid           SelectedOutputId       { get; set; } = Guid.Empty;
    public TransitionType DefaultTransitionType  { get; set; } = TransitionType.Cut;
    public int            DefaultTransitionDuration { get; set; } = 500;
    public int    StartPageIndex   { get; set; } = 0;
    public string Label            { get; set; } = string.Empty;
    public bool   AutoAdvance      { get; set; } = false;
    public int    AutoAdvanceDelayMs { get; set; } = 0;
}
