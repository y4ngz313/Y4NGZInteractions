using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests;

/// <summary>
/// Contract: the envelope is instantaneous, so it is telemetry plus PURE MAGNITUDE only. It
/// never stops for a stance mismatch — that reading is true at t=0 of every legitimate
/// crouch/stand transition, and stopping there killed legitimate transitions. Stance damage is
/// owned by the debounced stance viewpoint invariant. Only a reading beyond the threshold from
/// BOTH stance rests is displacement worth stopping for.
/// </summary>
public sealed class CameraRestEnvelopePolicyTests
{
    private const float RestX = 0f;
    // Vanilla standing rest in the player-local frame the guard measures in. The old 0.01 was
    // derived without the -0.368 CameraContainer hierarchy offset.
    private const float RestZ = -0.3545f;
    private const float StandingRestHeight = 2.351f;
    private const float CrouchedRestHeight = 1.17f;
    private const float Threshold = 1.25f;
    private const float StanceTolerance = 0.15f;

    private static CameraRestEnvelopeDecision Evaluate(
        float y,
        bool crouching,
        bool crouchingKnown = true,
        float x = RestX,
        float z = RestZ)
    {
        return CameraRestEnvelopePolicy.Evaluate(
            x,
            y,
            z,
            RestX,
            RestZ,
            StandingRestHeight,
            CrouchedRestHeight,
            crouchingKnown,
            crouching,
            Threshold,
            StanceTolerance);
    }

    [Fact]
    public void StandingSessionAtStandingRestStaysWhitelisted()
    {
        CameraRestEnvelopeDecision decision = Evaluate(StandingRestHeight, crouching: false);

        Assert.True(decision.Whitelisted);
        Assert.Equal("settled_on_stance_rest", decision.Basis);
        Assert.Equal(StandingRestHeight, decision.StanceRestHeight, 3);
    }

    [Fact]
    public void CrouchedSessionAtCrouchedRestStaysWhitelisted()
    {
        CameraRestEnvelopeDecision decision = Evaluate(CrouchedRestHeight, crouching: true);

        Assert.True(decision.Whitelisted);
        Assert.Equal("settled_on_stance_rest", decision.Basis);
        Assert.Equal(CrouchedRestHeight, decision.StanceRestHeight, 3);
    }

    /// <summary>
    /// t=0 of a legitimate crouch: isCrouching has flipped and the camera is by definition still
    /// at standing rest. The envelope must continue and let the debounced invariant decide.
    /// </summary>
    [Fact]
    public void CrouchTransitionAtTimeZeroContinues()
    {
        CameraRestEnvelopeDecision decision = Evaluate(StandingRestHeight, crouching: true);

        Assert.True(decision.Whitelisted);
        Assert.Equal("stance_mismatched_rest_height", decision.Basis);
    }

    /// <summary>
    /// The same at t=0 of a legitimate stand-up: the camera is still at crouched rest.
    /// </summary>
    [Fact]
    public void StandTransitionAtTimeZeroContinues()
    {
        CameraRestEnvelopeDecision decision = Evaluate(CrouchedRestHeight, crouching: false);

        Assert.True(decision.Whitelisted);
        Assert.Equal("stance_mismatched_rest_height", decision.Basis);
    }

    /// <summary>
    /// A viewpoint parked at the OPPOSITE stance rest is real damage, but the envelope cannot
    /// tell it apart from t=0 of a transition without history, so it continues here. The
    /// debounced stance invariant (0.5 s sustained) owns that stop.
    /// </summary>
    [Fact]
    public void ParkedAtOppositeStanceRestContinuesFromTheEnvelope()
    {
        Assert.True(Evaluate(StandingRestHeight, crouching: true).Whitelisted);
        Assert.True(Evaluate(CrouchedRestHeight, crouching: false).Whitelisted);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(1.76f)]
    [InlineData(2.0f)]
    public void CrouchGlideInFlightStaysWhitelisted(float y)
    {
        CameraRestEnvelopeDecision decision = Evaluate(y, crouching: true);

        Assert.True(decision.Whitelisted);
        Assert.Equal("stance_glide_in_flight", decision.Basis);
    }

    [Theory]
    [InlineData(1.5f)]
    [InlineData(1.76f)]
    [InlineData(2.0f)]
    public void StandGlideInFlightStaysWhitelisted(float y)
    {
        CameraRestEnvelopeDecision decision = Evaluate(y, crouching: false);

        Assert.True(decision.Whitelisted);
        Assert.Equal("stance_glide_in_flight", decision.Basis);
    }

    /// <summary>
    /// The pure-magnitude protection this policy exists to keep: a teleport or turret grab lands
    /// beyond the threshold from BOTH stance rests and is never whitelisted.
    /// </summary>
    [Fact]
    public void DisplacementFarFromBothStanceRestsIsNeverWhitelisted()
    {
        CameraRestEnvelopeDecision decision = Evaluate(StandingRestHeight, crouching: false, x: 4f, z: 3f);

        Assert.False(decision.Whitelisted);
        Assert.Equal("outside_stance_rest_envelope", decision.Basis);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void DisplacementBelowBothStanceRestsIsNeverWhitelisted(bool crouching)
    {
        CameraRestEnvelopeDecision decision = Evaluate(-0.5f, crouching);

        Assert.False(decision.Whitelisted);
        Assert.Equal("outside_stance_rest_envelope", decision.Basis);
    }

    /// <summary>
    /// The churn case from the failed run: a stance-height mismatch plus a little horizontal
    /// offset pushed the reading past the threshold from the CURRENT stance rest, which used to
    /// stop the session as 'outside_stance_rest_envelope'. Measured against both rests it is
    /// plainly still stance behaviour.
    /// </summary>
    [Fact]
    public void StanceMismatchWithHorizontalChurnContinues()
    {
        CameraRestEnvelopeDecision decision = Evaluate(StandingRestHeight, crouching: true, z: -0.85f);

        Assert.True(decision.DistanceFromStanceRest > Threshold);
        Assert.True(decision.DistanceFromOppositeStanceRest <= Threshold);
        Assert.True(decision.Whitelisted);
        Assert.Equal("stance_mismatched_rest_height", decision.Basis);
    }

    [Fact]
    public void UnreadableStanceKeepsThePlainStandingRestRadius()
    {
        CameraRestEnvelopeDecision decision =
            Evaluate(StandingRestHeight, crouching: true, crouchingKnown: false);

        Assert.True(decision.Whitelisted);
        Assert.Equal("stance_unknown_vanilla_rest_envelope", decision.Basis);
        Assert.Equal(StandingRestHeight, decision.StanceRestHeight, 3);
    }

    [Fact]
    public void UnreadableStanceStillRejectsAnOutOfRangeCamera()
    {
        CameraRestEnvelopeDecision decision =
            Evaluate(0.2f, crouching: true, crouchingKnown: false);

        Assert.False(decision.Whitelisted);
        Assert.Equal("outside_stance_rest_envelope", decision.Basis);
    }
}
