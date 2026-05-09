namespace ShowCast.Core;

/// <summary>
/// A named folder that organises Packages.
/// Multiple Shows can exist (e.g. "Graphics", "Lower Thirds").
/// </summary>
public class Show
{
    public Guid      Id       { get; init; } = Guid.NewGuid();
    public string    Name     { get; set; }  = "Show";

    public List<Package> Packages { get; } = new();

    public Package AddPackage(string name)
    {
        var p = new Package { Name = name };
        Packages.Add(p);
        return p;
    }

    public void RemovePackage(Guid id) => Packages.RemoveAll(p => p.Id == id);

    public Package? PackageById(Guid id) => Packages.FirstOrDefault(p => p.Id == id);
}
