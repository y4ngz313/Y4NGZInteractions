using System;
using Xunit;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions.Tests;

public sealed class ResourceLeaseMatrixTests
{
    [Theory]
    [InlineData(0, true)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(6, false)]
    [InlineData(7, false)]
    public void EveryPresentationClaimCombinationHonorsBothPolicies(
        int scenario,
        bool expectedConflict)
    {
        var localPlayer = new object();
        var firstRemote = new object();
        var secondRemote = new object();
        GetClaims(
            scenario,
            localPlayer,
            firstRemote,
            secondRemote,
            out InteractionAnimationResourceClaim[] incumbentClaims,
            out InteractionAnimationResourceClaim[] requestedClaims);

        foreach (InteractionAnimationConflictPolicy policy in Enum.GetValues(
                     typeof(InteractionAnimationConflictPolicy)))
        {
            var registry = new InteractionAnimationResourceLeaseRegistry();
            InteractionAnimationHandle incumbent = InteractionAnimationHandle.NewHandle();
            Assert.True(registry.TryAcquire(incumbent, incumbentClaims, out _));

            bool planned = registry.TryPlanAcquisition(
                requestedClaims,
                policy,
                out InteractionAnimationHandle[] conflicts,
                out string reason);

            if (!expectedConflict)
            {
                Assert.True(planned);
                Assert.Empty(conflicts);
                Assert.Equal(string.Empty, reason);
            }
            else if (policy == InteractionAnimationConflictPolicy.RejectIfBusy)
            {
                Assert.False(planned);
                Assert.Equal(new[] { incumbent }, conflicts);
                Assert.Equal("interaction_resource_busy", reason);
            }
            else
            {
                Assert.True(planned);
                Assert.Equal(new[] { incumbent }, conflicts);
                Assert.Equal(string.Empty, reason);
            }
        }
    }

    private static void GetClaims(
        int scenario,
        object localPlayer,
        object firstRemote,
        object secondRemote,
        out InteractionAnimationResourceClaim[] incumbent,
        out InteractionAnimationResourceClaim[] requested)
    {
        InteractionAnimationResourceClaim[] localView = Camera(localPlayer);
        InteractionAnimationResourceClaim[] localBody = BodyAndCamera(localPlayer);
        InteractionAnimationResourceClaim[] remoteBody =
            Body(firstRemote);
        InteractionAnimationResourceClaim[] otherRemoteBody =
            Body(secondRemote);

        switch (scenario)
        {
            case 0:
                incumbent = localView;
                requested = localView;
                return;
            case 1:
                incumbent = localView;
                requested = localBody;
                return;
            case 2:
                incumbent = localBody;
                requested = localView;
                return;
            case 3:
                incumbent = localBody;
                requested = localBody;
                return;
            case 4:
                incumbent = remoteBody;
                requested = remoteBody;
                return;
            case 5:
                incumbent = remoteBody;
                requested = otherRemoteBody;
                return;
            case 6:
                incumbent = localView;
                requested = remoteBody;
                return;
            case 7:
                incumbent = localBody;
                requested = remoteBody;
                return;
            default:
                throw new ArgumentOutOfRangeException(nameof(scenario));
        }
    }

    private static InteractionAnimationResourceClaim[] Camera(object player)
    {
        return new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.LocalCameraAndArms, player)
        };
    }

    private static InteractionAnimationResourceClaim[] Body(object player)
    {
        return new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, player)
        };
    }

    private static InteractionAnimationResourceClaim[] BodyAndCamera(object player)
    {
        return new[]
        {
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.BodyAnimator, player),
            new InteractionAnimationResourceClaim(
                InteractionAnimationResourceKind.LocalCameraAndArms, player)
        };
    }
}
