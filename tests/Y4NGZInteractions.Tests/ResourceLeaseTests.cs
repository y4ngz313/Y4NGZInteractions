using System;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests;

public sealed class ResourceLeaseTests
{
    [Fact]
    public void DefaultConflictPolicyIsRejectIfBusy()
    {
        Assert.Equal(
            InteractionAnimationConflictPolicy.RejectIfBusy,
            new InteractionAnimationRequest().ConflictPolicy);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    public void SameResourceRejectsByDefault(int kindValue)
    {
        var kind = (InteractionAnimationResourceKind)kindValue;
        var registry = new InteractionAnimationResourceLeaseRegistry();
        var owner = new object();
        InteractionAnimationHandle incumbent = InteractionAnimationHandle.NewHandle();
        var claim = new[] { new InteractionAnimationResourceClaim(kind, owner) };

        Assert.True(registry.TryAcquire(incumbent, claim, out _));
        Assert.False(registry.TryPlanAcquisition(
            claim,
            InteractionAnimationConflictPolicy.RejectIfBusy,
            out InteractionAnimationHandle[] conflicts,
            out string reason));

        Assert.Equal("interaction_resource_busy", reason);
        Assert.Single(conflicts);
        Assert.Equal(incumbent, conflicts[0]);
        Assert.True(registry.IsOwned(kind, owner, out InteractionAnimationHandle stillOwned));
        Assert.Equal(incumbent, stillOwned);
    }

    [Fact]
    public void InterruptPolicyPlansConflictWithoutMutatingIncumbent()
    {
        var registry = new InteractionAnimationResourceLeaseRegistry();
        var owner = new object();
        InteractionAnimationHandle incumbent = InteractionAnimationHandle.NewHandle();
        var claim = new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.LocalCameraAndArms, owner)
        };
        Assert.True(registry.TryAcquire(incumbent, claim, out _));

        Assert.True(registry.TryPlanAcquisition(
            claim,
            InteractionAnimationConflictPolicy.InterruptExisting,
            out InteractionAnimationHandle[] conflicts,
            out string reason));

        Assert.Equal(string.Empty, reason);
        Assert.Equal(new[] { incumbent }, conflicts);
        Assert.True(registry.IsOwned(
            InteractionAnimationResourceKind.LocalCameraAndArms,
            owner,
            out InteractionAnimationHandle stillOwned));
        Assert.Equal(incumbent, stillOwned);
    }

    [Fact]
    public void DifferentRemotePlayersCanOwnBodyResourcesConcurrently()
    {
        var registry = new InteractionAnimationResourceLeaseRegistry();
        var firstPlayer = new object();
        var secondPlayer = new object();
        InteractionAnimationHandle first = InteractionAnimationHandle.NewHandle();
        InteractionAnimationHandle second = InteractionAnimationHandle.NewHandle();

        Assert.True(registry.TryAcquire(first, new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, firstPlayer)
        }, out _));
        Assert.True(registry.TryAcquire(second, new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, secondPlayer)
        }, out _));

        Assert.True(registry.IsOwned(
            InteractionAnimationResourceKind.BodyAnimator, firstPlayer, out _));
        Assert.True(registry.IsOwned(
            InteractionAnimationResourceKind.BodyAnimator, secondPlayer, out _));
    }

    [Fact]
    public void LocalBodyClaimsBothBodyAndCameraResources()
    {
        var registry = new InteractionAnimationResourceLeaseRegistry();
        var localPlayer = new object();
        InteractionAnimationHandle body = InteractionAnimationHandle.NewHandle();
        var localBodyClaims = new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, localPlayer),
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.LocalCameraAndArms, localPlayer)
        };
        Assert.True(registry.TryAcquire(body, localBodyClaims, out _));

        Assert.False(registry.TryPlanAcquisition(new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.LocalCameraAndArms, localPlayer)
        }, InteractionAnimationConflictPolicy.RejectIfBusy, out _, out string viewReason));
        Assert.Equal("interaction_resource_busy", viewReason);

        Assert.False(registry.TryPlanAcquisition(new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, localPlayer)
        }, InteractionAnimationConflictPolicy.RejectIfBusy, out _, out string bodyReason));
        Assert.Equal("interaction_resource_busy", bodyReason);
    }

    [Fact]
    public void ReleaseAndClearRemoveEveryClaim()
    {
        var registry = new InteractionAnimationResourceLeaseRegistry();
        var owner = new object();
        InteractionAnimationHandle handle = InteractionAnimationHandle.NewHandle();
        Assert.True(registry.TryAcquire(handle, new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, owner),
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.LocalCameraAndArms, owner)
        }, out _));

        registry.Release(handle);
        Assert.False(registry.IsOwned(
            InteractionAnimationResourceKind.BodyAnimator, owner, out _));
        Assert.False(registry.IsOwned(
            InteractionAnimationResourceKind.LocalCameraAndArms, owner, out _));

        Assert.True(registry.TryAcquire(handle, Array.Empty<InteractionAnimationResourceClaim>(),
            out _));
        registry.Clear();
        Assert.True(registry.TryAcquire(handle, Array.Empty<InteractionAnimationResourceClaim>(),
            out _));
    }
}
