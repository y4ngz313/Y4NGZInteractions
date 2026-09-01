using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class MovementParameterPolicyTests
{
    [Fact]
    public void StandingStillIsIdle()
    {
        Assert.Equal(
            MovementParameterPolicy.Idle,
            MovementParameterPolicy.Resolve(0f, sprinting: false, crouching: false));
    }

    [Fact]
    public void OrdinaryWalkIsMoveOne()
    {
        Assert.Equal(
            MovementParameterPolicy.Walking,
            MovementParameterPolicy.Resolve(3.8f, sprinting: false, crouching: false));
    }

    [Fact]
    public void SprintIsMoveTwoOnlyWithTheSprintInput()
    {
        Assert.Equal(
            MovementParameterPolicy.Sprinting,
            MovementParameterPolicy.Resolve(7.9f, sprinting: true, crouching: false));
        Assert.Equal(
            MovementParameterPolicy.Walking,
            MovementParameterPolicy.Resolve(7.9f, sprinting: false, crouching: false));
    }

    /// <summary>
    /// Vanilla cannot sprint out of a crouch, but isSprinting stays true while the sprint key is
    /// held — so a held key during a crouch-walk (~2.4 m/s) used to drive Hold -> Sprint and play
    /// the sprint arms clip.
    /// </summary>
    [Fact]
    public void CrouchWalkIsMoveOneEvenWithTheSprintKeyHeld()
    {
        Assert.Equal(
            MovementParameterPolicy.Walking,
            MovementParameterPolicy.Resolve(2.4f, sprinting: true, crouching: true));
        Assert.Equal(
            MovementParameterPolicy.Walking,
            MovementParameterPolicy.Resolve(2.4f, sprinting: false, crouching: true));
    }

    [Fact]
    public void CrouchedAndStationaryIsStillIdle()
    {
        Assert.Equal(
            MovementParameterPolicy.Idle,
            MovementParameterPolicy.Resolve(0.05f, sprinting: true, crouching: true));
    }

    [Theory]
    [InlineData(0.2f, MovementParameterPolicy.Idle)]
    [InlineData(0.21f, MovementParameterPolicy.Walking)]
    public void TheIdleThresholdIsUnchanged(float horizontalSpeed, int expected)
    {
        Assert.Equal(
            expected,
            MovementParameterPolicy.Resolve(horizontalSpeed, sprinting: false, crouching: false));
    }
}
