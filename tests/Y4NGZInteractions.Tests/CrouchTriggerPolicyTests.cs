using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class CrouchTriggerPolicyTests
{
    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void LocalPlayerEdgesRemainOwnedByVanilla(
        bool previousCrouching,
        bool currentCrouching)
    {
        CrouchTriggerAction action = CrouchTriggerPolicy.ResolveEdgeAction(
            isLocalPlayer: true,
            hasPreviousState: true,
            previousCrouching,
            currentCrouching);

        Assert.Equal(CrouchTriggerAction.None, action);
    }

    [Fact]
    public void RemoteCrouchEdgeIsSynthesized()
    {
        CrouchTriggerAction action = CrouchTriggerPolicy.ResolveEdgeAction(
            isLocalPlayer: false,
            hasPreviousState: true,
            previousCrouching: false,
            currentCrouching: true);

        Assert.Equal(CrouchTriggerAction.Fire, action);
    }

    [Fact]
    public void RemoteStandEdgeResetsLatchedTrigger()
    {
        CrouchTriggerAction action = CrouchTriggerPolicy.ResolveEdgeAction(
            isLocalPlayer: false,
            hasPreviousState: true,
            previousCrouching: true,
            currentCrouching: false);

        Assert.Equal(CrouchTriggerAction.Reset, action);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedRemoteStateDoesNotTouchTrigger(bool crouching)
    {
        CrouchTriggerAction action = CrouchTriggerPolicy.ResolveEdgeAction(
            isLocalPlayer: false,
            hasPreviousState: true,
            previousCrouching: crouching,
            currentCrouching: crouching);

        Assert.Equal(CrouchTriggerAction.None, action);
    }
}
