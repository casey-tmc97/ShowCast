using SkiaSharp;

namespace ShowCast.Views;

public sealed record SpanFormatInfo(
    bool?    Bold,
    bool?    Italic,
    float?   FontSize,
    string?  FontFamily,
    bool?    Underline,
    bool?    Strikethrough,
    float?   Baseline,
    float?   Kerning,
    SKColor? Color);
