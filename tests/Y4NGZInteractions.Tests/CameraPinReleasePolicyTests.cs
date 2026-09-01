using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class CameraPinReleasePolicyTests
{
    [Fact]
    public void DeferredWindowIsTwoLateUpdatesAndReleasesExactlyOnce()
    {
        int remaining = CameraPinReleasePolicy.ResolveScheduledLateUpdates(
            CameraPinReleasePolicy.DeferredReleaseLateUpdates);
        Assert.Equal(2, remaining);

        CameraPinReleaseStep first = CameraPinReleasePolicy.AdvanceRelease(remaining);
        Assert.False(first.ReleaseNow);
        Assert.Equal(1, first.RemainingLateUpdates);

        CameraPinReleaseStep second =
            CameraPinReleasePolicy.AdvanceRelease(first.RemainingLateUpdates);
        Assert.True(second.ReleaseNow);
        Assert.Equal(0, second.RemainingLateUpdates);
    }

    [Fact]
    public void UnscheduledPinNeverReleases()
    {
        CameraPinReleaseStep step = CameraPinReleasePolicy.AdvanceRelease(-1);

        Assert.False(step.ReleaseNow);
        Assert.Equal(-1, step.RemainingLateUpdates);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void ZeroOrNegativeRequestStillSchedulesOneLateUpdate(int requested)
    {
        int remaining = CameraPinReleasePolicy.ResolveScheduledLateUpdates(requested);

        Assert.Equal(1, remaining);
        Assert.True(CameraPinReleasePolicy.AdvanceRelease(remaining).ReleaseNow);
    }

    [Fact]
    public void ContainerCorrectionIsAllowedForACameraParentedToTheContainer()
    {
        Assert.True(CameraPinReleasePolicy.CanCorrectAtContainerLevel(
            hasContainer: true,
            containerIsCamera: false,
            cameraIsChildOfContainer: true));
    }

    [Theory]
    // No container at all: nothing safe to translate.
    [InlineData(false, false, false)]
    // The container resolves to the camera itself: a container write would be a camera write.
    [InlineData(true, true, true)]
    // Container is not an ancestor: translating it would not move the camera.
    [InlineData(true, false, false)]
    public void ContainerCorrectionIsRefusedWhenTheChainCannotCarryIt(
        bool hasContainer,
        bool containerIsCamera,
        bool cameraIsChildOfContainer)
    {
        Assert.False(CameraPinReleasePolicy.CanCorrectAtContainerLevel(
            hasContainer,
            containerIsCamera,
            cameraIsChildOfContainer));
    }
}
