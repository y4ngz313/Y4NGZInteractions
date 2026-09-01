using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// The metarig stance enforcement may only run while a session is active, the player is
    /// known to be standing, and the base layer is outside the crouch cluster; it engages
    /// hysteretically (a real sag to engage, glide finishes at rest), steps at the configured
    /// speed, and never pushes the arms down.
    /// </summary>
    public class ArmsMetarigStanceEnforcementPolicyTests
    {
        [Fact]
        public void EligibleOnlyWhileStandingOutsideTheCrouchCluster()
        {
            Assert.True(ArmsMetarigStanceEnforcementPolicy.IsEligible(
                sessionActive: true, crouchingKnown: true, crouching: false,
                insideCrouchCluster: false));

            Assert.False(ArmsMetarigStanceEnforcementPolicy.IsEligible(
                sessionActive: false, crouchingKnown: true, crouching: false,
                insideCrouchCluster: false));
            Assert.False(ArmsMetarigStanceEnforcementPolicy.IsEligible(
                sessionActive: true, crouchingKnown: false, crouching: false,
                insideCrouchCluster: false));
            Assert.False(ArmsMetarigStanceEnforcementPolicy.IsEligible(
                sessionActive: true, crouchingKnown: true, crouching: true,
                insideCrouchCluster: false));
            Assert.False(ArmsMetarigStanceEnforcementPolicy.IsEligible(
                sessionActive: true, crouchingKnown: true, crouching: false,
                insideCrouchCluster: true));
        }

        [Fact]
        public void EngageThresholdSplitsCrouchCaptureFromAuthoredStandingPoses()
        {
            // The failure class (a crouch-poisoned write-defaults capture) sags 1.087 m in the
            // parent-local frame (authored 2.104 standing vs 1.017 crouched); the deepest
            // legitimately authored standing-eligible poses (seated/vehicle clips at 1.752)
            // sag 0.352 m. The default threshold must engage on the former, never the latter.
            Assert.True(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: false, sagMeters: 1.087f,
                engageSagMeters: ArmsMetarigStanceEnforcementPolicy.DefaultEngageSagMeters,
                releaseSagMeters: ArmsMetarigStanceEnforcementPolicy.DefaultReleaseSagMeters));
            Assert.False(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: false, sagMeters: 0.352f,
                engageSagMeters: ArmsMetarigStanceEnforcementPolicy.DefaultEngageSagMeters,
                releaseSagMeters: ArmsMetarigStanceEnforcementPolicy.DefaultReleaseSagMeters));

            // Round 7's phantom sag — the player-local misread (2.3599) of the healthy
            // parent-local rest (2.104) — must stay well under the default threshold.
            Assert.False(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: false, sagMeters: 0.256f,
                engageSagMeters: ArmsMetarigStanceEnforcementPolicy.DefaultEngageSagMeters,
                releaseSagMeters: ArmsMetarigStanceEnforcementPolicy.DefaultReleaseSagMeters));
        }

        [Fact]
        public void DisengagedEpisodeNeedsARealSagToEngage()
        {
            Assert.False(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: false, sagMeters: 0.05f,
                engageSagMeters: 0.1f, releaseSagMeters: 0.005f));
            Assert.True(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: false, sagMeters: 1.2f,
                engageSagMeters: 0.1f, releaseSagMeters: 0.005f));
        }

        [Fact]
        public void EngagedEpisodeFinishesAtRestInsteadOfHoveringOneDeadzoneLow()
        {
            Assert.True(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: true, sagMeters: 0.05f,
                engageSagMeters: 0.1f, releaseSagMeters: 0.005f));
            Assert.False(ArmsMetarigStanceEnforcementPolicy.AdvanceEngagement(
                currentlyEngaged: true, sagMeters: 0.004f,
                engageSagMeters: 0.1f, releaseSagMeters: 0.005f));
        }

        [Fact]
        public void CorrectionStepIsSpeedLimitedAndFinishesExactly()
        {
            // 1.2 m sag at 4 m/s and 60 fps: one tick lifts 4/60 m.
            float step = ArmsMetarigStanceEnforcementPolicy.ResolveCorrectionStep(
                sagMeters: 1.2f, deltaSeconds: 1f / 60f, maxCorrectionMetersPerSecond: 4f);
            Assert.Equal(4f / 60f, step, precision: 5);

            // The final tick covers only the remaining sag, never overshooting the rest.
            step = ArmsMetarigStanceEnforcementPolicy.ResolveCorrectionStep(
                sagMeters: 0.01f, deltaSeconds: 1f / 60f, maxCorrectionMetersPerSecond: 4f);
            Assert.Equal(0.01f, step, precision: 5);
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(-0.2f)]
        public void NeverPushesTheArmsDown(float sag)
        {
            Assert.Equal(0f, ArmsMetarigStanceEnforcementPolicy.ResolveCorrectionStep(
                sag, deltaSeconds: 1f / 60f, maxCorrectionMetersPerSecond: 4f));
        }

        [Fact]
        public void NegativeDeltaTimeProducesNoStep()
        {
            Assert.Equal(0f, ArmsMetarigStanceEnforcementPolicy.ResolveCorrectionStep(
                sagMeters: 1.2f, deltaSeconds: -0.016f, maxCorrectionMetersPerSecond: 4f));
        }
    }
}
