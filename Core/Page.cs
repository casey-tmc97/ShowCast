using SkiaSharp;

namespace ShowCast.Core;

public enum TransitionType { Cut, Fade, Wipe, Zoom, SlideLeft, SlideRight }

public class PageTransition
{
    public TransitionType Type       { get; set; } = TransitionType.Cut;
    public int            DurationMs { get; set; } = 500;
    public float          Easing     { get; set; } = 0.5f;  // 0=linear 1=ease-in-out
}

/// <summary>
/// A page is an ordered stack of layers.
/// Call LayersForRoles() to get only the layers an output should render.
/// </summary>
public class Page
{
    public Guid   Id   { get; init; } = Guid.NewGuid();
    public string Name { get; set; }  = "1";

    public List<SlideLayer>  Layers     { get; } = new();
    public PageTransition    Transition { get; set; } = new();

    /// <summary>Auto-advance delay. 0 = manual advance only.</summary>
    public int DurationMs { get; set; } = 0;

    /// <summary>When true, the auto-advance timer loops back to the first page instead of advancing forward.</summary>
    public bool LoopToStart { get; set; } = false;

    /// <summary>Timer IDs to Play() when this page goes live.</summary>
    public List<Guid> TriggerTimerIds { get; set; } = new();

    public Page Clone()
    {
        var copy = new Page { Name = Name };
        copy.Transition.Type       = Transition.Type;
        copy.Transition.DurationMs = Transition.DurationMs;
        copy.Transition.Easing     = Transition.Easing;
        copy.DurationMs            = DurationMs;
        copy.LoopToStart           = LoopToStart;
        foreach (var l in Layers)
            copy.AddLayer(l.Clone(newId: true));
        foreach (var id in TriggerTimerIds)
            copy.TriggerTimerIds.Add(id);
        return copy;
    }

    public void AddLayer(SlideLayer layer)
    {
        Layers.Add(layer);
        Layers.Sort((a, b) => a.ZOrder.CompareTo(b.ZOrder));
    }

    public void RemoveLayer(Guid id) =>
        Layers.RemoveAll(l => l.Id == id);

    /// <summary>
    /// Returns layers visible to a given output's role filter, in draw order.
    /// </summary>
    public IEnumerable<SlideLayer> LayersForRoles(LayerRole roles) =>
        Layers.Where(l => l.Visible && (l.Roles & roles) != 0);

    /// <summary>Representative background color for thumbnail display.</summary>
    public SKColor ThumbnailColor =>
        Layers.FirstOrDefault(l => l.Type == LayerType.Background)?.Color
        ?? new SKColor(30, 30, 30);
}
