using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class StanceViewpointGuardMathTests
{
    [Fact]
    public void ThirtyHighRefreshTicksDoNotExhaustWallTimeDebounce()
    {
        float mismatchSeconds = 0f;
        bool stop = false;

        for (int i = 0; i < 30; i++)
        {
            stop = StanceViewpointGuardMath.HasSustainedMismatch(
                stanceChanged: false,
                exempt: false,
                heightDeviation: 1.18f,
                tolerance: 0.15f,
                deltaSeconds: 1f / 240f,
                requiredSeconds: 0.5f,
                ref mismatchSeconds);
        }

        Assert.False(stop);
        Assert.InRange(mismatchSeconds, 0.124f, 0.126f);
    }

    [Theory]
    [InlineData(60)]
    [InlineData(120)]
    [InlineData(240)]
    public void PersistentMismatchTripsAtSameWallTimeAcrossFrameRates(int framesPerSecond)
    {
        float mismatchSeconds = 0f;
        int ticks = 0;
        bool stop;

        do
        {
            ticks++;
            stop = StanceViewpointGuardMath.HasSustainedMismatch(
                stanceChanged: false,
                exempt: false,
                heightDeviation: 1.18f,
                tolerance: 0.15f,
                deltaSeconds: 1f / framesPerSecond,
                requiredSeconds: 0.5f,
                ref mismatchSeconds);
        }
        while (!stop && ticks < framesPerSecond);

        Assert.True(stop);
        Assert.InRange(mismatchSeconds, 0.5f, 0.5f + 1f / framesPerSecond);
    }

    [Fact]
    public void StanceChangeAndSettledHeightResetAccumulatedMismatch()
    {
        float mismatchSeconds = 0.4f;

        Assert.False(StanceViewpointGuardMath.HasSustainedMismatch(
            stanceChanged: true,
            exempt: false,
            heightDeviation: 1.18f,
            tolerance: 0.15f,
            deltaSeconds: 0.1f,
            requiredSeconds: 0.5f,
            ref mismatchSeconds));
        Assert.Equal(0f, mismatchSeconds);

        mismatchSeconds = 0.4f;
        Assert.False(StanceViewpointGuardMath.HasSustainedMismatch(
            stanceChanged: false,
            exempt: false,
            heightDeviation: 0.1f,
            tolerance: 0.15f,
            deltaSeconds: 0.1f,
            requiredSeconds: 0.5f,
            ref mismatchSeconds));
        Assert.Equal(0f, mismatchSeconds);
    }
}
