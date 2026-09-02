using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// How a Base Layer state relates to the first-person arms metarig position binding, from
    /// the vanilla metarig.controller ground truth (#37 round 8, byte-level clip read): only
    /// Idle1 (keys 2.104), CrouchIdle/CrouchWalk (key 1.017), and CrouchDown (keyed ramp
    /// 2.104→1.017 over 0.2 s) author the binding; the other 11 states leave it to
    /// write-defaults write-back.
    /// </summary>
    internal enum ArmsMetarigStateKind
    {
        /// <summary>Not one of the 15 verified vanilla Base Layer states — the owner has no
        /// model for it and must stand down.</summary>
        Unknown,

        /// <summary>Idle1: keys the standing 2.104 itself; the animator is authoritative.</summary>
        KeyedStanding,

        /// <summary>CrouchIdle/CrouchWalk: key the crouched 1.017; the animator is
        /// authoritative.</summary>
        KeyedCrouch,

        /// <summary>CrouchDown: keys its own 2.104→1.017 ramp (zero-tangent Hermite over the
        /// 0.2 s clip). Steady, the animator is authoritative. In a transition the ramp is a
        /// modelable endpoint: the round-14 artifact was the owner standing down on the
        /// AnyState startCrouching→CrouchDown edge — the ONLY entry into the crouch cluster —
        /// letting the animator blend the poisoned default while the camera was still
        /// standing.</summary>
        KeyedRamp,

        /// <summary>The 11 standing states that do not key the binding: whatever they write is
        /// the write-defaults capture — the poisonable surface this owner replaces.</summary>
        UnkeyedStanding,
    }

    /// <summary>What the write-back owner should do to the metarig this frame. A write puts
    /// the pristine x alongside the modeled y/z — every authored curve keys x at the pristine
    /// 0, so the owner never invents a lateral pose.</summary>
    internal readonly struct ArmsMetarigOwnerDecision
    {
        internal readonly bool ShouldWrite;
        internal readonly float TargetLocalY;
        internal readonly float TargetLocalZ;

        internal ArmsMetarigOwnerDecision(bool shouldWrite, float targetLocalY, float targetLocalZ)
        {
            ShouldWrite = shouldWrite;
            TargetLocalY = targetLocalY;
            TargetLocalZ = targetLocalZ;
        }

        internal static readonly ArmsMetarigOwnerDecision NoWrite =
            new ArmsMetarigOwnerDecision(false, float.NaN, float.NaN);
    }

    /// <summary>
    /// #37 round 13: deterministic per-frame ownership of the arms-metarig position in states
    /// that do not key it. Round 12 measured the runtime semantics end-to-end: unkeyed states
    /// write back a single graph-lifetime captured default every evaluation, and Unity takes
    /// that capture at the first NATURAL evaluations after a controller swap — later than every
    /// steerable boundary (assignment, RigBuilder.Build, first manual evaluation, the whole
    /// pristine-held seam window; rounds 8/10/11/12 each proved one). A crouched equip or drop
    /// therefore inevitably captures the authored crouch pose (the crouch cluster keys it on
    /// those frames), poisoning all 11 unkeyed states in and out of session. This policy is the
    /// closed replacement model: unkeyed states get the pristine standing rest, transitions
    /// blend the authored endpoint values by the transition's normalized time — exactly the
    /// curve a HEALTHY capture produces, so the owner is a tolerance-gated no-op on healthy
    /// vanilla. Steady keyed states and unknown states are the animator's; the owner never
    /// touches them.
    ///
    /// Round 14 extended the model from y-only to the full authored y/z (the poisoned capture
    /// carries the crouched z ≈ 0.336 as well — the "arms push forward" half of the artifact)
    /// and to transitions with a CrouchDown endpoint, evaluated on the authored ramp at the
    /// ramp state's own normalized time (clip length 0.2 s == state length, post-infinity
    /// clamp).
    /// </summary>
    internal static class ArmsMetarigWriteBackOwnerPolicy
    {
        /// <summary>Authored crouch height for the metarig, PARENT-local — the same space as
        /// metarig.localPosition and the pristine standing baseline (2.104). Never compare or
        /// write the player-local reading of the same pose (2.3599 = 2.104 x the 1.1216 parent
        /// scale); that coordinate-space confusion burned round 7.</summary>
        internal const float VanillaCrouchArmsMetarigParentLocalY = 1.017f;

        /// <summary>Authored crouch z for the metarig, parent-local. CrouchIdle and the
        /// CrouchDown ramp end key 0.336; CrouchWalk keys 0.331 — the 5 mm difference sits
        /// inside the correction gate, so one constant models both without ever fighting a
        /// healthy capture.</summary>
        internal const float VanillaCrouchArmsMetarigParentLocalZ = 0.336f;

        /// <summary>Correction gate. A healthy write-back reproduces the model to float noise
        /// (same authored constants, same linear crossfade weight), a poisoned one deviates by
        /// 0.2–1.087 m in y and up to 0.324 m in z; 0.02 m keeps the owner silent on healthy
        /// frames without letting any real poison through.</summary>
        internal const float DefaultCorrectionToleranceMeters = 0.02f;

        internal static ArmsMetarigOwnerDecision Decide(
            bool pristineAvailable,
            ArmsMetarigStateKind currentKind,
            bool inTransition,
            ArmsMetarigStateKind nextKind,
            float transitionNormalizedTime,
            float currentStateNormalizedTime,
            float nextStateNormalizedTime,
            float currentLocalY,
            float currentLocalZ,
            float standingRestLocalY,
            float standingRestLocalZ,
            float toleranceMeters)
        {
            // No pristine baseline means no trustworthy rest reference — guessing one is the
            // round-7 failure mode. NaN reads mean the transform is not measurable.
            if (!pristineAvailable ||
                float.IsNaN(currentLocalY) || float.IsNaN(currentLocalZ) ||
                float.IsNaN(standingRestLocalY) || float.IsNaN(standingRestLocalZ))
            {
                return ArmsMetarigOwnerDecision.NoWrite;
            }

            if (!inTransition)
            {
                // Steady frames: only the unkeyed states need an owner; keyed states author
                // the binding themselves (the ramp included) and unknown states have no model.
                if (currentKind != ArmsMetarigStateKind.UnkeyedStanding)
                    return ArmsMetarigOwnerDecision.NoWrite;
                return DecideWrite(
                    currentLocalY, currentLocalZ,
                    standingRestLocalY, standingRestLocalZ, toleranceMeters);
            }

            // Transitions: participate only when an unkeyed endpoint is involved (otherwise
            // the animator blends two authored curves and is authoritative), and never when an
            // endpoint is unmodelable (Unknown).
            if (currentKind == ArmsMetarigStateKind.Unknown ||
                nextKind == ArmsMetarigStateKind.Unknown)
            {
                return ArmsMetarigOwnerDecision.NoWrite;
            }
            if (currentKind != ArmsMetarigStateKind.UnkeyedStanding &&
                nextKind != ArmsMetarigStateKind.UnkeyedStanding)
            {
                return ArmsMetarigOwnerDecision.NoWrite;
            }

            if (!TryModelEndpoint(
                    currentKind, currentStateNormalizedTime,
                    standingRestLocalY, standingRestLocalZ,
                    out float fromY, out float fromZ) ||
                !TryModelEndpoint(
                    nextKind, nextStateNormalizedTime,
                    standingRestLocalY, standingRestLocalZ,
                    out float toY, out float toZ))
            {
                return ArmsMetarigOwnerDecision.NoWrite;
            }

            float weight = transitionNormalizedTime;
            if (float.IsNaN(weight))
                return ArmsMetarigOwnerDecision.NoWrite;
            if (weight < 0f)
                weight = 0f;
            else if (weight > 1f)
                weight = 1f;
            float targetY = fromY + (toY - fromY) * weight;
            float targetZ = fromZ + (toZ - fromZ) * weight;
            return DecideWrite(currentLocalY, currentLocalZ, targetY, targetZ, toleranceMeters);
        }

        private static bool TryModelEndpoint(
            ArmsMetarigStateKind kind,
            float stateNormalizedTime,
            float standingRestLocalY,
            float standingRestLocalZ,
            out float y,
            out float z)
        {
            switch (kind)
            {
                case ArmsMetarigStateKind.KeyedCrouch:
                    y = VanillaCrouchArmsMetarigParentLocalY;
                    z = VanillaCrouchArmsMetarigParentLocalZ;
                    return true;
                case ArmsMetarigStateKind.KeyedRamp:
                    if (float.IsNaN(stateNormalizedTime))
                    {
                        y = float.NaN;
                        z = float.NaN;
                        return false;
                    }
                    // CrouchDown's authored curve: both keys have zero tangents, so Unity's
                    // Hermite interpolation is the classic smoothstep 3u²−2u³ — a linear lerp
                    // deviates from it by up to ~0.10 m, five times the gate. Clip length
                    // equals the state length (0.2 s) and post-infinity clamps, so the state's
                    // normalized time maps 1:1 onto the curve.
                    float u = stateNormalizedTime;
                    if (u < 0f)
                        u = 0f;
                    else if (u > 1f)
                        u = 1f;
                    float eased = u * u * (3f - 2f * u);
                    y = standingRestLocalY +
                        (VanillaCrouchArmsMetarigParentLocalY - standingRestLocalY) * eased;
                    z = standingRestLocalZ +
                        (VanillaCrouchArmsMetarigParentLocalZ - standingRestLocalZ) * eased;
                    return true;
                case ArmsMetarigStateKind.KeyedStanding:
                case ArmsMetarigStateKind.UnkeyedStanding:
                    y = standingRestLocalY;
                    z = standingRestLocalZ;
                    return true;
                default:
                    y = float.NaN;
                    z = float.NaN;
                    return false;
            }
        }

        private static ArmsMetarigOwnerDecision DecideWrite(
            float currentLocalY,
            float currentLocalZ,
            float targetLocalY,
            float targetLocalZ,
            float toleranceMeters)
        {
            return Math.Abs(currentLocalY - targetLocalY) > toleranceMeters ||
                   Math.Abs(currentLocalZ - targetLocalZ) > toleranceMeters
                ? new ArmsMetarigOwnerDecision(true, targetLocalY, targetLocalZ)
                : ArmsMetarigOwnerDecision.NoWrite;
        }
    }
}
