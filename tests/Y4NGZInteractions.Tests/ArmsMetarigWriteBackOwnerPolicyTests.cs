using Xunit;
using Y4NGZInteractions.InteractionAnimationApi.Presenters;

namespace Y4NGZInteractions.Tests
{
    /// <summary>
    /// The write-back owner (#37 rounds 13–14) replaces Unity's re-capturable write-defaults
    /// value with the closed authored model: unkeyed standing states get the pristine
    /// standing rest (2.104/0.012 parent-local), transitions blend the authored endpoint
    /// values by the transition's normalized time — a CrouchDown endpoint evaluates on the
    /// authored zero-tangent Hermite ramp at that state's own normalized time — and steady
    /// keyed or unknown states stay the animator's. A HEALTHY capture reproduces the model
    /// exactly, so every healthy frame must be a no-op — the owner only ever writes what
    /// vanilla itself writes when the capture is standing.
    /// </summary>
    public class ArmsMetarigWriteBackOwnerPolicyTests
    {
        private const float StandingRest = 2.104f;
        private const float StandingRestZ = 0.012f;
        private const float CrouchRest =
            ArmsMetarigWriteBackOwnerPolicy.VanillaCrouchArmsMetarigParentLocalY;
        private const float CrouchRestZ =
            ArmsMetarigWriteBackOwnerPolicy.VanillaCrouchArmsMetarigParentLocalZ;
        private const float Tolerance =
            ArmsMetarigWriteBackOwnerPolicy.DefaultCorrectionToleranceMeters;

        private static ArmsMetarigOwnerDecision Decide(
            ArmsMetarigStateKind currentKind,
            bool inTransition,
            ArmsMetarigStateKind nextKind,
            float transitionNormalizedTime,
            float currentLocalY,
            float currentLocalZ = StandingRestZ,
            float currentStateNormalizedTime = 0f,
            float nextStateNormalizedTime = 0f,
            bool pristineAvailable = true)
        {
            return ArmsMetarigWriteBackOwnerPolicy.Decide(
                pristineAvailable, currentKind, inTransition, nextKind,
                transitionNormalizedTime, currentStateNormalizedTime, nextStateNormalizedTime,
                currentLocalY, currentLocalZ, StandingRest, StandingRestZ, Tolerance);
        }

        /// <summary>The CrouchDown curve both clips key with zero tangents: Unity's Hermite
        /// interpolation there is the classic smoothstep.</summary>
        private static float Eased(float u)
        {
            return u * u * (3f - 2f * u);
        }

        [Fact]
        public void PoisonedUnkeyedSteadyStateIsCorrectedToStandingRest()
        {
            // Round-12 measurement: Sprint writing the authored crouch pose while standing.
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f,
                currentLocalY: CrouchRest, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRest, decision.TargetLocalY, 3);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyUnkeyedSteadyStateIsANoOp()
        {
            Assert.False(Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f, currentLocalY: 2.104f).ShouldWrite);
        }

        [Fact]
        public void PoisonedZAloneOpensTheGate()
        {
            // The crouched capture carries z 0.336 as well as y 1.017; a read healthy in y
            // but crouched in z is still poison ("arms push forward").
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f,
                currentLocalY: StandingRest, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Theory]
        [InlineData((int)ArmsMetarigStateKind.KeyedStanding)]
        [InlineData((int)ArmsMetarigStateKind.KeyedCrouch)]
        [InlineData((int)ArmsMetarigStateKind.KeyedRamp)]
        [InlineData((int)ArmsMetarigStateKind.Unknown)]
        public void SteadyKeyedAndUnknownStatesAreTheAnimators(int kind)
        {
            // Even a wildly deviant reading: keyed states author the binding themselves
            // (Idle1 heals on its own) and unknown states have no model to write. (int
            // parameters: xunit test methods are public, the enum is internal.)
            Assert.False(Decide(
                (ArmsMetarigStateKind)kind, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f, currentLocalY: 0.5f).ShouldWrite);
        }

        [Fact]
        public void CrouchToUnkeyedTransitionBlendsTheAuthoredPose()
        {
            // The CrouchWalk -> Walk direct edge (crouching ifnot, 0.2 s) at half weight:
            // exactly the trajectory a healthy capture's crossfade produces, in y AND z.
            float expectedY = CrouchRest + (StandingRest - CrouchRest) * 0.5f;
            float expectedZ = CrouchRestZ + (StandingRestZ - CrouchRestZ) * 0.5f;
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.KeyedCrouch, inTransition: true,
                ArmsMetarigStateKind.UnkeyedStanding, 0.5f,
                currentLocalY: CrouchRest, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedY, decision.TargetLocalY, 3);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyCrouchToUnkeyedTransitionIsANoOp()
        {
            // A healthy capture blends 1.017 -> 2.104 (and 0.336 -> 0.012) by the same weight
            // the owner models, so the reading matches the target and nothing is written.
            float healthyY = CrouchRest + (StandingRest - CrouchRest) * 0.5f;
            float healthyZ = CrouchRestZ + (StandingRestZ - CrouchRestZ) * 0.5f;
            Assert.False(Decide(
                ArmsMetarigStateKind.KeyedCrouch, inTransition: true,
                ArmsMetarigStateKind.UnkeyedStanding, 0.5f,
                currentLocalY: healthyY, currentLocalZ: healthyZ).ShouldWrite);
        }

        [Fact]
        public void UnkeyedToUnkeyedTransitionCorrectsToStanding()
        {
            // Walk -> Sprint with a poisoned capture: both model endpoints are standing, the
            // animator blends poison with poison, the owner writes the full pristine pose.
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: true,
                ArmsMetarigStateKind.UnkeyedStanding, 0.3f, currentLocalY: CrouchRest);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRest, decision.TargetLocalY, 3);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void UnkeyedToCrouchTransitionBlendsDownward()
        {
            // Re-entering the crouch cluster from a poisoned unkeyed state: the owner walks
            // the pose down the same authored curve vanilla would.
            float expectedY = StandingRest + (CrouchRest - StandingRest) * 0.25f;
            float expectedZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * 0.25f;
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: true,
                ArmsMetarigStateKind.KeyedCrouch, 0.25f, currentLocalY: CrouchRest);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedY, decision.TargetLocalY, 3);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void UnkeyedIntoRampTransitionBlendsOntoTheAuthoredRamp()
        {
            // The round-14 artifact: AnyState startCrouching -> CrouchDown (0.1 s) is the ONLY
            // entry into the crouch cluster, so crouching while moving is Walk -> CrouchDown.
            // The owner used to stand down here; the animator blended the poisoned 1.017/0.336
            // while the camera was still standing — arms snapped down and forward. The model:
            // (1-w) x standing + w x ramp(uNext), ramp on the zero-tangent Hermite.
            const float w = 0.5f;
            const float uNext = 0.25f;
            float rampY = StandingRest + (CrouchRest - StandingRest) * Eased(uNext);
            float rampZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * Eased(uNext);
            float expectedY = StandingRest + (rampY - StandingRest) * w;
            float expectedZ = StandingRestZ + (rampZ - StandingRestZ) * w;
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: true,
                ArmsMetarigStateKind.KeyedRamp, w,
                currentLocalY: CrouchRest, currentLocalZ: CrouchRestZ,
                nextStateNormalizedTime: uNext);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedY, decision.TargetLocalY, 3);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void HealthyUnkeyedIntoRampTransitionIsANoOp()
        {
            // A healthy capture produces exactly the modeled trajectory, so entering the
            // crouch cluster from a healthy Walk writes nothing.
            const float w = 0.5f;
            const float uNext = 0.25f;
            float rampY = StandingRest + (CrouchRest - StandingRest) * Eased(uNext);
            float rampZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * Eased(uNext);
            float healthyY = StandingRest + (rampY - StandingRest) * w;
            float healthyZ = StandingRestZ + (rampZ - StandingRestZ) * w;
            Assert.False(Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: true,
                ArmsMetarigStateKind.KeyedRamp, w,
                currentLocalY: healthyY, currentLocalZ: healthyZ,
                nextStateNormalizedTime: uNext).ShouldWrite);
        }

        [Fact]
        public void RampEndpointClampsPastTheClipEnd()
        {
            // Post-infinity on the authored curve clamps: a ramp state past its 0.2 s holds
            // the crouch pose.
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: true,
                ArmsMetarigStateKind.KeyedRamp, 1f,
                currentLocalY: StandingRest, currentLocalZ: StandingRestZ,
                nextStateNormalizedTime: 1.8f);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(CrouchRest, decision.TargetLocalY, 3);
            Assert.Equal(CrouchRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void RampOutToUnkeyedTransitionBlendsFromTheRampPose()
        {
            // A jump/fall AnyState edge interrupting CrouchDown mid-descent: the owner blends
            // from the ramp evaluated at the CURRENT state's own normalized time.
            const float w = 0.4f;
            const float uCurrent = 0.5f;
            float rampY = StandingRest + (CrouchRest - StandingRest) * Eased(uCurrent);
            float rampZ = StandingRestZ + (CrouchRestZ - StandingRestZ) * Eased(uCurrent);
            float expectedY = rampY + (StandingRest - rampY) * w;
            float expectedZ = rampZ + (StandingRestZ - rampZ) * w;
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.KeyedRamp, inTransition: true,
                ArmsMetarigStateKind.UnkeyedStanding, w,
                currentLocalY: CrouchRest, currentLocalZ: CrouchRestZ,
                currentStateNormalizedTime: uCurrent);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(expectedY, decision.TargetLocalY, 3);
            Assert.Equal(expectedZ, decision.TargetLocalZ, 3);
        }

        [Theory]
        [InlineData((int)ArmsMetarigStateKind.Unknown, (int)ArmsMetarigStateKind.UnkeyedStanding)]
        [InlineData((int)ArmsMetarigStateKind.UnkeyedStanding, (int)ArmsMetarigStateKind.Unknown)]
        public void TransitionsInvolvingUnknownStandDown(int from, int to)
        {
            Assert.False(Decide(
                (ArmsMetarigStateKind)from, inTransition: true,
                (ArmsMetarigStateKind)to, 0.5f, currentLocalY: 0.5f).ShouldWrite);
        }

        [Theory]
        [InlineData((int)ArmsMetarigStateKind.KeyedStanding, (int)ArmsMetarigStateKind.KeyedCrouch)]
        [InlineData((int)ArmsMetarigStateKind.KeyedStanding, (int)ArmsMetarigStateKind.KeyedRamp)]
        [InlineData((int)ArmsMetarigStateKind.KeyedRamp, (int)ArmsMetarigStateKind.KeyedCrouch)]
        public void TransitionsBetweenKeyedStatesAreTheAnimators(int from, int to)
        {
            // Idle1 -> CrouchDown style: two authored curves blending; no unkeyed endpoint
            // means no poisonable term and no reason to intervene.
            Assert.False(Decide(
                (ArmsMetarigStateKind)from, inTransition: true,
                (ArmsMetarigStateKind)to, 0.5f, currentLocalY: 0.5f).ShouldWrite);
        }

        [Fact]
        public void TransitionWeightIsClampedToTheUnitInterval()
        {
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.KeyedCrouch, inTransition: true,
                ArmsMetarigStateKind.UnkeyedStanding, 1.4f,
                currentLocalY: CrouchRest, currentLocalZ: CrouchRestZ);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRest, decision.TargetLocalY, 3);
            Assert.Equal(StandingRestZ, decision.TargetLocalZ, 3);
        }

        [Fact]
        public void MissingPristineBaselineDisarmsTheOwner()
        {
            // Guessing a rest without a baseline is the round-7 failure mode.
            Assert.False(Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f, currentLocalY: CrouchRest,
                pristineAvailable: false).ShouldWrite);
        }

        [Theory]
        [InlineData(float.NaN, StandingRestZ)]
        [InlineData(StandingRest, float.NaN)]
        public void NaNReadingDisarmsTheOwner(float readY, float readZ)
        {
            Assert.False(Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f,
                currentLocalY: readY, currentLocalZ: readZ).ShouldWrite);
        }

        [Fact]
        public void NaNRampTimeDisarmsTheOwner()
        {
            Assert.False(Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: true,
                ArmsMetarigStateKind.KeyedRamp, 0.5f, currentLocalY: CrouchRest,
                currentLocalZ: CrouchRestZ,
                nextStateNormalizedTime: float.NaN).ShouldWrite);
        }

        [Fact]
        public void CapturedAboveRestIsCorrectedDownToo()
        {
            // The owner writes the model bidirectionally — a default captured above the rest
            // (e.g. a mid-air capture) comes back down, not just up.
            ArmsMetarigOwnerDecision decision = Decide(
                ArmsMetarigStateKind.UnkeyedStanding, inTransition: false,
                ArmsMetarigStateKind.Unknown, 0f, currentLocalY: 2.7f);
            Assert.True(decision.ShouldWrite);
            Assert.Equal(StandingRest, decision.TargetLocalY, 3);
        }
    }
}
