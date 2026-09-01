using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

public sealed class CameraStanceRestorePolicyTests
{
    private const float VanillaStandingRestX = 0f;
    // Vanilla standing rest Z in the player-local frame. The old 0.01 was derived without the
    // -0.368 CameraContainer hierarchy offset; -0.3545 is the measured rest.
    private const float VanillaStandingRestZ = -0.3545f;

    [Fact]
    public void CrouchedRestoreUsesCleanSessionEntryHorizontalPosition()
    {
        CameraHorizontalRestoreTarget target =
            CameraStanceRestorePolicy.ResolveHorizontalTarget(
                currentX: 0f,
                currentZ: -0.71f,
                sessionEntryX: 0f,
                sessionEntryZ: -0.3213f,
                hasSessionEntry: true,
                vanillaRestX: VanillaStandingRestX,
                vanillaRestZ: VanillaStandingRestZ,
                horizontalTolerance: 0.15f);

        Assert.Equal(0f, target.X, 5);
        Assert.Equal(-0.3213f, target.Z, 5);
        Assert.True(target.UsedSessionEntry);
    }

    [Fact]
    public void ContaminatedSessionEntryFallsBackToVanillaHorizontalRest()
    {
        CameraHorizontalRestoreTarget target =
            CameraStanceRestorePolicy.ResolveHorizontalTarget(
                currentX: 0f,
                currentZ: -0.71f,
                sessionEntryX: 0f,
                sessionEntryZ: 0.033543f,
                hasSessionEntry: true,
                vanillaRestX: VanillaStandingRestX,
                vanillaRestZ: VanillaStandingRestZ,
                horizontalTolerance: 0.15f);

        Assert.Equal(0f, target.X, 5);
        Assert.Equal(-0.3545f, target.Z, 5);
        Assert.False(target.UsedSessionEntry);
    }

    [Fact]
    public void MissingSessionEntryFallsBackToVanillaHorizontalRest()
    {
        CameraHorizontalRestoreTarget target =
            CameraStanceRestorePolicy.ResolveHorizontalTarget(
                currentX: 0f,
                currentZ: -0.71f,
                sessionEntryX: 0f,
                sessionEntryZ: 0f,
                hasSessionEntry: false,
                vanillaRestX: VanillaStandingRestX,
                vanillaRestZ: VanillaStandingRestZ,
                horizontalTolerance: 0.15f);

        Assert.Equal(0f, target.X, 5);
        Assert.Equal(-0.3545f, target.Z, 5);
        Assert.False(target.UsedSessionEntry);
    }

    [Theory]
    [InlineData(-0.3213f, true)]
    [InlineData(0.033543f, false)]
    public void HorizontalBaselineValidationRejectsPersistentCameraResidue(
        float sessionEntryZ,
        bool expectedUsable)
    {
        bool usable = CameraStanceRestorePolicy
            .IsSessionEntryHorizontalPositionUsable(
                sessionEntryX: 0f,
                sessionEntryZ,
                vanillaRestX: VanillaStandingRestX,
                vanillaRestZ: VanillaStandingRestZ,
                tolerance: 0.15f);

        Assert.Equal(expectedUsable, usable);
    }

    /// <summary>
    /// After the pristine chain restore the chain carries the authored STANDING defaults, so a
    /// crouched exit still needs the crouch rest height asserted.
    /// </summary>
    [Fact]
    public void CrouchedExitOnStandingChainStillNeedsCorrection()
    {
        bool needed = CameraStanceRestorePolicy.NeedsStanceCorrection(
            currentX: 0f,
            currentY: 2.359903f,
            currentZ: -0.3545f,
            targetX: 0f,
            targetY: 1.17f,
            targetZ: -0.3545f,
            heightTolerance: 0.15f,
            horizontalTolerance: 0.15f);

        Assert.True(needed);
    }

    /// <summary>
    /// The post-pristine correction must be conditional: an already-correct chain is never
    /// re-snapped, because each stop-time write is another chance to bake permanent residue.
    /// </summary>
    [Fact]
    public void ChainAlreadyOnTheStanceTargetNeedsNoCorrection()
    {
        bool needed = CameraStanceRestorePolicy.NeedsStanceCorrection(
            currentX: 0f,
            currentY: 1.2f,
            currentZ: -0.3445f,
            targetX: 0f,
            targetY: 1.17f,
            targetZ: -0.3545f,
            heightTolerance: 0.15f,
            horizontalTolerance: 0.15f);

        Assert.False(needed);
    }

    [Fact]
    public void HorizontalDeviationAloneStillNeedsCorrection()
    {
        bool needed = CameraStanceRestorePolicy.NeedsStanceCorrection(
            currentX: 0f,
            currentY: 1.17f,
            currentZ: -0.71f,
            targetX: 0f,
            targetY: 1.17f,
            targetZ: -0.3545f,
            heightTolerance: 0.15f,
            horizontalTolerance: 0.15f);

        Assert.True(needed);
    }

    /// <summary>
    /// The gameplay camera is a CHILD of the camera container and its pristine local position is
    /// the vanilla invariant. The -1.19 m offset the old world-position write baked in must be
    /// detected as residue; float noise on the chain must not be.
    /// </summary>
    [Theory]
    [InlineData(0f, -1.19f, 0f, true)]
    [InlineData(0f, -0.0002f, 0f, false)]
    [InlineData(0f, 0f, 0f, false)]
    [InlineData(0.005f, 0f, 0f, true)]
    public void CameraLocalResidueIsDetectedAtMillimeterScale(
        float deltaX,
        float deltaY,
        float deltaZ,
        bool expectedResidue)
    {
        bool residue = CameraStanceRestorePolicy.ExceedsResidueTolerance(
            deltaX,
            deltaY,
            deltaZ,
            toleranceMeters: 0.001f);

        Assert.Equal(expectedResidue, residue);
    }
}
