namespace ShowCast.Core;

/// <summary>
/// A Package is an ordered collection of Pages — the primary content unit.
/// Packages live inside Shows and are referenced by Rundowns.
/// Editing a Package anywhere changes it everywhere it is referenced.
/// </summary>
public class Package
{
    public Guid        Id          { get; init; } = Guid.NewGuid();
    public string      Name        { get; set; }  = "Untitled Package";
    public List<Page> Pages       { get; }       = new();
    public int         CanvasWidth  { get; set; } = 1920;
    public int         CanvasHeight { get; set; } = 1080;

    public Page? PageAt(int index) =>
        index >= 0 && index < Pages.Count ? Pages[index] : null;

    public int IndexOf(Guid pageId) =>
        Pages.FindIndex(s => s.Id == pageId);

    public void AddPage(Page page) => Pages.Add(page);

    public void InsertPage(int index, Page page)
    {
        index = Math.Clamp(index, 0, Pages.Count);
        Pages.Insert(index, page);
    }

    public void RemovePage(int index)
    {
        if (index >= 0 && index < Pages.Count)
            Pages.RemoveAt(index);
    }

    public void MovePage(int from, int to)
    {
        if (from == to) return;
        if (from < 0 || from >= Pages.Count) return;
        if (to   < 0 || to   >= Pages.Count) return;
        var page = Pages[from];
        Pages.RemoveAt(from);
        Pages.Insert(to, page);
    }
}
