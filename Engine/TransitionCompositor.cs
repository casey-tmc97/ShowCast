using ShowCast.Core;
using SkiaSharp;

namespace ShowCast.Engine;

/// <summary>
/// Composites two page frames for a mid-transition render.
/// All off-screen surfaces are created per-call; rendering happens at ≤60 fps only during
/// active transitions so the cost is bounded.
/// </summary>
public static class TransitionCompositor
{
    /// <param name="output">Canvas to draw the composited frame onto.</param>
    /// <param name="from">Page transitioning out.</param>
    /// <param name="to">Page transitioning in (newly live).</param>
    /// <param name="roles">Layer role filter applied to both pages.</param>
    /// <param name="type">Transition style.</param>
    /// <param name="rawProgress">Linear 0–1 elapsed fraction (before easing).</param>
    /// <param name="easing">0 = linear, 1 = smooth-step ease in/out.</param>
    /// <param name="w">Canvas width in pixels.</param>
    /// <param name="h">Canvas height in pixels.</param>
    /// <param name="exitElapsedMs">
    /// Milliseconds elapsed into the transition, passed to the FROM page so its
    /// exit animations play during the outgoing portion of the transition.
    /// </param>
    public static void Composite(SKCanvas output,
                                  Page from, Page to, LayerRole roles,
                                  TransitionType type,
                                  float rawProgress, float easing,
                                  int w, int h,
                                  double exitElapsedMs = 0.0)
    {
        float p = ApplyEasing(rawProgress, easing);

        if (type == TransitionType.Cut || p >= 1f)
        {
            PageRenderer.Render(output, to, roles, w, h);
            return;
        }

        // Render both pages into temporary off-screen images
        using var fromSurface = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));
        using var toSurface   = SKSurface.Create(new SKImageInfo(w, h, SKColorType.Rgba8888));

        // From page: drive exit animations using elapsed transition time
        PageRenderer.Render(fromSurface.Canvas, from, roles, w, h,
                             elapsedMs: -1, exitElapsedMs: exitElapsedMs);

        // To page: drive entry animations at a proportional time into the transition
        int toElapsed = (int)(p * (to.Transition.DurationMs > 0 ? to.Transition.DurationMs : 500));
        PageRenderer.Render(toSurface.Canvas, to, roles, w, h, toElapsed);

        using var fromImg = fromSurface.Snapshot();
        using var toImg   = toSurface.Snapshot();

        output.Clear(SKColors.Black);

        switch (type)
        {
            // ── Dissolve / Fade ────────────────────────────────────────────────
            case TransitionType.Fade:
                output.DrawImage(fromImg, 0f, 0f);
                using (var paint = new SKPaint
                    { Color = SKColors.White.WithAlpha((byte)(p * 255)) })
                    output.DrawImage(toImg, 0f, 0f, paint);
                break;

            // ── Horizontal wipe (left → right) ────────────────────────────────
            case TransitionType.Wipe:
            {
                float splitX = w * p;
                output.Save();
                output.ClipRect(new SKRect(0, 0, splitX, h));
                output.DrawImage(toImg, 0f, 0f);
                output.Restore();
                output.Save();
                output.ClipRect(new SKRect(splitX, 0, w, h));
                output.DrawImage(fromImg, 0f, 0f);
                output.Restore();
                break;
            }

            // ── Slide left: incoming from right, outgoing to left ─────────────
            case TransitionType.SlideLeft:
                output.DrawImage(fromImg, -w * p, 0f);
                output.DrawImage(toImg,    w * (1f - p), 0f);
                break;

            // ── Slide right: incoming from left, outgoing to right ────────────
            case TransitionType.SlideRight:
                output.DrawImage(fromImg,  w * p, 0f);
                output.DrawImage(toImg,   -w * (1f - p), 0f);
                break;

            // ── Zoom: incoming scales in from center while from fades out ─────
            case TransitionType.Zoom:
            {
                output.DrawImage(fromImg, 0f, 0f);
                float scale = 0.85f + 0.15f * p;
                output.Save();
                output.Translate(w / 2f, h / 2f);
                output.Scale(scale, scale);
                output.Translate(-w / 2f, -h / 2f);
                using (var paint = new SKPaint
                    { Color = SKColors.White.WithAlpha((byte)(p * 255)) })
                    output.DrawImage(toImg, 0f, 0f, paint);
                output.Restore();
                break;
            }

            default:
                output.DrawImage(toImg, 0f, 0f);
                break;
        }
    }

    // Blend between linear (easing=0) and smooth-step (easing=1)
    static float ApplyEasing(float t, float easing)
    {
        t = Math.Clamp(t, 0f, 1f);
        float smooth = t * t * (3f - 2f * t);
        return t * (1f - easing) + smooth * easing;
    }
}
