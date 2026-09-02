using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// The metarig ROOT write-back owner (#37 round 18) replaces Unity's re-capturable
    /// write-defaults value on the root position binding with the closed authored model:
    /// unkeyed standing states (Idle1, Sprint, Jump) get the authored standing rest (z 0),
    /// transitions blend the authored endpoint values by the transition's normalized time
    /// (a CrouchDown endpoint evaluates on its linear 2-key ramp at that state's own
    /// normalized time), and steady keyed or unknown states stay the animator's. A HEALTHY
    /// capture reproduces the model exactly, so every healthy frame must be a no-op.
    /// </summary>
    public class MetarigRootWriteBackOwnerPolicyTests
    {
        private const float StandingRestZ =
            MetarigRootWriteBackOwnerPolicy.VanillaStandingRootParentLocalZ;
        private const float CrouchRestZ =
            MetarigRootWriteBackOwnerPolicy.VanillaCrouchRootParentLocalZ;
        private const float Tolerance =
            MetarigRootWriteBackOwnerPolicy.DefaultCorrectionToleranceMeters;

        private static MetarigRootOwnerDecision Decide(
            MetarigRootStateKind currentKind,
            bool inTransition,
            MetarigRootStateKind nextKind,
            float transitionNormalizedTime,
            float currentLocalZ,
            float currentStateNormalizedTime = 0f,
            float nextStateNormalizedTime = 0f)
        {
            return MetarigRootWriteBackOwnerPolicy.Decide(
                currentKind, inTransition, nextKind, transitionNormalizedTime,
                currentStateNormalizedTime, nextStateNormalizedTime, currentLocalZ, Tolerance);
        }

        [Fact]
        public void PoisonedUnkeyedSteadyStateIsCorrectedToStandingRest()
        {
            // Round-17b: Idle1 holding -0.184 after a crouched pickup.
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: false,
                MetarigRootStateKind.Unknown, 0f, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyUnkeyedSteadyStateIsANoOp()
        {
            Assert.False(Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: false,
                MetarigRootStateKind.Unknown, 0f, currentLocalZ: 0f).ShouldWrite);
        }

        [Fact]
        public void DeviationInsideTheGateIsANoOp()
        {
            Assert.False(Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: false,
                MetarigRootStateKind.Unknown, 0f, currentLocalZ: -0.013f).ShouldWrite);
        }

        [Theory]
        [InlineData((int)MetarigRootStateKind.KeyedStanding)]
        [InlineData((int)MetarigRootStateKind.KeyedCrouch)]
        [InlineData((int)MetarigRootStateKind.KeyedRamp)]
        [InlineData((int)MetarigRootStateKind.Unknown)]
        public void SteadyKeyedAndUnknownStatesAreTheAnimators(int kind)
        {
            // Keyed states author the binding themselves (Walk heals on its own) and unknown
            // states have no model to write. (int parameters: the enum is internal.)
            Assert.False(Decide(
                (MetarigRootStateKind)kind, inTransition: false,
                MetarigRootStateKind.Unknown, 0f, currentLocalZ: -0.5f).ShouldWrite);
        }

        [Fact]
        public void IdleToWalkTransitionCorrectsToStanding()
        {
            // THE round-17b artifact: Idle1 (poisoned -0.184) -> Walk (keys 0). The animator
            // crossfades the poison toward 0 over 0.1 s, dragging the whole rig forward; the
            // owner models both endpoints at 0 and pins it there.
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.KeyedStanding, 0.3f, currentLocalZ: -0.129f);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void WalkToIdleTransitionCorrectsToStanding()
        {
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.KeyedStanding, inTransition: true,
                MetarigRootStateKind.UnkeyedStanding, 0.7f, currentLocalZ: -0.129f);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyIdleToWalkTransitionIsANoOp()
        {
            Assert.False(Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.KeyedStanding, 0.3f, currentLocalZ: 0f).ShouldWrite);
        }

        [Fact]
        public void CrouchToUnkeyedTransitionBlendsTheAuthoredPose()
        {
            // CrouchIdle -> Idle1 (standing up) at half weight: exactly the trajectory a
            // healthy capture's crossfade produces.
            float expectedZ = CrouchRestZ + (StandingRestZ - CrouchRestZ) * 0.5f;
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.KeyedCrouch, inTransition: true,
                MetarigRootStateKind.UnkeyedStanding, 0.5f, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyCrouchToUnkeyedTransitionIsANoOp()
        {
            float healthyZ = CrouchRestZ + (StandingRestZ - CrouchRestZ) * 0.5f;
            Assert.False(Decide(
                MetarigRootStateKind.KeyedCrouch, inTransition: true,
                MetarigRootStateKind.UnkeyedStanding, 0.5f, currentLocalZ: healthyZ).ShouldWrite);
        }

        [Fact]
        public void UnkeyedToUnkeyedTransitionCorrectsToStanding()
        {
            // Idle1 -> Sprint with a poisoned capture: both endpoints standing.
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.UnkeyedStanding, 0.3f, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void UnkeyedIntoRampTransitionBlendsOntoTheAuthoredRamp()
        {
            // AnyState startCrouching -> CrouchDown (0.1 s) from a poisoned Idle1:
            // (1-w) x standing + w x ramp(uNext), ramp linear on the 2-key clip.
            const float w = 0.5f;
            const float uNext = 0.25f;
            float rampZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * uNext;
            float expectedZ = StandingRestZ + (rampZ - StandingRestZ) * w;
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.KeyedRamp, w, currentLocalZ: CrouchRestZ,
                nextStateNormalizedTime: uNext);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyUnkeyedIntoRampTransitionIsANoOp()
        {
            const float w = 0.5f;
            const float uNext = 0.25f;
            float rampZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * uNext;
            float healthyZ = StandingRestZ + (rampZ - StandingRestZ) * w;
            Assert.False(Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.KeyedRamp, w, currentLocalZ: healthyZ,
                nextStateNormalizedTime: uNext).ShouldWrite);
        }

        [Fact]
        public void RampEndpointClampsPastTheClipEnd()
        {
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.KeyedRamp, 1f, currentLocalZ: StandingRestZ,
                nextStateNormalizedTime: 1.8f);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(CrouchRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void RampOutToUnkeyedTransitionBlendsFromTheRampPose()
        {
            // A jump AnyState edge interrupting CrouchDown mid-descent: blend from the ramp
            // evaluated at the CURRENT state's own normalized time.
            const float w = 0.4f;
            const float uCurrent = 0.5f;
            float rampZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * uCurrent;
            float expectedZ = rampZ + (StandingRestZ - rampZ) * w;
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.KeyedRamp, inTransition: true,
                MetarigRootStateKind.UnkeyedStanding, w, currentLocalZ: CrouchRestZ,
                currentStateNormalizedTime: uCurrent);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Theory]
        [InlineData((int)MetarigRootStateKind.Unknown, (int)MetarigRootStateKind.UnkeyedStanding)]
        [InlineData((int)MetarigRootStateKind.UnkeyedStanding, (int)MetarigRootStateKind.Unknown)]
        public void TransitionsInvolvingUnknownStandDown(int from, int to)
        {
            Assert.False(Decide(
                (MetarigRootStateKind)from, inTransition: true,
                (MetarigRootStateKind)to, 0.5f, currentLocalZ: -0.5f).ShouldWrite);
        }

        [Theory]
        [InlineData((int)MetarigRootStateKind.KeyedStanding, (int)MetarigRootStateKind.KeyedCrouch)]
        [InlineData((int)MetarigRootStateKind.KeyedStanding, (int)MetarigRootStateKind.KeyedRamp)]
        [InlineData((int)MetarigRootStateKind.KeyedRamp, (int)MetarigRootStateKind.KeyedCrouch)]
        [InlineData((int)MetarigRootStateKind.KeyedCrouch, (int)MetarigRootStateKind.KeyedStanding)]
        public void TransitionsBetweenKeyedStatesAreTheAnimators(int from, int to)
        {
            // Walk -> CrouchDown, CrouchWalk -> Walk: two authored curves blending; no
            // poisonable term.
            Assert.False(Decide(
                (MetarigRootStateKind)from, inTransition: true,
                (MetarigRootStateKind)to, 0.5f, currentLocalZ: -0.5f).ShouldWrite);
        }

        [Fact]
        public void TransitionWeightIsClampedToTheUnitInterval()
        {
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.KeyedCrouch, inTransition: true,
                MetarigRootStateKind.UnkeyedStanding, 1.4f, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void NaNReadingDisarmsTheOwner()
        {
            Assert.False(Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: false,
                MetarigRootStateKind.Unknown, 0f, currentLocalZ: float.NaN).ShouldWrite);
        }

        [Fact]
        public void NaNRampTimeDisarmsTheOwner()
        {
            Assert.False(Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: true,
                MetarigRootStateKind.KeyedRamp, 0.5f, currentLocalZ: CrouchRestZ,
                nextStateNormalizedTime: float.NaN).ShouldWrite);
        }

        [Fact]
        public void CapturedAheadOfRestIsCorrectedBackToo()
        {
            // Bidirectional: a default captured on the far side of the rest comes back.
            MetarigRootOwnerDecision decision = Decide(
                MetarigRootStateKind.UnkeyedStanding, inTransition: false,
                MetarigRootStateKind.Unknown, 0f, currentLocalZ: 0.3f);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }
    }
}
