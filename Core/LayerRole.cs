namespace ShowCast.Core;

/// <summary>
/// Tags a layer for routing to specific outputs.
/// Each output declares which roles it renders — same slide, different results.
/// </summary>
[Flags]
public enum LayerRole
{
    None       = 0,
    Program    = 1 << 0,   // Full broadcast output (backgrounds + graphics)
    Stage      = 1 << 1,   // Stage display (text only, high contrast)
    Overlay    = 1 << 2,   // Lower thirds and graphic overlays
    NDIKey     = 1 << 3,   // Alpha/key channel for NDI key+fill workflow
    NDIFill    = 1 << 4,   // Fill channel for NDI key+fill workflow
    Confidence = 1 << 5,   // Confidence monitor (timers, notes, cues)
    Preview    = 1 << 6,   // Preview / next-slide monitor
    All        = ~0
}
