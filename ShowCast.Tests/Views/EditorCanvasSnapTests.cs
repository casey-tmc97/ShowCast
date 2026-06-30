using ShowCast.Views;
using Xunit;

namespace ShowCast.Tests.Views;

public class EditorCanvasSnapTests
{
    // irWidth=1920 → threshold = 8/1920 ≈ 0.004167
    // irHeight=1080 → threshold = 8/1080 ≈ 0.007407

    // ── SnapToGuideX ─────────────────────────────────────────────────────────

    [Fact]
    public void SnapToGuideX_LeftEdgeWithinThreshold_SnapsLeftEdgeToCenter()
    {
        // left edge at 0.499 → dist 0.001 < threshold → X becomes 0.5
        float result = EditorCanvas.SnapToGuideX(0.499f, 0.3f, 1920.0);
        Assert.Equal(0.5f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_RightEdgeWithinThreshold_SnapsRightEdgeToCenter()
    {
        // x=0.2, w=0.299 → right edge at 0.499 → dist 0.001 < threshold → X = 0.5 - 0.299 = 0.201
        float result = EditorCanvas.SnapToGuideX(0.2f, 0.299f, 1920.0);
        Assert.Equal(0.201f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_CenterWithinThreshold_CentersLayerHorizontally()
    {
        // x=0.301, w=0.4 → center at 0.501 → dist 0.001 < threshold → X = 0.5 - 0.2 = 0.3
        float result = EditorCanvas.SnapToGuideX(0.301f, 0.4f, 1920.0);
        Assert.Equal(0.3f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_NoCandidateWithinThreshold_ReturnsUnchanged()
    {
        // x=0.48, w=0.1 → left=0.48(dist 0.02), center=0.53(dist 0.03), right=0.58(dist 0.08) — all > threshold
        float result = EditorCanvas.SnapToGuideX(0.48f, 0.1f, 1920.0);
        Assert.Equal(0.48f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_MultipleCandidatesWithinThreshold_ClosestWins()
    {
        // x=0.4975, w=0.004 → left=0.4975(dist 0.0025), right=0.5015(dist 0.0015), center=0.4995(dist 0.0005)
        // center is closest → X = 0.5 - 0.002 = 0.498
        float result = EditorCanvas.SnapToGuideX(0.4975f, 0.004f, 1920.0);
        Assert.Equal(0.498f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideX_WideLayerLeftEdgeSnap_ResultClamped()
    {
        // w=0.502 → max valid X = 1 - 0.502 = 0.498
        // x=0.499, left edge dist=0.001 < threshold → snap fires → raw result=0.5
        // After caller clamps: layer.X = Math.Clamp(0.5f, 0f, 0.498f) = 0.498
        // This test verifies SnapToGuideX returns 0.5 (the caller is responsible for clamping)
        float result = EditorCanvas.SnapToGuideX(0.499f, 0.502f, 1920.0);
        Assert.Equal(0.5f, result, precision: 4); // raw snap result — caller clamps
    }

    // ── SnapToGuideY ─────────────────────────────────────────────────────────

    [Fact]
    public void SnapToGuideY_TopEdgeWithinThreshold_SnapsTopEdgeToCenter()
    {
        // top edge at 0.499 → dist 0.001 < threshold → Y becomes 0.5
        float result = EditorCanvas.SnapToGuideY(0.499f, 0.3f, 1080.0);
        Assert.Equal(0.5f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_BottomEdgeWithinThreshold_SnapsBottomEdgeToCenter()
    {
        // y=0.2, h=0.299 → bottom at 0.499 → dist 0.001 < threshold → Y = 0.5 - 0.299 = 0.201
        float result = EditorCanvas.SnapToGuideY(0.2f, 0.299f, 1080.0);
        Assert.Equal(0.201f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_CenterWithinThreshold_CentersLayerVertically()
    {
        // y=0.301, h=0.4 → center at 0.501 → dist 0.001 < threshold → Y = 0.5 - 0.2 = 0.3
        float result = EditorCanvas.SnapToGuideY(0.301f, 0.4f, 1080.0);
        Assert.Equal(0.3f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_NoCandidateWithinThreshold_ReturnsUnchanged()
    {
        // y=0.46, h=0.05 → top=0.46(dist 0.04), bottom=0.51(dist 0.01), center=0.485(dist 0.015)
        // threshold=0.007407 — bottom dist 0.01 > threshold
        float result = EditorCanvas.SnapToGuideY(0.46f, 0.05f, 1080.0);
        Assert.Equal(0.46f, result, precision: 4);
    }

    [Fact]
    public void SnapToGuideY_MultipleCandidatesWithinThreshold_ClosestWins()
    {
        // y=0.4965, h=0.004 → top=0.4965(dist 0.0035), bottom=0.5005(dist 0.0005), center=0.4985(dist 0.0015)
        // bottom is closest → Y = 0.5 - 0.004 = 0.496
        float result = EditorCanvas.SnapToGuideY(0.4965f, 0.004f, 1080.0);
        Assert.Equal(0.496f, result, precision: 4);
    }
}
