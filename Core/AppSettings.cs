namespace ShowCast.Core;

public class AppSettings
{
    // Editor display toggles
    public bool   ShowGrid           { get; set; } = true;
    public bool   ShowRulers         { get; set; } = true;
    public int    GridSpacing        { get; set; } = 100;
    public bool   SnapToGrid         { get; set; } = false;
    public bool   ShowSafeBoundaries { get; set; } = false;

    // Page grid view
    public double ThumbSize { get; set; } = 160;
    public string ViewMode  { get; set; } = "Grid";

    // Window geometry (null = use OS default)
    public double WindowWidth     { get; set; } = 1600;
    public double WindowHeight    { get; set; } = 900;
    public int?   WindowX         { get; set; } = null;
    public int?   WindowY         { get; set; } = null;
    public bool   WindowMaximized { get; set; } = false;

    // Panel splitter positions
    public double LeftPanelWidth     { get; set; } = 200;
    public double RightPanelWidth    { get; set; } = 300;
    public double LeftTopStarWeight  { get; set; } = 1;
    public double LeftMidStarWeight  { get; set; } = 1;
    public double LeftBotStarWeight  { get; set; } = 1;
    public double RightTopStarWeight { get; set; } = 1;
    public double RightBotStarWeight { get; set; } = 1;

    // Transition bar state
    public string NextTransitionType     { get; set; } = "Cut";
    public int    NextTransitionDuration { get; set; } = 0;

    // Last-selected items (restored by ID)
    public Guid SelectedOutputId     { get; set; } = Guid.Empty;
    public Guid SelectedShowId       { get; set; } = Guid.Empty;
    public Guid SelectedRundownId    { get; set; } = Guid.Empty;
    public Guid SelectedPackageItemId   { get; set; } = Guid.Empty;
    public Guid SelectedAudioPlaylistId { get; set; } = Guid.Empty;
}
