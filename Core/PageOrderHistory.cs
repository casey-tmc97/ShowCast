using System.Collections.Generic;
using System.Linq;

namespace ShowCast.Core;

public class PageOrderHistory
{
    const int MaxDepth = 99;

    // Each entry is a snapshot of one or more packages (for multi-package operations like cross-package move).
    readonly List<List<(Package Package, List<Page> Order)>> _undoStack = new();
    readonly List<List<(Package Package, List<Page> Order)>> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    public void Push(params Package[] packages)
    {
        _undoStack.Add(packages.Select(p => (p, p.Pages.ToList())).ToList());
        _redoStack.Clear();
        while (_undoStack.Count > MaxDepth)
            _undoStack.RemoveAt(0);
    }

    public List<Package>? Undo()
    {
        if (!CanUndo) return null;
        var entry = _undoStack[^1];
        _redoStack.Add(entry.Select(s => (s.Package, s.Package.Pages.ToList())).ToList());
        _undoStack.RemoveAt(_undoStack.Count - 1);
        var result = new List<Package>();
        foreach (var (pkg, saved) in entry)
        {
            pkg.Pages.Clear();
            foreach (var p in saved) pkg.Pages.Add(p);
            result.Add(pkg);
        }
        return result;
    }

    public List<Package>? Redo()
    {
        if (!CanRedo) return null;
        var entry = _redoStack[^1];
        _undoStack.Add(entry.Select(s => (s.Package, s.Package.Pages.ToList())).ToList());
        _redoStack.RemoveAt(_redoStack.Count - 1);
        var result = new List<Package>();
        foreach (var (pkg, saved) in entry)
        {
            pkg.Pages.Clear();
            foreach (var p in saved) pkg.Pages.Add(p);
            result.Add(pkg);
        }
        return result;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }
}
