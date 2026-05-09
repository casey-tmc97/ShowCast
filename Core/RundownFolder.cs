namespace ShowCast.Core;

/// <summary>
/// A named container that groups Rundowns in the sidebar tree.
/// Folders can be nested (ParentId points to another folder, null = root level).
/// </summary>
public class RundownFolder
{
    public Guid   Id         { get; init; } = Guid.NewGuid();
    public string Name       { get; set; }  = "New Folder";
    public Guid?  ParentId   { get; set; }  = null;
    public bool   IsExpanded { get; set; }  = true;
}
