using SkiaSharp;

namespace ShowCast.Core;

public class TextSpan
{
    public string   Text          { get; set; } = "";
    public float?   FontSize      { get; set; }   // null = inherit from layer
    public string?  FontFamily    { get; set; }   // null = inherit
    public bool?    Bold          { get; set; }   // null = inherit
    public bool?    Italic        { get; set; }   // null = inherit
    public SKColor? Color         { get; set; }   // null = inherit
    public bool?    Underline     { get; set; }   // null = inherit
    public bool?    Strikethrough { get; set; }   // null = inherit
    public float?   Baseline      { get; set; }   // vertical shift in virtual px (+ = up)
    public float?   Kerning       { get; set; }   // extra spacing after run in virtual px
}
