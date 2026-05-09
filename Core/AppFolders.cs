namespace ShowCast.Core;

/// <summary>
/// Ensures the ShowCast folder structure exists under the user's Documents folder.
/// Call <see cref="EnsureCreated"/> once at startup.
/// </summary>
public static class AppFolders
{
    public static string Root          { get; private set; } = "";
    public static string Configuration { get; private set; } = "";
    public static string Libraries     { get; private set; } = "";
    public static string Playlists     { get; private set; } = "";
    public static string Media         { get; private set; } = "";

    /// <summary>Fixed path for the auto-saved session state.</summary>
    public static string SessionFile   => Path.Combine(Configuration, "session.scf");

    public static void EnsureCreated()
    {
        Root          = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ShowCast");
        Configuration = Path.Combine(Root, "Configuration");
        Libraries     = Path.Combine(Root, "Libraries");
        Playlists     = Path.Combine(Root, "Playlists");
        Media         = Path.Combine(Root, "Media");

        Directory.CreateDirectory(Configuration);
        Directory.CreateDirectory(Libraries);
        Directory.CreateDirectory(Playlists);
        Directory.CreateDirectory(Media);

        // Default library folder
        Directory.CreateDirectory(Path.Combine(Libraries, "Default"));
    }
}
