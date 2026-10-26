using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests;

public sealed class AnimatorLayerReplayTargetTests
{
    // Stand-ins for captured fullPathHash values; the selection only compares them with zero.
    private const int Idle1 = 201;
    private const int CrouchDown = 101;

    [Fact]
    public void SettledLayerReplaysItsCurrentState()
    {
        Assert.Equal(
            AnimatorLayerReplayTarget.Current,
            AnimatorStateSnapshot.SelectReplayTarget(
                currentStateHash: Idle1,
                nextStateHash: 0,
                inTransition: false,
                hasCurrentState: true,
                hasNextState: false));
    }

    [Fact]
    public void InTransitionCaptureReplaysTheDestinationInsteadOfRefusing()
    {
        Assert.Equal(
            AnimatorLayerReplayTarget.Next,
            AnimatorStateSnapshot.SelectReplayTarget(
                currentStateHash: Idle1,
                nextStateHash: CrouchDown,
                inTransition: true,
                hasCurrentState: true,
                hasNextState: true));
    }

    [Theory]
    [InlineData(CrouchDown, false)]
    [InlineData(0, true)]
    public void MissingDestinationFallsBackToTheCurrentState(int nextStateHash, bool hasNextState)
    {
        Assert.Equal(
            AnimatorLayerReplayTarget.Current,
            AnimatorStateSnapshot.SelectReplayTarget(
                currentStateHash: Idle1,
                nextStateHash,
                inTransition: true,
                hasCurrentState: true,
                hasNextState));
    }

    [Fact]
    public void NextStateIsIgnoredWhenTheLayerWasNotInTransition()
    {
        Assert.Equal(
            AnimatorLayerReplayTarget.Current,
            AnimatorStateSnapshot.SelectReplayTarget(
                currentStateHash: Idle1,
                nextStateHash: CrouchDown,
                inTransition: false,
                hasCurrentState: true,
                hasNextState: true));
    }

    [Theory]
    [InlineData(0, 0, false, true, false)]
    [InlineData(Idle1, 0, false, false, false)]
    [InlineData(Idle1, CrouchDown, true, false, false)]
    [InlineData(0, CrouchDown, true, true, false)]
    public void UnplayableCaptureIsRefused(
        int currentStateHash,
        int nextStateHash,
        bool inTransition,
        bool hasCurrentState,
        bool hasNextState)
    {
        Assert.Equal(
            AnimatorLayerReplayTarget.None,
            AnimatorStateSnapshot.SelectReplayTarget(
                currentStateHash,
                nextStateHash,
                inTransition,
                hasCurrentState,
                hasNextState));
    }
}
