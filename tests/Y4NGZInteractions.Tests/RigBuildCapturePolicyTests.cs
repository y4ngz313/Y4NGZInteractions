using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// The rig-build pose normalization (#37 round 10) must hold the arms chain at pristine
    /// standing only when a pristine baseline exists AND the chain actually deviates: building
    /// without a baseline must never guess a pose, and a build taken at rest must not churn
    /// transforms. A crouched-stance build deviates ~1.087 m on the arms metarig Y (authored
    /// 1.017 vs 2.104) — the defining case that must normalize, because the build bakes the
    /// defaults every unkeyed Base Layer state writes back. After the build, only a target the
    /// scope actually moved gets its live pose written back.
    /// </summary>
    public class RigBuildCapturePolicyTests
    {
        private const float Tolerance = 0.001f;

        [Fact]
        public void CrouchedStanceBuildNormalizes()
        {
            Assert.True(RigBuildCapturePolicy.ShouldNormalizeBeforeBuild(
                pristineAvailable: true, 0f, -1.087f, 0f, Tolerance));
        }

        [Theory]
        [InlineData(0.002f, 0f, 0f)]
        [InlineData(0f, 0.002f, 0f)]
        [InlineData(0f, 0f, 0.002f)]
        [InlineData(0f, -0.002f, 0f)]
        public void AnySingleAxisBeyondToleranceNormalizes(float dx, float dy, float dz)
        {
            Assert.True(RigBuildCapturePolicy.ShouldNormalizeBeforeBuild(
                pristineAvailable: true, dx, dy, dz, Tolerance));
        }

        [Fact]
        public void StandingRestBuildDoesNotNormalize()
        {
            Assert.False(RigBuildCapturePolicy.ShouldNormalizeBeforeBuild(
                pristineAvailable: true, 0.0005f, -0.0005f, 0.0005f, Tolerance));
        }

        [Fact]
        public void MissingPristineBaselineNeverNormalizes()
        {
            Assert.False(RigBuildCapturePolicy.ShouldNormalizeBeforeBuild(
                pristineAvailable: false, 0f, -1.087f, 0f, Tolerance));
        }

        [Fact]
        public void NormalizedTargetRestoresLivePoseAfterBuild()
        {
            Assert.True(RigBuildCapturePolicy.ShouldRestoreLivePoseAfterBuild(
                normalizedBeforeBuild: true));
        }

        [Fact]
        public void UntouchedTargetIsNotRewrittenAfterBuild()
        {
            Assert.False(RigBuildCapturePolicy.ShouldRestoreLivePoseAfterBuild(
                normalizedBeforeBuild: false));
        }

        // Probe verdicts (#37 round 11): Walk writes the captured default back; reading it
        // against the pristine standing rest (2.104) is the direct capture measurement.

        [Fact]
        public void ProbeReadingCrouchHeightReportsPoison()
        {
            Assert.True(RigBuildCapturePolicy.ProbeIndicatesPoisonedDefault(
                pristineAvailable: true, probedLocalY: 1.017f, pristineLocalY: 2.104f,
                poisonToleranceMeters: 0.5f));
        }

        [Fact]
        public void ProbeReadingStandingRestIsHealthy()
        {
            Assert.False(RigBuildCapturePolicy.ProbeIndicatesPoisonedDefault(
                pristineAvailable: true, probedLocalY: 2.104f, pristineLocalY: 2.104f,
                poisonToleranceMeters: 0.5f));
        }

        [Fact]
        public void ProbeAboveRestIsNotPoison()
        {
            Assert.False(RigBuildCapturePolicy.ProbeIndicatesPoisonedDefault(
                pristineAvailable: true, probedLocalY: 2.7f, pristineLocalY: 2.104f,
                poisonToleranceMeters: 0.5f));
        }

        [Fact]
        public void ProbeWithoutPristineBaselineGivesNoVerdict()
        {
            Assert.False(RigBuildCapturePolicy.ProbeIndicatesPoisonedDefault(
                pristineAvailable: false, probedLocalY: 1.017f, pristineLocalY: 0f,
                poisonToleranceMeters: 0.5f));
        }

        [Fact]
        public void NaNProbeGivesNoVerdict()
        {
            Assert.False(RigBuildCapturePolicy.ProbeIndicatesPoisonedDefault(
                pristineAvailable: true, probedLocalY: float.NaN, pristineLocalY: 2.104f,
                poisonToleranceMeters: 0.5f));
        }

        // Sentinel seam probe (#37 round 12): the binding is parked at 1.7 (neither authored
        // height) between the Idle1 and Walk evaluations. Walk overwriting it proves
        // write-back; the sentinel surviving proves retention — the round-11 confound where
        // both answers read as Idle1's leftover 2.104 is gone.

        private const float SentinelY = 1.7f;
        private const float StandingDefaultY = 2.104f;
        private const float SentinelMatchTolerance = 0.05f;

        [Fact]
        public void SeamProbeSentinelSurvivingWalkProvesRetention()
        {
            Assert.Equal(
                CaptureProbeWriteVerdict.WroteNothing,
                RigBuildCapturePolicy.ClassifySentinelSeamProbe(
                    probedLocalY: SentinelY, SentinelY, StandingDefaultY,
                    SentinelMatchTolerance));
        }

        [Fact]
        public void SeamProbeStandingReadingProvesWriteBack()
        {
            Assert.Equal(
                CaptureProbeWriteVerdict.WroteCapturedDefault,
                RigBuildCapturePolicy.ClassifySentinelSeamProbe(
                    probedLocalY: 2.104f, SentinelY, StandingDefaultY,
                    SentinelMatchTolerance));
        }

        [Fact]
        public void SeamProbeCrouchReadingIsNeitherSentinelNorDefault()
        {
            // A 1.017 reading means Walk DID write, and wrote a poisoned capture — combined
            // with ProbeIndicatesPoisonedDefault this stays a distinct verdict from retention.
            Assert.Equal(
                CaptureProbeWriteVerdict.WroteOther,
                RigBuildCapturePolicy.ClassifySentinelSeamProbe(
                    probedLocalY: 1.017f, SentinelY, StandingDefaultY,
                    SentinelMatchTolerance));
        }

        [Fact]
        public void SeamProbeNaNReadingIsInconclusive()
        {
            Assert.Equal(
                CaptureProbeWriteVerdict.Inconclusive,
                RigBuildCapturePolicy.ClassifySentinelSeamProbe(
                    probedLocalY: float.NaN, SentinelY, StandingDefaultY,
                    SentinelMatchTolerance));
        }

        [Fact]
        public void SeamProbeSentinelConfusableWithDefaultIsInconclusive()
        {
            // If Idle1's write happened to land within the sentinel's match window, the two
            // answers cannot be separated and the probe must not claim either.
            Assert.Equal(
                CaptureProbeWriteVerdict.Inconclusive,
                RigBuildCapturePolicy.ClassifySentinelSeamProbe(
                    probedLocalY: 1.7f, sentinelLocalY: 1.7f, expectedDefaultLocalY: 1.75f,
                    SentinelMatchTolerance));
        }

        // Mid-session writer probe (#37 round 12): natural-frame semantics. The frame's own
        // written value joins the sentinel and the standing default as references, because
        // the round-9/10 enforcement fights imply a per-frame writer whose identity the seam
        // probes never measured.

        [Fact]
        public void MidSessionHealthyStandingFrameReportsDefaultWriteBack()
        {
            Assert.Equal(
                CaptureProbeWriteVerdict.WroteCapturedDefault,
                RigBuildCapturePolicy.ClassifyMidSessionWriterProbe(
                    probedLocalY: 2.104f, SentinelY, frameLocalY: 2.104f,
                    standingDefaultLocalY: StandingDefaultY, SentinelMatchTolerance));
        }

        [Fact]
        public void MidSessionSentinelSurvivingEvalProvesRetention()
        {
            Assert.Equal(
                CaptureProbeWriteVerdict.WroteNothing,
                RigBuildCapturePolicy.ClassifyMidSessionWriterProbe(
                    probedLocalY: 1.7f, SentinelY, frameLocalY: 2.104f,
                    standingDefaultLocalY: StandingDefaultY, SentinelMatchTolerance));
        }

        [Fact]
        public void MidSessionEvalReproducingFightHeightNamesSceneIndependentWriter()
        {
            // Enforcement-fight signature: the frame sat at ~1.5 and the re-evaluation wrote
            // ~1.5 again despite the sentinel — a per-frame writer that ignores the scene.
            Assert.Equal(
                CaptureProbeWriteVerdict.RewroteFrameValue,
                RigBuildCapturePolicy.ClassifyMidSessionWriterProbe(
                    probedLocalY: 1.5f, SentinelY, frameLocalY: 1.5f,
                    standingDefaultLocalY: StandingDefaultY, SentinelMatchTolerance));
        }

        [Fact]
        public void MidSessionBlendPulledTowardSentinelIsSceneFeedback()
        {
            // w*sentinel + (1-w)*standing with w=0.5 → 1.902: neither reference matches, so
            // the writer read the scene value the probe had just planted.
            Assert.Equal(
                CaptureProbeWriteVerdict.WroteOther,
                RigBuildCapturePolicy.ClassifyMidSessionWriterProbe(
                    probedLocalY: 1.902f, SentinelY, frameLocalY: 1.6f,
                    standingDefaultLocalY: StandingDefaultY, SentinelMatchTolerance));
        }

        [Fact]
        public void MidSessionFrameNearSentinelCannotProveRetention()
        {
            // Mid-fight the frame value can drift near 1.7; a sentinel reading is then
            // ambiguous between "wrote nothing" and "rewrote the frame value".
            Assert.Equal(
                CaptureProbeWriteVerdict.Inconclusive,
                RigBuildCapturePolicy.ClassifyMidSessionWriterProbe(
                    probedLocalY: 1.7f, SentinelY, frameLocalY: 1.68f,
                    standingDefaultLocalY: StandingDefaultY, SentinelMatchTolerance));
        }

        [Fact]
        public void MidSessionMissingStandingDefaultIsInconclusive()
        {
            Assert.Equal(
                CaptureProbeWriteVerdict.Inconclusive,
                RigBuildCapturePolicy.ClassifyMidSessionWriterProbe(
                    probedLocalY: 2.104f, SentinelY, frameLocalY: 2.104f,
                    standingDefaultLocalY: float.NaN, SentinelMatchTolerance));
        }

        // sentinelContribution: 0 = the evaluation ignored the sentinel, 1 = it reproduced
        // it; under the crossfade-feedback hypothesis this is the blend weight itself.

        [Fact]
        public void SentinelContributionZeroWhenEvalIgnoresSentinel()
        {
            Assert.Equal(0f, RigBuildCapturePolicy.SentinelContribution(
                probedLocalY: 2.104f, SentinelY, referenceLocalY: StandingDefaultY), 3);
        }

        [Fact]
        public void SentinelContributionOneWhenEvalReproducesSentinel()
        {
            Assert.Equal(1f, RigBuildCapturePolicy.SentinelContribution(
                probedLocalY: 1.7f, SentinelY, referenceLocalY: StandingDefaultY), 3);
        }

        [Fact]
        public void SentinelContributionMidpointIsHalf()
        {
            Assert.Equal(0.5f, RigBuildCapturePolicy.SentinelContribution(
                probedLocalY: 1.902f, SentinelY, referenceLocalY: StandingDefaultY), 3);
        }

        [Fact]
        public void SentinelContributionDegenerateSpanIsNaN()
        {
            Assert.True(float.IsNaN(RigBuildCapturePolicy.SentinelContribution(
                probedLocalY: 1.7f, sentinelLocalY: 1.7f, referenceLocalY: 1.7f)));
        }
    }
}
