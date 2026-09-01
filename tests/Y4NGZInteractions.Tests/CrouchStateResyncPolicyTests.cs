using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class CrouchStateResyncPolicyTests
{
    // Stand-ins for Animator.StringToHash values; the policy only ever compares them.
    private const int CrouchDown = 101;
    private const int CrouchIdle = 102;
    private const int CrouchWalk = 103;
    private const int Walk = 201;
    private const int FallNoJump = 202;

    private const int MinFrames = CrouchStateResyncPolicy.DefaultMinimumFramesBetweenFires;

    private static CrouchStateResyncAction Resolve(
        bool crouching,
        int stateHash,
        bool isInTransition,
        int frame,
        ref CrouchStateResyncDebounce debounce,
        bool crouchingKnown = true,
        bool sessionActive = true,
        bool baseLayerStateKnown = true)
    {
        return CrouchStateResyncPolicy.Resolve(
            sessionActive,
            crouchingKnown,
            crouching,
            baseLayerStateKnown,
            CrouchStateResyncPolicy.IsCrouchClusterState(
                stateHash,
                CrouchDown,
                CrouchIdle,
                CrouchWalk),
            isInTransition,
            stateHash,
            frame,
            MinFrames,
            ref debounce);
    }

    [Theory]
    [InlineData(CrouchDown, true)]
    [InlineData(CrouchIdle, true)]
    [InlineData(CrouchWalk, true)]
    [InlineData(Walk, false)]
    [InlineData(FallNoJump, false)]
    public void CrouchClusterMembershipIsHashExact(int stateHash, bool expected)
    {
        Assert.Equal(
            expected,
            CrouchStateResyncPolicy.IsCrouchClusterState(
                stateHash,
                CrouchDown,
                CrouchIdle,
                CrouchWalk));
    }

    [Fact]
    public void CrouchedAndEjectedFromClusterFiresStartCrouching()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));
    }

    [Fact]
    public void CrouchedInsideClusterDoesNothing()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: true, CrouchWalk, isInTransition: false, frame: 10, ref debounce));
    }

    [Fact]
    public void StandingPlayerNeverFires()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: false, Walk, isInTransition: false, frame: 10, ref debounce));
    }

    [Fact]
    public void UnreadableStanceNeverFires()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(
                crouching: true,
                Walk,
                isInTransition: false,
                frame: 10,
                ref debounce,
                crouchingKnown: false));
    }

    [Fact]
    public void InactiveSessionNeverFires()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(
                crouching: true,
                Walk,
                isInTransition: false,
                frame: 10,
                ref debounce,
                sessionActive: false));
    }

    [Fact]
    public void MidTransitionNeverFires()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: true, Walk, isInTransition: true, frame: 10, ref debounce));
    }

    [Fact]
    public void UnknownBaseLayerStateNeverFires()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(
                crouching: true,
                Walk,
                isInTransition: false,
                frame: 10,
                ref debounce,
                baseLayerStateKnown: false));
    }

    [Fact]
    public void FiredTriggerIsNotRepeatedWhileItsTransitionIsPending()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));

        // Still the same ejected state on the next several frames: no second trigger.
        for (int frame = 11; frame < 10 + MinFrames; frame++)
        {
            Assert.Equal(
                CrouchStateResyncAction.None,
                Resolve(crouching: true, Walk, isInTransition: false, frame, ref debounce));
        }
    }

    [Fact]
    public void SwallowedTriggerIsRetriedAfterTheFrameGate()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(
                crouching: true,
                Walk,
                isInTransition: false,
                frame: 10 + MinFrames,
                ref debounce));
    }

    [Fact]
    public void ANewEjectedStateFiresImmediately()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, FallNoJump, isInTransition: false, frame: 11, ref debounce));
    }

    [Fact]
    public void ReturningToTheClusterRearmsTheNextEjection()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: true, CrouchIdle, isInTransition: false, frame: 12, ref debounce));
        // Ejected to the same state again, well inside the frame gate: it must fire again
        // because the trigger demonstrably landed in between.
        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 13, ref debounce));
    }

    [Fact]
    public void StandingUpClearsTheDebounce()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: false, Walk, isInTransition: false, frame: 11, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 12, ref debounce));
    }

    [Fact]
    public void MidTransitionDoesNotClearAPendingFire()
    {
        CrouchStateResyncDebounce debounce = default;

        Assert.Equal(
            CrouchStateResyncAction.FireStartCrouching,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 10, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: true, Walk, isInTransition: true, frame: 11, ref debounce));
        Assert.Equal(
            CrouchStateResyncAction.None,
            Resolve(crouching: true, Walk, isInTransition: false, frame: 12, ref debounce));
    }
}
