namespace ShowCast.Core;

/// <summary>
/// The show rundown — an ordered list of Package references.
/// Answers "what plays and when". Does NOT control how outputs render it.
/// </summary>
public class Rundown
{
    public Guid               Id        { get; init; } = Guid.NewGuid();
    public string             Name      { get; set; }  = "New Rundown";
    /// <summary>The folder this rundown lives in (null = root level).</summary>
    public Guid?              FolderId  { get; set; }  = null;
    public List<RundownEntry> Entries   { get; }       = new();

    public RundownEntry? EntryAt(int index) =>
        index >= 0 && index < Entries.Count ? Entries[index] : null;

    public void AddEntry(RundownEntry entry)    => Entries.Add(entry);

    public void InsertEntry(int index, RundownEntry entry)
    {
        index = Math.Clamp(index, 0, Entries.Count);
        Entries.Insert(index, entry);
    }

    public void RemoveEntry(int index)
    {
        if (index >= 0 && index < Entries.Count)
            Entries.RemoveAt(index);
    }

    public void MoveEntry(int from, int to)
    {
        if (from == to) return;
        if (from < 0 || from >= Entries.Count) return;
        if (to   < 0 || to   >= Entries.Count) return;
        var entry = Entries[from];
        Entries.RemoveAt(from);
        Entries.Insert(to, entry);
    }
}
