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

    [Fact]
    public void RestoreEntryNotNeededWhenAssertionIsNotRequired()
    {
        Assert.Equal(
            CrouchRestoreEntryOutcome.NotNeeded,
            CrouchTriggerPolicy.ResolveRestoreEntryOutcome(
                shouldAssertRestoreEntry: false,
                landedInCrouchCluster: true));
        Assert.Equal(
            CrouchRestoreEntryOutcome.NotNeeded,
            CrouchTriggerPolicy.ResolveRestoreEntryOutcome(
                shouldAssertRestoreEntry: false,
                landedInCrouchCluster: false));
    }

    [Fact]
    public void RestoreEntryPrefersInstantClusterEntryOverTheCrossfadingTrigger()
    {
        Assert.Equal(
            CrouchRestoreEntryOutcome.InstantEntry,
            CrouchTriggerPolicy.ResolveRestoreEntryOutcome(
                shouldAssertRestoreEntry: true,
                landedInCrouchCluster: true));
    }

    [Fact]
    public void RestoreEntryFallsBackToTriggerWhenInstantEntryDidNotLand()
    {
        Assert.Equal(
            CrouchRestoreEntryOutcome.TriggerFallback,
            CrouchTriggerPolicy.ResolveRestoreEntryOutcome(
                shouldAssertRestoreEntry: true,
                landedInCrouchCluster: false));
    }

    [Fact]
    public void RestoreFallbackAssertsLocalCrouchWhenReplayIsUnavailable()
    {
        Assert.True(CrouchTriggerPolicy.ShouldAssertRestoreEntry(
            preserveLiveBaseLayer: true,
            liveBaseLayerStateReplayed: false,
            currentCrouchingKnown: true,
            currentCrouching: true,
            entryAlreadyMirrored: false));
    }

    [Theory]
    [InlineData(true, false, true, true, true)]
    [InlineData(true, true, true, true, false)]
    [InlineData(true, false, true, false, false)]
    [InlineData(false, false, true, true, false)]
    public void RestoreFallbackNeverDuplicatesOrOverridesAResolvedState(
        bool preserveLiveBaseLayer,
        bool liveBaseLayerStateReplayed,
        bool currentCrouchingKnown,
        bool currentCrouching,
        bool entryAlreadyMirrored)
    {
        Assert.False(CrouchTriggerPolicy.ShouldAssertRestoreEntry(
            preserveLiveBaseLayer,
            liveBaseLayerStateReplayed,
            currentCrouchingKnown,
            currentCrouching,
            entryAlreadyMirrored));
    }
}
