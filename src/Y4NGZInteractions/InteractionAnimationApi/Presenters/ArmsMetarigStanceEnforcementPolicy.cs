using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// Pure decisions for the arms-metarig stance safety net.
    ///
    /// Ground truth (vanilla metarig.controller + clips, read 2026-08-31): only 4 of the 15
    /// Base Layer states key `ScavengerModelArmsOnly/metarig` — Idle1 at the authored
    /// parent-local 2.103999 and the crouch cluster at 1.017 (CrouchDown ramps between). The
    /// other 11 (Walk, Sprint, WalkSideways, Jump, JumpLand, FallNoJump, ...) have
    /// write-defaults on and write back whatever Unity captured at the controller's first
    /// evaluation after assignment. The deterministic fix is the write-defaults capture priming
    /// at both swap seams (evaluate the default standing state once before any stance replay);
    /// this policy remains as the net that catches a poisoned capture from any path the priming
    /// does not own (third-party rebinds, future seams).
    ///
    /// While the session is active, the player is standing, and the base layer is outside the
    /// crouch cluster, the metarig's parent-local height may never sag far below the pristine
    /// standing rest. Corrections are stepped, not snapped, so an engaged lift glides at
    /// roughly the speed the camera rises on stand-up. Engagement is hysteretic: it takes a
    /// gross sag to engage, but once engaged the glide finishes at rest instead of hovering
    /// one deadzone low.
    ///
    /// SPACE CONTRACT: every height fed to this policy is PARENT-LOCAL
    /// (metarig.localPosition.y and the pristine baseline for the same transform). Round 7
    /// hardcoded the PLAYER-local reading of the same pose (2.3599 = 2.104 x the 1.1216 parent
    /// scale) as the rest, which manufactured a permanent 0.256 m phantom sag in healthy
    /// standing states, lifted the arms above the head, and wrote the transform for hundreds
    /// of frames per episode.
    /// </summary>
    internal static class ArmsMetarigStanceEnforcementPolicy
    {
        /// <summary>
        /// Sag below the standing rest that ENGAGES enforcement. Calibrated to the failure
        /// class, not to noise: a crouch-poisoned capture sags 1.087 (2.104 - 1.017), while the
        /// deepest legitimately authored standing-eligible poses (seated/vehicle clips key
        /// 1.752-1.986, sag up to 0.352) and walk-bob variation (sag under 0.02) must never
        /// engage. Half a meter splits those classes with wide margin on both sides.
        /// </summary>
        internal const float DefaultEngageSagMeters = 0.5f;

        /// <summary>Residual sag below which an engaged episode is finished.</summary>
        internal const float DefaultReleaseSagMeters = 0.005f;

        /// <summary>
        /// Correction speed. The vanilla stand-up camera rise covers ~1.2 m in roughly a third
        /// of a second, so matching it keeps an enforced glide indistinguishable from an
        /// authored one.
        /// </summary>
        internal const float DefaultMaxCorrectionMetersPerSecond = 4f;

        /// <summary>
        /// True when the metarig height is eligible for enforcement this tick: active session,
        /// the player is known to be standing, and the base layer is outside the crouch cluster
        /// (crouch states key the metarig themselves and must stay authoritative; an ejected
        /// still-crouched session belongs to the crouch resync, not to this policy).
        /// </summary>
        internal static bool IsEligible(
            bool sessionActive,
            bool crouchingKnown,
            bool crouching,
            bool insideCrouchCluster)
        {
            return sessionActive && crouchingKnown && !crouching && !insideCrouchCluster;
        }

        /// <summary>
        /// Advances the hysteretic engagement for one tick. A disengaged episode engages only on
        /// a sag beyond the engage threshold; an engaged episode stays engaged until the sag
        /// falls under the release threshold, so the glide completes at rest.
        /// </summary>
        internal static bool AdvanceEngagement(
            bool currentlyEngaged,
            float sagMeters,
            float engageSagMeters,
            float releaseSagMeters)
        {
            return currentlyEngaged
                ? sagMeters > releaseSagMeters
                : sagMeters > engageSagMeters;
        }

        /// <summary>
        /// The upward correction to add to the metarig's local height this tick of an ENGAGED
        /// episode. Never negative — enforcement only lifts, so a state that legitimately
        /// authors the arms higher than rest is never clamped down.
        /// </summary>
        internal static float ResolveCorrectionStep(
            float sagMeters,
            float deltaSeconds,
            float maxCorrectionMetersPerSecond)
        {
            if (sagMeters <= 0f)
                return 0f;
            float maxStep = maxCorrectionMetersPerSecond * Math.Max(0f, deltaSeconds);
            return Math.Min(sagMeters, maxStep);
        }
    }
}
