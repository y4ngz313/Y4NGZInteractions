using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class AnimatorPlaybackRateMathTests
{
    [Fact]
    public void OneAuthoredCycleOverClipLengthMeasuresRealtimePlayback()
    {
        bool measured = AnimatorPlaybackRateMath.TryMeasure(
            startNormalizedTime: 0.25f,
            endNormalizedTime: 1.25f,
            wallSeconds: 2.7d,
            stateLengthSeconds: 2.7f,
            animatorSpeed: 1f,
            stateSpeed: 1f,
            stateSpeedMultiplier: 1f,
            out AnimatorPlaybackRateMeasurement result);

        Assert.True(measured);
        Assert.Equal(1d, result.NormalizedCyclesAdvanced, 6);
        Assert.Equal(1, result.CompletedCycles);
        Assert.Equal(2.7d, result.ClipSecondsAdvanced, 6);
        Assert.Equal(1d, result.EffectiveClipSecondsPerWallSecond, 6);
        Assert.Equal(1d, result.ExpectedClipSecondsPerWallSecond, 6);
        Assert.Equal(1d, result.EffectiveToExpectedRatio, 6);
    }

    [Fact]
    public void HalfSpeedProgressionReportsHalfRealtimePlayback()
    {
        bool measured = AnimatorPlaybackRateMath.TryMeasure(
            startNormalizedTime: 0.25f,
            endNormalizedTime: 0.75f,
            wallSeconds: 2.7d,
            stateLengthSeconds: 2.7f,
            animatorSpeed: 1f,
            stateSpeed: 0.5f,
            stateSpeedMultiplier: 1f,
            out AnimatorPlaybackRateMeasurement result);

        Assert.True(measured);
        Assert.Equal(0.5d, result.NormalizedCyclesAdvanced, 6);
        Assert.Equal(0, result.CompletedCycles);
        Assert.Equal(0.5d, result.EffectiveClipSecondsPerWallSecond, 6);
        Assert.Equal(0.5d, result.ExpectedClipSecondsPerWallSecond, 6);
        Assert.Equal(1d, result.EffectiveToExpectedRatio, 6);
    }

    [Fact]
    public void ZeroWallClockWindowIsRejected()
    {
        Assert.False(AnimatorPlaybackRateMath.TryMeasure(
            startNormalizedTime: 0f,
            endNormalizedTime: 1f,
            wallSeconds: 0d,
            stateLengthSeconds: 2.7f,
            animatorSpeed: 1f,
            stateSpeed: 1f,
            stateSpeedMultiplier: 1f,
            out _));
    }
}
