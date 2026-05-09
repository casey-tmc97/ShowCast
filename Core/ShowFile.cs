namespace ShowCast.Core;

/// <summary>
/// Root document — the saved show file.
/// Owns all Shows, Rundowns, and Output configurations.
/// </summary>
public class ShowFile
{
    public AppSettings          Settings        { get; } = new();
    public List<TimerDef>       Timers          { get; } = new();
    public List<Show>           Shows           { get; } = new();
    public List<RundownFolder>  RundownFolders  { get; } = new();
    public List<Rundown>        Rundowns        { get; } = new();
    public List<OutputConfig>   Outputs         { get; } = new();
    public List<ScheduledEvent> ScheduledEvents { get; } = new();

    public Show AddShow(string name)
    {
        var show = new Show { Name = name };
        Shows.Add(show);
        return show;
    }

    public void RemoveShow(Guid id) =>
        Shows.RemoveAll(s => s.Id == id);

    public RundownFolder AddRundownFolder(string name, Guid? parentId = null)
    {
        var f = new RundownFolder { Name = name, ParentId = parentId };
        RundownFolders.Add(f);
        return f;
    }

    public void RemoveRundownFolder(Guid id)
    {
        // Move any child rundowns to the parent of the removed folder
        var folder = RundownFolders.FirstOrDefault(f => f.Id == id);
        if (folder is not null)
            foreach (var rd in Rundowns.Where(r => r.FolderId == id))
                rd.FolderId = folder.ParentId;
        RundownFolders.RemoveAll(f => f.Id == id || f.ParentId == id);
    }

    public Rundown AddRundown(string name, Guid? folderId = null)
    {
        var r = new Rundown { Name = name, FolderId = folderId };
        Rundowns.Add(r);
        return r;
    }

    public void RemoveRundown(Guid id) =>
        Rundowns.RemoveAll(r => r.Id == id);

    public void AddOutput(OutputConfig config) => Outputs.Add(config);
    public void RemoveOutput(Guid id)          => Outputs.RemoveAll(o => o.Id == id);

    /// <summary>Locate a Package by ID across all Shows.</summary>
    public Package? FindPackage(Guid id) =>
        Shows.SelectMany(s => s.Packages).FirstOrDefault(p => p.Id == id);
}
