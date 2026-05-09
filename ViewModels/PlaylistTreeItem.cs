using ReactiveUI;
using ShowCast.Core;

namespace ShowCast.ViewModels;

/// <summary>
/// Flat-tree node for the Rundown sidebar panel.
/// Each node is either a RundownFolder (collapsible) or a Rundown (selectable).
/// </summary>
public class PlaylistTreeItem : ViewModelBase
{
    public RundownFolder? Folder   { get; }
    public Rundown?       Rundown  { get; }

    public bool IsFolder  => Folder  is not null;
    public bool IsRundown => Rundown is not null;

    // Keep IsPlaylist alias for compatibility with code that reads it
    public bool IsPlaylist => IsRundown;

    // Keep Playlist alias for code that accesses .Playlist
    public Rundown? Playlist => Rundown;

    /// <summary>Nesting depth — drives left-padding in the UI.</summary>
    public int    Depth        { get; }
    public double IndentMargin => Depth * 16.0;

    public string Name => IsFolder ? Folder!.Name : Rundown!.Name;

    private bool _isExpanded;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            this.RaiseAndSetIfChanged(ref _isExpanded, value);
            if (Folder is not null) Folder.IsExpanded = value;
            this.RaisePropertyChanged(nameof(ExpandIcon));
        }
    }

    /// <summary>Caret icon shown on folder rows.</summary>
    public string ExpandIcon => _isExpanded ? "▾" : "▸";

    public PlaylistTreeItem(RundownFolder folder, int depth)
    {
        Folder      = folder;
        Depth       = depth;
        _isExpanded = folder.IsExpanded;
    }

    public PlaylistTreeItem(Rundown rundown, int depth)
    {
        Rundown     = rundown;
        Depth       = depth;
        _isExpanded = false;
    }
}
