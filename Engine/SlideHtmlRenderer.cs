using System;
using System.Text;
using ShowCast.Core;
using SkiaSharp;

namespace ShowCast.Engine;

/// <summary>
/// Converts a Page to a self-contained HTML document.
/// Geometry is normalized 0-1; positions map directly to CSS percentages.
/// Font sizes use vh units (normalized to canvas height).
/// </summary>
public static class SlideHtmlRenderer
{
    public static string Render(Page page, LayerRole roles)
    {
        var sb = new StringBuilder(4096);
        sb.Append("""
            <!DOCTYPE html>
            <html>
            <head>
            <meta charset="UTF-8">
            <style>
            *, *::before, *::after { box-sizing: border-box; margin: 0; padding: 0; }
            html, body { width: 100%; height: 100%; overflow: hidden; background: black; }
            .layer { position: absolute; }
            </style>
            </head>
            <body>

            """);

        foreach (var layer in page.LayersForRoles(roles))
            AppendLayer(sb, layer);

        sb.Append("</body>\n</html>");
        return sb.ToString();
    }

    // ── Per-layer dispatch ────────────────────────────────────────────────────

    static void AppendLayer(StringBuilder sb, SlideLayer layer)
    {
        string pos  = $"left:{P(layer.X)};top:{P(layer.Y)};width:{P(layer.Width)};height:{P(layer.Height)};";
        string comp = $"opacity:{layer.Opacity:F2};mix-blend-mode:{CssBlend(layer.BlendMode)};";
        string rot  = layer.RotationDegrees != 0f
                      ? $"transform:rotate({layer.RotationDegrees:F2}deg);" : "";

        switch (layer.Type)
        {
            case LayerType.Background:
                sb.Append($"<div class=\"layer\" style=\"{pos}{comp}{rot}background:{CssRgba(layer.Color)};\"></div>\n");
                break;

            case LayerType.Shape:
                AppendShape(sb, layer, pos, comp, rot);
                break;

            case LayerType.Text:
                AppendText(sb, layer, pos, comp, rot);
                break;

            case LayerType.Image when !string.IsNullOrEmpty(layer.AssetPath):
                string objFit = layer.ImageFit switch {
                    ImageFit.Fill    => "cover",
                    ImageFit.Stretch => "fill",
                    _                => "contain"
                };
                sb.Append($"<img class=\"layer\" style=\"{pos}{comp}{rot}object-fit:{objFit};\" src=\"{FileUri(layer.AssetPath)}\">\n");
                break;

        }
    }

    static void AppendShape(StringBuilder sb, SlideLayer layer, string pos, string comp, string rot)
    {
        string bg     = $"background:{CssRgba(layer.Color)};";
        string border = layer.StrokeWidth > 0
            ? $"border:{Vh(layer.StrokeWidth)} solid {CssRgba(layer.StrokeColor)};" : "";

        string extra = layer.ShapeKind switch {
            ShapeKind.Ellipse     => "border-radius:50%;",
            ShapeKind.RoundedRect => $"border-radius:{Vh(layer.CornerRadius)};",
            ShapeKind.Triangle    => "clip-path:polygon(50% 0%,0% 100%,100% 100%);",
            _                     => ""
        };

        // Border interacts badly with clip-path on triangles; skip it.
        if (layer.ShapeKind == ShapeKind.Triangle) border = "";

        sb.Append($"<div class=\"layer\" style=\"{pos}{comp}{rot}{bg}{border}{extra}\"></div>\n");
    }

    static void AppendText(StringBuilder sb, SlideLayer layer, string pos, string comp, string rot)
    {
        string halign = layer.TextHAlign switch {
            TextHAlign.Left  => "left",
            TextHAlign.Right => "right",
            _                => "center"
        };
        string valign = layer.TextVAlign switch {
            TextVAlign.Top    => "flex-start",
            TextVAlign.Bottom => "flex-end",
            _                 => "center"
        };

        string stroke = layer.StrokeWidth > 0
            ? $"-webkit-text-stroke:{Vh(layer.StrokeWidth)} {CssRgba(layer.StrokeColor)};paint-order:stroke fill;" : "";

        string style = $"{pos}{comp}{rot}"
            + $"display:flex;flex-direction:column;justify-content:{valign};text-align:{halign};"
            + $"font-family:{AttrEsc(layer.FontFamily)},sans-serif;"
            + $"font-size:{layer.FontSize * 100:F3}vh;"
            + $"color:{CssRgba(layer.Color)};"
            + $"font-style:{(layer.Italic ? "italic" : "normal")};"
            + $"font-weight:{(layer.Bold ? "bold" : "normal")};"
            + $"word-wrap:break-word;overflow:hidden;{stroke}";

        sb.Append($"<div class=\"layer\" style=\"{style}\">");
        sb.Append($"<span>{HtmlEnc(layer.Text)}</span>");
        sb.Append("</div>\n");
    }

    // ── CSS helpers ───────────────────────────────────────────────────────────

    static string P(float normalized)      => $"{normalized * 100:F2}%";
    static string Vh(float virtualPx)      => $"{virtualPx / 1080.0 * 100:F3}vh";
    static string CssRgba(SKColor c)       => $"rgba({c.Red},{c.Green},{c.Blue},{c.Alpha / 255.0:F3})";
    static string AttrEsc(string s)        => s.Replace("\"", "&quot;");
    static string FileUri(string path)     => new Uri(path).AbsoluteUri;
    static string HtmlEnc(string s)        =>
        s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;")
         .Replace("\"", "&quot;").Replace("\n", "<br>");

    static string CssBlend(BlendMode m) => m switch {
        BlendMode.Multiply => "multiply",
        BlendMode.Screen   => "screen",
        BlendMode.Overlay  => "overlay",
        BlendMode.Add      => "screen",
        _                  => "normal"
    };
}
