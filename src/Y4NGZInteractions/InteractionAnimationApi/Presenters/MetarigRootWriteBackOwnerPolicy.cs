using System;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// How a Base Layer state relates to the metarig ROOT position binding (clip path "",
    /// the ScavengerModel/metarig Animator GameObject), from the vanilla metarig.controller
    /// ground truth (#37 round 16 enumeration, round 17b proof). The keyed set differs from
    /// the arms metarig: Walk/WalkTired/WalkSideways/WalkHindered/LimpWalk/JumpLand/FallNoJump
    /// key (0,0,0); CrouchIdle/CrouchWalk key (0,0,-0.184); CrouchDown keys a 2-key ramp
    /// 0 to -0.184 over its 0.2 s clip; Idle1, Sprint and Jump do NOT key it and write back
    /// the captured default; ClimbLadder and PushLever are left to the animator.
    /// </summary>
    internal enum MetarigRootStateKind
    {
        /// <summary>Not modeled (ClimbLadder, PushLever, third-party states): the owner
        /// stands down.</summary>
        Unknown,

        /// <summary>Walk-family, JumpLand, FallNoJump: key the standing 0 themselves; the
        /// animator is authoritative.</summary>
        KeyedStanding,

        /// <summary>CrouchIdle/CrouchWalk: key the crouched -0.184; the animator is
        /// authoritative.</summary>
        KeyedCrouch,

        /// <summary>CrouchDown: keys its own 0 to -0.184 ramp over the 0.2 s clip. Steady,
        /// the animator is authoritative; in a transition the ramp is a modelable
        /// endpoint.</summary>
        KeyedRamp,

        /// <summary>Idle1, Sprint, Jump: do not key the binding; whatever they write is the
        /// write-defaults capture, the poisonable surface this owner replaces. Idle1 is
        /// UNKEYED here (it keys the arms metarig but not the root), which is why a standing
        /// pickup+drop never healed the root: the swap re-captures whatever Idle1 already
        /// holds.</summary>
        UnkeyedStanding,
    }

    /// <summary>What the root owner should do this frame. The root's x and y are 0 in every
    /// authored clip, so a write always carries (0, 0, TargetLocalZ).</summary>
    internal readonly struct MetarigRootOwnerDecision
    {
        internal readonly bool ShouldWrite;
        internal readonly float TargetLocalZ;

        internal MetarigRootOwnerDecision(bool shouldWrite, float targetLocalZ)
        {
            ShouldWrite = shouldWrite;
            TargetLocalZ = targetLocalZ;
        }

        internal static readonly MetarigRootOwnerDecision NoWrite =
            new MetarigRootOwnerDecision(false, float.NaN);
    }

    /// <summary>
    /// #37 round 18: deterministic per-frame ownership of the metarig ROOT position in states
    /// that do not key it. Same closed replacement model as
    /// <see cref="ArmsMetarigWriteBackOwnerPolicy"/>, applied to the binding the round-17b
    /// probe proved to be the "first-input jitter": a controller swap while crouched captures
    /// -0.184 for the root, Idle1/Sprint/Jump write it back every frame afterwards, and every
    /// Idle1-Walk crossfade then drags the whole rig (camera, arms, body) 0.184 m in
    /// metarig-parent z over the 0.1 s blend. Unkeyed states get the authored standing rest
    /// (0), transitions blend the authored endpoints by the transition's normalized time,
    /// exactly what a healthy capture produces, so the owner is a tolerance-gated no-op on
    /// healthy vanilla. Steady keyed and unknown states are the animator's.
    ///
    /// The rest is not read from a pristine snapshot: seven standing clips author (0,0,0)
    /// outright, and the pristine camera-chain capture does not record the root.
    /// </summary>
    internal static class MetarigRootWriteBackOwnerPolicy
    {
        /// <summary>Authored standing root z, parent-local: keyed by every standing clip that
        /// keys the binding and held by a healthy capture (round-17b phase 1: 0.000 on every
        /// start).</summary>
        internal const float VanillaStandingRootParentLocalZ = 0f;

        /// <summary>Authored crouched root z, parent-local: CrouchIdle, CrouchWalk and the
        /// CrouchDown ramp end all key -0.184 (round-16 enumeration; round-17b: -0.184 in
        /// every broken phase).</summary>
        internal const float VanillaCrouchRootParentLocalZ = -0.184f;

        /// <summary>Correction gate. The poison is 0.184 m; 0.02 m keeps healthy frames
        /// silent. It also covers the CrouchDown ramp shape: the 2-key clip is modeled
        /// linearly, and a zero-tangent Hermite would differ by at most 0.096 x 0.184 ~ 0.018 m,
        /// inside the gate either way.</summary>
        internal const float DefaultCorrectionToleranceMeters = 0.02f;

        internal static MetarigRootOwnerDecision Decide(
            MetarigRootStateKind currentKind,
            bool inTransition,
            MetarigRootStateKind nextKind,
            float transitionNormalizedTime,
            float currentStateNormalizedTime,
            float nextStateNormalizedTime,
            float currentLocalZ,
            float toleranceMeters)
        {
            if (float.IsNaN(currentLocalZ))
                return MetarigRootOwnerDecision.NoWrite;

            if (!inTransition)
            {
                // Steady frames: only the unkeyed states need an owner; keyed states author
                // the binding themselves (the ramp included) and unknown states have no model.
                if (currentKind != MetarigRootStateKind.UnkeyedStanding)
                    return MetarigRootOwnerDecision.NoWrite;
                return DecideWrite(currentLocalZ, VanillaStandingRootParentLocalZ, toleranceMeters);
            }

            // Transitions: participate only when an unkeyed endpoint is involved (otherwise
            // the animator blends two authored curves and is authoritative), and never when
            // an endpoint is unmodelable.
            if (currentKind == MetarigRootStateKind.Unknown ||
                nextKind == MetarigRootStateKind.Unknown)
            {
                return MetarigRootOwnerDecision.NoWrite;
            }
            if (currentKind != MetarigRootStateKind.UnkeyedStanding &&
                nextKind != MetarigRootStateKind.UnkeyedStanding)
            {
                return MetarigRootOwnerDecision.NoWrite;
            }

            if (!TryModelEndpoint(currentKind, currentStateNormalizedTime, out float fromZ) ||
                !TryModelEndpoint(nextKind, nextStateNormalizedTime, out float toZ))
            {
                return MetarigRootOwnerDecision.NoWrite;
            }

            float weight = transitionNormalizedTime;
            if (float.IsNaN(weight))
                return MetarigRootOwnerDecision.NoWrite;
            if (weight < 0f)
                weight = 0f;
            else if (weight > 1f)
                weight = 1f;
            float targetZ = fromZ + (toZ - fromZ) * weight;
            return DecideWrite(currentLocalZ, targetZ, toleranceMeters);
        }

        private static bool TryModelEndpoint(
            MetarigRootStateKind kind,
            float stateNormalizedTime,
            out float z)
        {
            switch (kind)
            {
                case MetarigRootStateKind.KeyedCrouch:
                    z = VanillaCrouchRootParentLocalZ;
                    return true;
                case MetarigRootStateKind.KeyedRamp:
                    if (float.IsNaN(stateNormalizedTime))
                    {
                        z = float.NaN;
                        return false;
                    }
                    // CrouchDown: 2-key ramp t=0 -> 0, t=0.2 s -> -0.184; clip length equals
                    // the state length and post-infinity clamps, so the state's normalized
                    // time maps 1:1 onto the curve. Modeled linearly (see the tolerance note).
                    float u = stateNormalizedTime;
                    if (u < 0f)
                        u = 0f;
                    else if (u > 1f)
                        u = 1f;
                    z = VanillaStandingRootParentLocalZ +
                        (VanillaCrouchRootParentLocalZ - VanillaStandingRootParentLocalZ) * u;
                    return true;
                case MetarigRootStateKind.KeyedStanding:
                case MetarigRootStateKind.UnkeyedStanding:
                    z = VanillaStandingRootParentLocalZ;
                    return true;
                default:
                    z = float.NaN;
                    return false;
            }
        }

        private static MetarigRootOwnerDecision DecideWrite(
            float currentLocalZ,
            float targetLocalZ,
            float toleranceMeters)
        {
            return Math.Abs(currentLocalZ - targetLocalZ) > toleranceMeters
                ? new MetarigRootOwnerDecision(true, targetLocalZ)
                : MetarigRootOwnerDecision.NoWrite;
        }
    }
}
