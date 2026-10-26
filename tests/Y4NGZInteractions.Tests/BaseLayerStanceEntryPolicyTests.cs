using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class BaseLayerStanceEntryPolicyTests
{
    // Stand-ins for Animator.StringToHash values; the policies only ever compare them.
    private const int CrouchDown = 101;
    private const int CrouchIdle = 102;
    private const int CrouchWalk = 103;
    private const int Idle1 = 201;
    private const int Walk = 202;

    private static BaseLayerStanceEntryAction Resolve(
        bool crouching,
        bool baseStateReplayed,
        int replayedStateHash,
        bool crouchIdleAvailable = true,
        bool crouchingKnown = true)
    {
        return BaseLayerStanceEntryPolicy.Resolve(
            crouchingKnown,
            crouching,
            baseStateReplayed,
            CrouchStateResyncPolicy.IsCrouchClusterState(
                replayedStateHash,
                CrouchDown,
                CrouchIdle,
                CrouchWalk),
            crouchIdleAvailable);
    }

    [Fact]
    public void CrouchedSwapWithoutAReplayEntersCrouchIdleInstantly()
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.PlayCrouchIdle,
            Resolve(crouching: true, baseStateReplayed: false, replayedStateHash: 0));
    }

    [Theory]
    [InlineData(Idle1)]
    [InlineData(Walk)]
    public void ReplayedStandingStateUnderACrouchedPlayerStillEntersCrouchIdle(int replayedState)
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.PlayCrouchIdle,
            Resolve(crouching: true, baseStateReplayed: true, replayedStateHash: replayedState));
    }

    [Theory]
    [InlineData(CrouchDown)]
    [InlineData(CrouchIdle)]
    [InlineData(CrouchWalk)]
    public void ReplayedCrouchStateIsLeftInPlace(int replayedState)
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.None,
            Resolve(crouching: true, baseStateReplayed: true, replayedStateHash: replayedState));
    }

    [Fact]
    public void CrouchHashThatWasNotActuallyReplayedDoesNotCount()
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.PlayCrouchIdle,
            Resolve(crouching: true, baseStateReplayed: false, replayedStateHash: CrouchIdle));
    }

    [Theory]
    [InlineData(false, 0)]
    [InlineData(true, Idle1)]
    public void ControllerWithoutCrouchIdleFallsBackToTheTrigger(
        bool baseStateReplayed,
        int replayedState)
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.FireStartCrouchingTrigger,
            Resolve(
                crouching: true,
                baseStateReplayed,
                replayedState,
                crouchIdleAvailable: false));
    }

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, Idle1, true)]
    [InlineData(true, CrouchIdle, true)]
    [InlineData(true, CrouchWalk, false)]
    [InlineData(false, 0, false)]
    public void StandingPlayerNeverGetsAStanceEntry(
        bool baseStateReplayed,
        int replayedState,
        bool crouchIdleAvailable)
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.None,
            Resolve(
                crouching: false,
                baseStateReplayed,
                replayedState,
                crouchIdleAvailable));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void UnknownStanceNeverGetsAStanceEntry(bool crouchIdleAvailable)
    {
        Assert.Equal(
            BaseLayerStanceEntryAction.None,
            Resolve(
                crouching: true,
                baseStateReplayed: false,
                replayedStateHash: 0,
                crouchIdleAvailable,
                crouchingKnown: false));
    }
}
