using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using ShowCast.Core;
using SkiaSharp;

namespace ShowCast.Engine;

/// <summary>
/// Renders a Page onto a SkiaSharp canvas, applying the given role filter.
/// Stateless — call Render() from any output's paint handler.
/// </summary>
public static class PageRenderer
{
    public static void Render(SKCanvas canvas, Page page, LayerRole roleFilter,
                              int canvasWidth, int canvasHeight,
                              double elapsedMs        = -1.0,
                              double exitElapsedMs    = -1.0,
                              bool useLiveTimers      = true,
                              Func<Guid, SKImage?>? getVideoFrame = null)
    {
        canvas.Clear(SKColors.Black);

        foreach (var layer in page.LayersForRoles(roleFilter))
        {
            var   rect     = LayerRect(layer, canvasWidth, canvasHeight);
            float animProg = ComputeAnimProgress(layer, elapsedMs);
            float exitProg = ComputeExitProgress(layer, exitElapsedMs, elapsedMs);

            // Exit takes priority; suppress entry if exit has started
            bool animating = animProg < 1f && exitProg == 0f;
            bool exiting   = exitProg > 0f;

            // Choose save mode: SaveLayer controls whole-layer alpha for fades
            bool fadeIn  = animating && layer.EntryAnim == LayerAnimation.FadeIn;
            bool fadeOut = exiting   && layer.ExitAnim  == LayerExitAnimation.FadeOut;

            if (fadeOut)
            {
                float a = (1f - exitProg) * layer.Opacity;
                using var ap = new SKPaint { Color = SKColors.White.WithAlpha((byte)(a * 255)) };
                canvas.SaveLayer(ap);
            }
            else if (fadeIn)
            {
                using var ap = new SKPaint
                    { Color = SKColors.White.WithAlpha((byte)(animProg * 255)) };
                canvas.SaveLayer(ap);
            }
            else
            {
                canvas.Save();
            }

            // Rotation (applied before clip)
            if (layer.RotationDegrees != 0f)
                canvas.RotateDegrees(layer.RotationDegrees, rect.MidX, rect.MidY);

            // Clip to layer rect first (screen space), then apply transform.
            // This keeps all motion contained within the layer's own bounding box.
            canvas.ClipRect(rect);

            if (exiting && layer.ExitAnim != LayerExitAnimation.None &&
                layer.ExitAnim != LayerExitAnimation.FadeOut)
            {
                ApplyExitTransform(canvas, layer.ExitAnim, exitProg, rect);
            }
            else if (animating && layer.EntryAnim != LayerAnimation.None &&
                     layer.EntryAnim != LayerAnimation.FadeIn)
            {
                ApplyEntryTransform(canvas, layer.EntryAnim, animProg, rect);
            }

            switch (layer.Type)
            {
                case LayerType.Background:
                case LayerType.Shape:
                    DrawShape(canvas, layer, canvasWidth, canvasHeight);
                    break;

                case LayerType.Text:
                    DrawText(canvas, layer, canvasWidth, canvasHeight, useLiveTimers);
                    break;

                case LayerType.Image:
                    DrawImagePlaceholder(canvas, layer, canvasWidth, canvasHeight);
                    break;

                case LayerType.Video:
                {
                    // Live frame from output > cached first frame for editor/thumbnails > placeholder.
                    var frame = getVideoFrame?.Invoke(layer.Id) ?? GetCachedThumbnail(layer.AssetPath);
                    if (frame is not null)
                        DrawSKImageInRect(canvas, frame, rect, layer);
                    else
                    {
                        using var bg = new SKPaint { Color = new SKColor(20, 20, 40, (byte)(layer.Opacity * 255)), BlendMode = ToSkia(layer.BlendMode) };
                        canvas.DrawRect(rect, bg);
                        DrawCenteredLabel(canvas, "[ Video ]", rect, SKColors.Gray);
                    }
                    break;
                }
            }

            canvas.Restore();
        }
    }

    // ── Layer animation helpers ───────────────────────────────────────────────

    /// <summary>
    /// Returns 0–1 progress for a layer's entry animation.
    /// Returns 1 when elapsedMs is -1 (static/thumbnail renders) or when animation is complete.
    /// </summary>
    static float ComputeAnimProgress(SlideLayer layer, double elapsedMs)
    {
        if (elapsedMs < 0 || layer.EntryAnim == LayerAnimation.None) return 1f;
        float delay    = layer.EntryDelayMs;
        float duration = layer.EntryDurationMs > 0 ? layer.EntryDurationMs : 400f;
        if (elapsedMs < delay) return 0f;
        float t = Math.Clamp((float)((elapsedMs - delay) / duration), 0f, 1f);
        return ApplyEasing(t, layer.EntryEasing, isExit: false);
    }

    static void ApplyEntryTransform(SKCanvas canvas, LayerAnimation anim, float progress,
                                     SKRect rect)
    {
        float inv = 1f - progress; // 1 at start → 0 at end
        switch (anim)
        {
            case LayerAnimation.SlideInLeft:  canvas.Translate(-rect.Width  * inv, 0);  break;
            case LayerAnimation.SlideInRight: canvas.Translate( rect.Width  * inv, 0);  break;
            case LayerAnimation.SlideInUp:    canvas.Translate(0, -rect.Height * inv);  break;
            case LayerAnimation.SlideInDown:  canvas.Translate(0,  rect.Height * inv);  break;
            case LayerAnimation.ZoomIn:
                float scale = 0.5f + 0.5f * progress;
                canvas.Translate(rect.MidX, rect.MidY);
                canvas.Scale(scale, scale);
                canvas.Translate(-rect.MidX, -rect.MidY);
                break;
        }
    }

    /// <summary>
    /// Returns 0–1 exit progress. 0 = fully visible, 1 = fully exited.
    /// <para>
    /// Two modes:<br/>
    /// • Transition-driven: <paramref name="exitElapsedMs"/> ≥ 0 — used when a page-to-page
    ///   transition is in flight; <paramref name="elapsedMs"/> is ignored.<br/>
    /// • Hold-driven: <paramref name="exitElapsedMs"/> = -1 and the layer has
    ///   <see cref="SlideLayer.HoldDurationMs"/> &gt; 0 — the exit timer starts automatically
    ///   after entry + hold time has elapsed (computed from <paramref name="elapsedMs"/>).
    /// </para>
    /// </summary>
    static float ComputeExitProgress(SlideLayer layer, double exitElapsedMs, double elapsedMs = -1.0)
    {
        if (layer.ExitAnim == LayerExitAnimation.None) return 0f;

        double effectiveExitElapsed;

        if (exitElapsedMs >= 0)
        {
            // Page-to-page transition provides explicit elapsed time
            effectiveExitElapsed = exitElapsedMs;
        }
        else if (elapsedMs >= 0 && layer.HoldDurationMs > 0)
        {
            // Hold-based auto-exit: begins after entry animation + hold period
            float exitStartMs = layer.EntryDelayMs + layer.EntryDurationMs + layer.HoldDurationMs;
            if (elapsedMs < exitStartMs) return 0f;
            effectiveExitElapsed = elapsedMs - exitStartMs;
        }
        else
        {
            return 0f;
        }

        float delay    = layer.ExitDelayMs;
        float duration = layer.ExitDurationMs > 0 ? layer.ExitDurationMs : 400f;
        if (effectiveExitElapsed < delay) return 0f;
        float t = Math.Clamp((float)((effectiveExitElapsed - delay) / duration), 0f, 1f);
        return ApplyEasing(t, layer.ExitEasing, isExit: true);
    }

    // ── Easing ────────────────────────────────────────────────────────────────

    /// <summary>
    /// Applies an easing curve to a 0–1 linear progress value.
    /// <paramref name="mode"/> interpretation for entry: 0=ease-out, 1=linear, 2=ease-in, 3=ease-in-out.
    /// <paramref name="mode"/> interpretation for exit:  0=ease-in,  1=linear, 2=ease-out, 3=ease-in-out.
    /// </summary>
    static float ApplyEasing(float t, int mode, bool isExit)
    {
        if (isExit)
        {
            return mode switch
            {
                1 => t,                             // linear
                2 => 1f - (1f - t) * (1f - t),     // ease-out
                3 => t * t * (3f - 2f * t),         // ease-in-out
                _ => t * t                           // ease-in (default for exits)
            };
        }
        else
        {
            return mode switch
            {
                1 => t,                             // linear
                2 => t * t,                         // ease-in
                3 => t * t * (3f - 2f * t),         // ease-in-out
                _ => 1f - (1f - t) * (1f - t)      // ease-out (default for entries)
            };
        }
    }

    static void ApplyExitTransform(SKCanvas canvas, LayerExitAnimation anim, float progress,
                                    SKRect rect)
    {
        // progress goes 0 (visible) → 1 (fully exited), so content moves out
        switch (anim)
        {
            case LayerExitAnimation.SlideOutLeft:  canvas.Translate(-rect.Width  * progress, 0);  break;
            case LayerExitAnimation.SlideOutRight: canvas.Translate( rect.Width  * progress, 0);  break;
            case LayerExitAnimation.SlideOutUp:    canvas.Translate(0, -rect.Height * progress);  break;
            case LayerExitAnimation.SlideOutDown:  canvas.Translate(0,  rect.Height * progress);  break;
            case LayerExitAnimation.ZoomOut:
                float scale = 1f - 0.5f * progress; // 1.0 → 0.5
                canvas.Translate(rect.MidX, rect.MidY);
                canvas.Scale(scale, scale);
                canvas.Translate(-rect.MidX, -rect.MidY);
                break;
        }
    }

    // ── Video thumbnail cache ─────────────────────────────────────────────────
    // Caches the first decoded frame of each video file for editor/thumbnail previews.
    // Extraction runs on a background Task; completed frames are stored as immutable SKImages.

    static readonly ConcurrentDictionary<string, SKImage?> _videoThumbnailCache = new();
    static readonly ConcurrentDictionary<string, byte>     _videoExtracting     = new();

    /// <summary>
    /// Fires (on a background thread) when a video thumbnail finishes extracting.
    /// Subscribers must dispatch to the UI thread before touching any UI objects.
    /// </summary>
    public static event Action? VideoThumbnailCached;

    /// <summary>Remove a cached thumbnail so it is re-extracted on next render.</summary>
    public static void InvalidateVideoThumbnail(string assetPath)
        => _videoThumbnailCache.TryRemove(Path.Combine(AppFolders.Video, assetPath), out _);

    /// <summary>Clear all video thumbnails (call on show close/new).</summary>
    public static void ClearVideoThumbnailCache() => _videoThumbnailCache.Clear();

    static SKImage? GetCachedThumbnail(string assetPath)
    {
        if (string.IsNullOrEmpty(assetPath)) return null;
        var full = Path.Combine(AppFolders.Video, assetPath);
        if (!File.Exists(full)) return null;
        if (_videoThumbnailCache.TryGetValue(full, out var cached)) return cached;
        if (_videoExtracting.TryAdd(full, 0))
            Task.Run(() => ExtractFirstVideoFrame(full));
        return null;
    }

    static void ExtractFirstVideoFrame(string fullPath)
    {
        SKImage? frame = null;
        try
        {
            using var player = new VideoLayerPlayer();
            player.Start(fullPath, VideoLoopMode.HoldLastFrame, 0f, null);

            // Wait for VLC to start outputting frames, then seek to 100ms for a
            // representative thumbnail (avoids black frames at the very start).
            var deadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < deadline && player.CurrentFrame is null)
                Thread.Sleep(20);

            player.SeekMs(3000);

            // Wait for the post-seek frame (new SKImage reference = new decoded frame).
            var prevFrame = player.CurrentFrame;
            var seekDeadline = DateTime.UtcNow.AddSeconds(3);
            while (DateTime.UtcNow < seekDeadline)
            {
                var current = player.CurrentFrame;
                if (current is not null && !ReferenceEquals(current, prevFrame))
                {
                    frame = current;
                    break;
                }
                Thread.Sleep(20);
            }
            frame ??= player.CurrentFrame; // fallback: use whatever frame we have
        }
        catch { }

        _videoThumbnailCache[fullPath] = frame;
        _videoExtracting.TryRemove(fullPath, out _);
        if (frame is not null) VideoThumbnailCached?.Invoke();
    }

    // ── Image cache ──────────────────────────────────────────────────────────
    // ConcurrentDictionary: PageRenderer.Render() is called from the NDI background thread
    // AND the UI thread (thumbnails) simultaneously — plain Dictionary is not safe here.
    static readonly ConcurrentDictionary<string, SKBitmap> _imageCache = new();

    /// <summary>Force-reload an image on next render (call after the file changes).</summary>
    public static void InvalidateImage(string path)
    {
        if (_imageCache.TryRemove(path, out var old)) old.Dispose();
    }

    /// <summary>Dispose and clear all cached bitmaps (call on file close/new).</summary>
    public static void ClearImageCache()
    {
        // Snapshot keys before clearing so we don't dispose while another thread reads.
        var bitmaps = _imageCache.Values.ToArray();
        _imageCache.Clear();
        foreach (var bmp in bitmaps) bmp.Dispose();
    }

    static SKBitmap? LoadImage(string path)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        if (_imageCache.TryGetValue(path, out var cached)) return cached;
        var bmp = SKBitmap.Decode(path);
        if (bmp is null) return null;
        // GetOrAdd is safe under concurrent access; the extra decode on a rare race is acceptable.
        return _imageCache.GetOrAdd(path, bmp);
    }

    // ── Helpers ──────────────────────────────────────────────────────────────

    static SKRect LayerRect(SlideLayer layer, int w, int h) =>
        new(layer.X * w, layer.Y * h,
            (layer.X + layer.Width) * w,
            (layer.Y + layer.Height) * h);

    static SKBlendMode ToSkia(BlendMode mode) => mode switch
    {
        BlendMode.Multiply => SKBlendMode.Multiply,
        BlendMode.Screen   => SKBlendMode.Screen,
        BlendMode.Overlay  => SKBlendMode.Overlay,
        BlendMode.Add      => SKBlendMode.Plus,
        _                  => SKBlendMode.SrcOver
    };

    static void DrawBitmapInRect(SKCanvas canvas, SKBitmap bmp, SKRect rect, SlideLayer layer)
    {
        byte alpha = (byte)(layer.Opacity * 255);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = SKColors.White.WithAlpha(alpha),
            BlendMode   = ToSkia(layer.BlendMode)
        };

        var src = new SKRect(0, 0, bmp.Width, bmp.Height);

        switch (layer.ImageFit)
        {
            case ImageFit.Stretch:
                canvas.DrawBitmap(bmp, src, rect, paint);
                break;

            case ImageFit.Fill:
                float scaleF = Math.Max(rect.Width / bmp.Width, rect.Height / bmp.Height);
                float cw = rect.Width / scaleF, ch = rect.Height / scaleF;
                src = new SKRect((bmp.Width - cw) / 2, (bmp.Height - ch) / 2,
                                 (bmp.Width + cw) / 2, (bmp.Height + ch) / 2);
                canvas.DrawBitmap(bmp, src, rect, paint);
                break;

            default: // Fit — letterbox, preserve aspect
                float scaleL = Math.Min(rect.Width / bmp.Width, rect.Height / bmp.Height);
                float fw = bmp.Width * scaleL, fh = bmp.Height * scaleL;
                var dst = new SKRect(rect.MidX - fw / 2, rect.MidY - fh / 2,
                                     rect.MidX + fw / 2, rect.MidY + fh / 2);
                canvas.DrawBitmap(bmp, src, dst, paint);
                break;
        }
    }

    static void DrawSKImageInRect(SKCanvas canvas, SKImage img, SKRect rect, SlideLayer layer)
    {
        byte alpha = (byte)(layer.Opacity * 255);
        using var paint = new SKPaint
        {
            IsAntialias = true,
            Color       = SKColors.White.WithAlpha(alpha),
            BlendMode   = ToSkia(layer.BlendMode)
        };

        var src = new SKRect(0, 0, img.Width, img.Height);

        switch (layer.ImageFit)
        {
            case ImageFit.Stretch:
                canvas.DrawImage(img, src, rect, paint);
                break;

            case ImageFit.Fill:
                float scaleF = Math.Max(rect.Width / img.Width, rect.Height / img.Height);
                float cw = rect.Width / scaleF, ch = rect.Height / scaleF;
                src = new SKRect((img.Width - cw) / 2, (img.Height - ch) / 2,
                                 (img.Width + cw) / 2, (img.Height + ch) / 2);
                canvas.DrawImage(img, src, rect, paint);
                break;

            default: // Fit — letterbox, preserve aspect
                float scaleL = Math.Min(rect.Width / img.Width, rect.Height / img.Height);
                float fw = img.Width * scaleL, fh = img.Height * scaleL;
                var dst = new SKRect(rect.MidX - fw / 2, rect.MidY - fh / 2,
                                     rect.MidX + fw / 2, rect.MidY + fh / 2);
                canvas.DrawImage(img, src, dst, paint);
                break;
        }
    }

    // ── Shape drawing ─────────────────────────────────────────────────────────

    static void DrawShape(SKCanvas canvas, SlideLayer layer, int w, int h)
    {
        var rect  = LayerRect(layer, w, h);
        byte alpha = (byte)(layer.Opacity * 255);

        using var fill = new SKPaint
        {
            Color       = layer.Color.WithAlpha(alpha),
            Style       = SKPaintStyle.Fill,
            BlendMode   = ToSkia(layer.BlendMode),
            IsAntialias = true
        };
        DrawShapeKind(canvas, layer, rect, fill, w);

        if (layer.StrokeWidth > 0 && layer.StrokeColor.Alpha > 0)
        {
            using var stroke = new SKPaint
            {
                Color       = layer.StrokeColor.WithAlpha(alpha),
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = layer.StrokeWidth * w / 1920f,
                IsAntialias = true
            };
            DrawShapeKind(canvas, layer, rect, stroke, w);
        }
    }

    static void DrawShapeKind(SKCanvas canvas, SlideLayer layer, SKRect rect, SKPaint paint, int w)
    {
        switch (layer.ShapeKind)
        {
            case ShapeKind.Ellipse:
                canvas.DrawOval(rect, paint);
                break;

            case ShapeKind.RoundedRect:
                float rx = layer.CornerRadius * w / 1920f;
                canvas.DrawRoundRect(rect, rx, rx, paint);
                break;

            case ShapeKind.Triangle:
                using (var path = new SKPath())
                {
                    path.MoveTo(rect.MidX, rect.Top);
                    path.LineTo(rect.Right, rect.Bottom);
                    path.LineTo(rect.Left, rect.Bottom);
                    path.Close();
                    canvas.DrawPath(path, paint);
                }
                break;

            default: // Rectangle
                canvas.DrawRect(rect, paint);
                break;
        }
    }

    // ── Text drawing ──────────────────────────────────────────────────────────

    static void DrawText(SKCanvas canvas, SlideLayer layer, int w, int h, bool useLiveTimers = true)
    {
        if (layer.TimerBinding is { } tid)
        {
            if (useLiveTimers && TimerTextCache.Values.TryGetValue(tid, out var tv))
                DrawPlainText(canvas, layer, tv, w, h);
            else
                DrawPlainText(canvas, layer, "12:34:56:78", w, h);
            return;
        }

        if (layer.Spans.Count > 0)
        {
            DrawSpans(canvas, layer, w, h);
            return;
        }

        DrawPlainText(canvas, layer, layer.Text, w, h);
    }

    static void DrawPlainText(SKCanvas canvas, SlideLayer layer, string text, int w, int h)
    {
        if (string.IsNullOrEmpty(text)) return;

        var   rect     = LayerRect(layer, w, h);
        float fontSize = layer.FontSize * h;

        var fontStyle = (layer.Bold, layer.Italic) switch
        {
            (true,  true)  => SKFontStyle.BoldItalic,
            (true,  false) => SKFontStyle.Bold,
            (false, true)  => SKFontStyle.Italic,
            _              => SKFontStyle.Normal
        };

        using var tf = SKTypeface.FromFamilyName(layer.FontFamily, fontStyle);

        var skAlign = layer.TextHAlign switch
        {
            TextHAlign.Left  => SKTextAlign.Left,
            TextHAlign.Right => SKTextAlign.Right,
            _                => SKTextAlign.Center
        };

        using var paint = new SKPaint
        {
            Color       = layer.Color.WithAlpha((byte)(layer.Opacity * 255)),
            TextSize    = fontSize,
            IsAntialias = true,
            Typeface    = tf,
            BlendMode   = ToSkia(layer.BlendMode),
            TextAlign   = skAlign
        };

        float textX = layer.TextHAlign switch
        {
            TextHAlign.Left  => rect.Left,
            TextHAlign.Right => rect.Right,
            _                => rect.MidX
        };

        var   lines  = WrapText(text, paint, rect.Width);
        float lh     = fontSize * 1.2f;
        float total  = lines.Count * lh;
        float startY = layer.TextVAlign switch
        {
            TextVAlign.Top    => rect.Top    + fontSize,
            TextVAlign.Bottom => rect.Bottom - total + fontSize,
            _                 => rect.MidY   - total / 2f + fontSize
        };

        float baselineShift = layer.Baseline * h / 1080f;

        // Draw stroke under fill if set
        if (layer.StrokeWidth > 0 && layer.StrokeColor.Alpha > 0)
        {
            using var sp = new SKPaint
            {
                Color       = layer.StrokeColor.WithAlpha((byte)(layer.Opacity * 255)),
                TextSize    = fontSize,
                IsAntialias = true,
                Typeface    = tf,
                Style       = SKPaintStyle.Stroke,
                StrokeWidth = layer.StrokeWidth * h / 1080f,
                TextAlign   = skAlign
            };
            for (int i = 0; i < lines.Count; i++)
                canvas.DrawText(lines[i], textX, startY + i * lh - baselineShift, sp);
        }

        for (int i = 0; i < lines.Count; i++)
        {
            float lineBaseline = startY + i * lh - baselineShift;
            canvas.DrawText(lines[i], textX, lineBaseline, paint);

            if (layer.Underline)
            {
                float sw = fontSize * 0.06f;
                float lineW = paint.MeasureText(lines[i]);
                float x1 = layer.TextHAlign switch
                {
                    TextHAlign.Left  => textX,
                    TextHAlign.Right => textX - lineW,
                    _                => textX - lineW / 2f,
                };
                using var ulp = new SKPaint { Color = paint.Color, StrokeWidth = sw, Style = SKPaintStyle.Stroke, IsAntialias = true };
                canvas.DrawLine(x1, lineBaseline + sw, x1 + lineW, lineBaseline + sw, ulp);
            }

            if (layer.Strikethrough)
            {
                float sw      = fontSize * 0.06f;
                float strikeY = lineBaseline - fontSize * 0.30f;
                float lineW   = paint.MeasureText(lines[i]);
                float x1 = layer.TextHAlign switch
                {
                    TextHAlign.Left  => textX,
                    TextHAlign.Right => textX - lineW,
                    _                => textX - lineW / 2f,
                };
                using var stp = new SKPaint { Color = paint.Color, StrokeWidth = sw, Style = SKPaintStyle.Stroke, IsAntialias = true };
                canvas.DrawLine(x1, strikeY, x1 + lineW, strikeY, stp);
            }
        }
    }

    static void DrawSpans(SKCanvas canvas, SlideLayer layer, int w, int h)
    {
        if (layer.Spans.Count == 0) return;

        float bx = layer.X * w, by = layer.Y * h;
        float bw = layer.Width * w, bh = layer.Height * h;
        if (bw <= 0 || bh <= 0) return;

        // Build per-span render info: (text, typeface, paint, lineH, underline, strikethrough, baselineShift, kerning)
        var spanInfos = new List<(string text, SKTypeface tf, SKPaint paint, float lineH,
                                  bool underline, bool strikethrough, float baselineShift, float kerning)>();
        foreach (var span in layer.Spans)
        {
            if (string.IsNullOrEmpty(span.Text)) continue;

            float  fs     = (span.FontSize   ?? layer.FontSize) * h;
            string ff     = span.FontFamily  ?? layer.FontFamily;
            bool   bold   = span.Bold        ?? layer.Bold;
            bool   italic = span.Italic      ?? layer.Italic;
            var    color  = span.Color       ?? layer.Color;
            bool   ul     = span.Underline     ?? layer.Underline;
            bool   st     = span.Strikethrough ?? layer.Strikethrough;
            float  bshift = (span.Baseline ?? layer.Baseline) * h / 1080f;
            float  kern   = (span.Kerning  ?? layer.Kerning)  * w / 1920f;

            var style = (bold, italic) switch
            {
                (true,  true)  => SKFontStyle.BoldItalic,
                (true,  false) => SKFontStyle.Bold,
                (false, true)  => SKFontStyle.Italic,
                _              => SKFontStyle.Normal,
            };
            var tf    = SKTypeface.FromFamilyName(ff, style) ?? SKTypeface.Default;
            var paint = new SKPaint
            {
                Color       = color,
                TextSize    = fs,
                IsAntialias = true,
                Typeface    = tf,
            };
            spanInfos.Add((span.Text, tf, paint, fs * 1.25f, ul, st, bshift, kern));
        }

        if (spanInfos.Count == 0) return;

        // Tokenise: split each span's text into word-units, keeping newlines as explicit break tokens
        // Each token: (text, paint, measured_width, lineH, underline, strikethrough, baselineShift, kerning)
        var tokens = new List<(string txt, SKPaint p, float tw, float lh,
                               bool ul, bool st, float bs, float kn)>();
        foreach (var (spanText, _, paint, lh, ul, st, bs, kn) in spanInfos)
        {
            var parts = spanText.Split('\n');
            for (int pi = 0; pi < parts.Length; pi++)
            {
                var words = parts[pi].Split(' ');
                for (int wi = 0; wi < words.Length; wi++)
                {
                    bool   addSpace = wi < words.Length - 1;
                    string tok      = addSpace ? words[wi] + " " : words[wi];
                    if (tok.Length == 0) continue;
                    float tw = paint.MeasureText(tok);
                    tokens.Add((tok, paint, tw, lh, ul, st, bs, kn));
                }
                // Explicit newline token between parts
                if (pi < parts.Length - 1)
                    tokens.Add(("\n", paint, bw + 1, lh, ul, st, bs, kn)); // width > bw forces a wrap
            }
        }

        // Word-wrap into lines
        var lines = new List<List<(string txt, SKPaint p, float tw, float lh,
                                   bool ul, bool st, float bs, float kn)>>();
        var cur   = new List<(string txt, SKPaint p, float tw, float lh,
                              bool ul, bool st, float bs, float kn)>();
        float curW = 0;
        foreach (var tok in tokens)
        {
            bool isNewline = tok.txt == "\n";
            if (isNewline || (cur.Count > 0 && curW + tok.tw > bw))
            {
                lines.Add(cur);
                cur  = new();
                curW = 0;
                if (isNewline) continue;
            }
            cur.Add(tok);
            curW += tok.tw;
        }
        if (cur.Count > 0) lines.Add(cur);

        if (lines.Count == 0) { DisposeSpanInfos(spanInfos); return; }

        float lineH  = spanInfos.Max(s => s.lineH);
        float totalH = lines.Count * lineH;
        float startY = layer.TextVAlign switch
        {
            TextVAlign.Bottom => by + bh - totalH,
            TextVAlign.Middle => by + (bh - totalH) / 2f,
            _                 => by,
        };

        canvas.Save();
        try
        {
            canvas.ClipRect(new SKRect(bx, by, bx + bw, by + bh));

            float y = startY;
            foreach (var line in lines)
            {
                float lineW   = line.Sum(t => t.tw);
                float x       = layer.TextHAlign switch
                {
                    TextHAlign.Right  => bx + bw - lineW,
                    TextHAlign.Center => bx + (bw - lineW) / 2f,
                    _                 => bx,
                };
                float baseline = y + lineH * 0.8f;
                foreach (var (txt, paint, tw, _, ul, st, bs, kn) in line)
                {
                    float effectiveBaseline = baseline - bs;
                    canvas.DrawText(txt, x, effectiveBaseline, paint);

                    // Underline
                    if (ul)
                    {
                        float strokeW = paint.TextSize * 0.06f;
                        using var ulPaint = new SKPaint
                        {
                            Color       = paint.Color,
                            StrokeWidth = strokeW,
                            Style       = SKPaintStyle.Stroke,
                            IsAntialias = true,
                        };
                        canvas.DrawLine(x, effectiveBaseline + strokeW, x + tw + kn, effectiveBaseline + strokeW, ulPaint);
                    }

                    // Strikethrough
                    if (st)
                    {
                        float strokeW = paint.TextSize * 0.06f;
                        float strikeY = effectiveBaseline - paint.TextSize * 0.30f;
                        using var stPaint = new SKPaint
                        {
                            Color       = paint.Color,
                            StrokeWidth = strokeW,
                            Style       = SKPaintStyle.Stroke,
                            IsAntialias = true,
                        };
                        canvas.DrawLine(x, strikeY, x + tw + kn, strikeY, stPaint);
                    }

                    x += tw + kn;
                }
                y += lineH;
            }
        }
        finally
        {
            canvas.Restore();
        }

        DisposeSpanInfos(spanInfos);
    }

    static void DisposeSpanInfos(
        List<(string text, SKTypeface tf, SKPaint paint, float lineH,
              bool ul, bool st, float baselineShift, float kerning)> infos)
    {
        foreach (var (_, tf, p, _, _, _, _, _) in infos) { tf.Dispose(); p.Dispose(); }
    }

    // ── Placeholders ──────────────────────────────────────────────────────────

    static void DrawImagePlaceholder(SKCanvas canvas, SlideLayer layer, int w, int h)
    {
        var rect = LayerRect(layer, w, h);
        var bmp  = LoadImage(layer.AssetPath);

        if (bmp is not null)
        {
            DrawBitmapInRect(canvas, bmp, rect, layer);
            return;
        }

        using var bg = new SKPaint { Color = new SKColor(60, 60, 80, (byte)(layer.Opacity * 255)), BlendMode = ToSkia(layer.BlendMode) };
        canvas.DrawRect(rect, bg);
        DrawCenteredLabel(canvas, "[ Image ]", rect, SKColors.Gray);
    }

    static void DrawCenteredLabel(SKCanvas canvas, string text, SKRect rect, SKColor color)
    {
        using var p = new SKPaint
        {
            Color       = color,
            TextSize    = 18,
            IsAntialias = true,
            TextAlign   = SKTextAlign.Center
        };
        canvas.DrawText(text, rect.MidX, rect.MidY + 6, p);
    }

    // ── Word wrap ─────────────────────────────────────────────────────────────

    static List<string> WrapText(string text, SKPaint paint, float maxWidth)
    {
        var result = new List<string>();
        foreach (var rawLine in text.Split('\n'))
        {
            var words   = rawLine.Split(' ');
            var current = string.Empty;
            foreach (var word in words)
            {
                var test = current.Length == 0 ? word : current + " " + word;
                if (paint.MeasureText(test) <= maxWidth)
                    current = test;
                else
                {
                    if (current.Length > 0) result.Add(current);
                    current = word;
                }
            }
            if (current.Length > 0) result.Add(current);
        }
        return result;
    }
}
