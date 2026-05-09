using SkiaSharp;

namespace ShowCast.Core;

public enum LayerType      { Background, Text, Image, Shape, Clock, Feed }
public enum BlendMode      { Normal, Multiply, Screen, Overlay, Add }
public enum TextHAlign     { Left, Center, Right }
public enum TextVAlign     { Top, Middle, Bottom }
public enum ShapeKind      { Rectangle, Ellipse, RoundedRect, Triangle }
public enum ImageFit       { Fit, Fill, Stretch }
public enum LayerAnimation     { None, FadeIn, SlideInLeft, SlideInRight, SlideInUp, SlideInDown, ZoomIn }
public enum LayerExitAnimation { None, FadeOut, SlideOutLeft, SlideOutRight, SlideOutUp, SlideOutDown, ZoomOut }

/// <summary>
/// A single composited element within a slide.
/// Layers are stacked by ZOrder and filtered by Role when sent to outputs.
/// Geometry values are normalized 0–1 relative to the 1920×1080 virtual canvas.
/// </summary>
public class SlideLayer
{
    public Guid      Id        { get; init; } = Guid.NewGuid();
    public string    Name      { get; set; }  = "Layer";
    public LayerType Type      { get; set; }  = LayerType.Background;
    public LayerRole Roles     { get; set; }  = LayerRole.All;

    // ── Geometry ──────────────────────────────────────────────────────────────
    public float X               { get; set; } = 0f;
    public float Y               { get; set; } = 0f;
    public float Width           { get; set; } = 1f;
    public float Height          { get; set; } = 1f;
    public float RotationDegrees { get; set; } = 0f;

    // ── Compositing ───────────────────────────────────────────────────────────
    public float     Opacity   { get; set; } = 1f;
    public BlendMode BlendMode { get; set; } = BlendMode.Normal;
    public int       ZOrder    { get; set; } = 0;
    public bool      Visible   { get; set; } = true;
    public bool      Locked    { get; set; } = false;

    // ── Shape ─────────────────────────────────────────────────────────────────
    public ShapeKind ShapeKind    { get; set; } = ShapeKind.Rectangle;
    /// <summary>Corner radius in virtual 1920×1080 pixels. Used for RoundedRect.</summary>
    public float     CornerRadius { get; set; } = 0f;

    // ── Stroke (Shape, Background, Text) ─────────────────────────────────────
    public SKColor StrokeColor { get; set; } = SKColors.Transparent;
    /// <summary>Stroke width in virtual 1920×1080 pixels.</summary>
    public float   StrokeWidth { get; set; } = 0f;

    // ── Content ───────────────────────────────────────────────────────────────
    /// <summary>When set, this text layer displays the live output of the named timer instead of Text.</summary>
    public Guid?      TimerBinding { get; set; } = null;
    public string     Text       { get; set; } = string.Empty;
    public SKColor    Color      { get; set; } = SKColors.White;
    /// <summary>Font size normalized to canvas height (0.07 ≈ 75px at 1080p).</summary>
    public float      FontSize   { get; set; } = 0.07f;
    public string     FontFamily { get; set; } = "Arial";
    public bool       Bold       { get; set; } = false;
    public bool       Italic     { get; set; } = false;
    public TextHAlign TextHAlign { get; set; } = TextHAlign.Center;
    public TextVAlign TextVAlign { get; set; } = TextVAlign.Middle;
    public string     AssetPath  { get; set; } = string.Empty;
    public ImageFit   ImageFit   { get; set; } = ImageFit.Fit;

    // ── Entry animation ───────────────────────────────────────────────────────
    public LayerAnimation EntryAnim       { get; set; } = LayerAnimation.None;
    public int            EntryDurationMs { get; set; } = 400;
    public int            EntryDelayMs    { get; set; } = 0;
    /// <summary>0=ease-out, 1=linear, 2=ease-in, 3=ease-in-out</summary>
    public int            EntryEasing     { get; set; } = 0;

    // ── Hold between entry and exit ───────────────────────────────────────────
    /// <summary>
    /// How long (ms) to hold the layer at full visibility after the entry animation
    /// completes before the exit animation begins. 0 = exit never auto-triggers.
    /// </summary>
    public int HoldDurationMs { get; set; } = 0;

    // ── Exit animation ────────────────────────────────────────────────────────
    public LayerExitAnimation ExitAnim       { get; set; } = LayerExitAnimation.None;
    public int                ExitDurationMs { get; set; } = 400;
    public int                ExitDelayMs    { get; set; } = 0;
    /// <summary>0=ease-in (default for exits), 1=linear, 2=ease-out, 3=ease-in-out</summary>
    public int                ExitEasing     { get; set; } = 0;

    /// <summary>Deep-copy. Pass newId=true when duplicating (fresh Guid).</summary>
    public SlideLayer Clone(bool newId = false) => new()
    {
        Id              = newId ? Guid.NewGuid() : Id,
        Name            = Name,           Type          = Type,
        Roles           = Roles,          ZOrder        = ZOrder,
        X               = X,              Y             = Y,
        Width           = Width,          Height        = Height,
        RotationDegrees = RotationDegrees,
        Opacity         = Opacity,        BlendMode     = BlendMode,
        Visible         = Visible,        Locked        = Locked,
        ShapeKind       = ShapeKind,      CornerRadius  = CornerRadius,
        StrokeColor     = StrokeColor,    StrokeWidth   = StrokeWidth,
        TimerBinding    = TimerBinding,
        Text            = Text,           Color         = Color,
        FontSize        = FontSize,       FontFamily    = FontFamily,
        Bold            = Bold,           Italic        = Italic,
        TextHAlign      = TextHAlign,     TextVAlign    = TextVAlign,
        AssetPath       = AssetPath,      ImageFit      = ImageFit,
        EntryAnim       = EntryAnim,
        EntryDurationMs = EntryDurationMs,
        EntryDelayMs    = EntryDelayMs,
        EntryEasing     = EntryEasing,
        HoldDurationMs  = HoldDurationMs,
        ExitAnim        = ExitAnim,
        ExitDurationMs  = ExitDurationMs,
        ExitDelayMs     = ExitDelayMs,
        ExitEasing      = ExitEasing
    };
}
