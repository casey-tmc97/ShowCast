using System.Collections.Generic;
using System.Linq;

namespace ShowCast.Core;

/// <summary>
/// Snapshot-based undo/redo for the page editor.
/// Call Push() BEFORE any mutation; Undo/Redo swap the layer state.
/// </summary>
public class EditorHistory
{
    const int MaxDepth = 99;

    readonly List<List<SlideLayer>> _undoStack = new();
    readonly List<List<SlideLayer>> _redoStack = new();

    public bool CanUndo => _undoStack.Count > 0;
    public bool CanRedo => _redoStack.Count > 0;

    /// <summary>Snapshot current page layers onto the undo stack.</summary>
    public void Push(Page page)
    {
        _undoStack.Add(Snapshot(page));
        _redoStack.Clear();
        while (_undoStack.Count > MaxDepth)
            _undoStack.RemoveAt(0);
    }

    public bool Undo(Page page)
    {
        if (!CanUndo) return false;
        _redoStack.Add(Snapshot(page));
        Restore(page, _undoStack[^1]);
        _undoStack.RemoveAt(_undoStack.Count - 1);
        return true;
    }

    public bool Redo(Page page)
    {
        if (!CanRedo) return false;
        _undoStack.Add(Snapshot(page));
        Restore(page, _redoStack[^1]);
        _redoStack.RemoveAt(_redoStack.Count - 1);
        return true;
    }

    public void Clear()
    {
        _undoStack.Clear();
        _redoStack.Clear();
    }

    static List<SlideLayer> Snapshot(Page page) =>
        page.Layers.Select(l => l.Clone()).ToList();

    static void Restore(Page page, List<SlideLayer> snapshot)
    {
        page.Layers.Clear();
        foreach (var l in snapshot)
            page.AddLayer(l);
    }
}
