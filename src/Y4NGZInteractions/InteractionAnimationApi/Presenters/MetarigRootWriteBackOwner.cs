using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// #37 round 18: per-frame LateUpdate owner of the LOCAL player's metarig ROOT position
    /// (playerBodyAnimator.transform.localPosition, the ScavengerModel/metarig GameObject) for
    /// Base Layer states that do not key it. Runs from the DDOL
    /// <see cref="InteractionRuntimeHost"/> every frame, in AND out of live-body sessions,
    /// right after <see cref="ArmsMetarigWriteBackOwner"/>. See
    /// <see cref="MetarigRootWriteBackOwnerPolicy"/> for the model and the round-17b proof.
    /// Tolerance-gated: on a healthy capture every frame is a no-op; corrections log at most
    /// once per second. Touches ONLY the metarig root localPosition, never the camera chain
    /// (#26).
    /// </summary>
    internal static class MetarigRootWriteBackOwner
    {
        // Classification of the vanilla Base Layer states for the ROOT binding (round-16
        // enumeration). The session shell's base layer is a byte-identical copy, so the same
        // map serves in-session and out. Anything else is Unknown: the owner stands down.
        private static readonly Dictionary<int, MetarigRootStateKind> BaseLayerStateKinds =
            BuildBaseLayerStateKinds();

        private static Dictionary<int, MetarigRootStateKind> BuildBaseLayerStateKinds()
        {
            var kinds = new Dictionary<int, MetarigRootStateKind>
            {
                [Animator.StringToHash("CrouchIdle")] = MetarigRootStateKind.KeyedCrouch,
                [Animator.StringToHash("CrouchWalk")] = MetarigRootStateKind.KeyedCrouch,
                [Animator.StringToHash("CrouchDown")] = MetarigRootStateKind.KeyedRamp,
            };
            string[] keyedStanding =
            {
                "Walk", "Walk 0", "WalkTired", "WalkSideways", "WalkHindered", "LimpWalk",
                "JumpLand", "FallNoJump",
            };
            for (int i = 0; i < keyedStanding.Length; i++)
                kinds[Animator.StringToHash(keyedStanding[i])] = MetarigRootStateKind.KeyedStanding;
            string[] unkeyedStanding = { "Idle1", "Sprint", "Jump" };
            for (int i = 0; i < unkeyedStanding.Length; i++)
                kinds[Animator.StringToHash(unkeyedStanding[i])] = MetarigRootStateKind.UnkeyedStanding;
            // ClimbLadder and PushLever deliberately absent: Unknown, no write.
            return kinds;
        }

        private static float nextCorrectionLogAllowedAtRealtime;
        private static bool tickFaultLogged;

        /// <param name="phase">"early" from <see cref="MetarigRootWriteBackOwnerRunner"/>
        /// (before vanilla PlayerControllerB.LateUpdate reads the camera chain), "late" from
        /// the plugin host. Logged so a run shows which pass is doing the correcting.</param>
        internal static void LateUpdateTick(string phase)
        {
            try
            {
                if (!InteractionAnimationApiRestoreDiagnostics.MetarigRootWriteBackOwnerEnabled)
                    return;

                PlayerControllerB player = GameNetworkManager.Instance != null
                    ? GameNetworkManager.Instance.localPlayerController
                    : null;
                if (player == null && StartOfRound.Instance != null)
                    player = StartOfRound.Instance.localPlayerController;
                if (player == null || player.isPlayerDead)
                    return;

                Animator animator = player.playerBodyAnimator;
                if (animator == null || !animator.isActiveAndEnabled ||
                    animator.runtimeAnimatorController == null || animator.layerCount <= 0)
                {
                    return;
                }

                Transform root = animator.transform;
                if (root == null)
                    return;

                AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                bool inTransition = animator.IsInTransition(0);
                int nextStateHash = 0;
                float transitionNormalizedTime = 0f;
                float nextStateNormalizedTime = 0f;
                if (inTransition)
                {
                    AnimatorStateInfo nextState = animator.GetNextAnimatorStateInfo(0);
                    nextStateHash = nextState.shortNameHash;
                    nextStateNormalizedTime = nextState.normalizedTime;
                    transitionNormalizedTime =
                        animator.GetAnimatorTransitionInfo(0).normalizedTime;
                }

                MetarigRootStateKind currentKind = Classify(state.shortNameHash);
                MetarigRootStateKind nextKind = inTransition
                    ? Classify(nextStateHash)
                    : MetarigRootStateKind.Unknown;
                Vector3 local = root.localPosition;
                MetarigRootOwnerDecision decision = MetarigRootWriteBackOwnerPolicy.Decide(
                    currentKind,
                    inTransition,
                    nextKind,
                    transitionNormalizedTime,
                    state.normalizedTime,
                    nextStateNormalizedTime,
                    local.z,
                    MetarigRootWriteBackOwnerPolicy.DefaultCorrectionToleranceMeters);
                if (!decision.ShouldWrite)
                    return;

                // Every authored clip keys the root x and y at 0.
                root.localPosition = new Vector3(0f, 0f, decision.TargetLocalZ);

                if (Time.realtimeSinceStartup >= nextCorrectionLogAllowedAtRealtime)
                {
                    nextCorrectionLogAllowedAtRealtime = Time.realtimeSinceStartup + 1f;
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                        "[RestoreSeam.metarigroot] owner_corrected: " +
                        $"phase={phase} frame={Time.frameCount} state={state.shortNameHash} " +
                        $"inTransition={inTransition} nextState={nextStateHash} " +
                        $"transitionNormalizedTime={transitionNormalizedTime:0.###} " +
                        $"nextStateNormalizedTime={nextStateNormalizedTime:0.###} " +
                        $"readLocal=({local.x:0.####},{local.y:0.####},{local.z:0.####}) " +
                        $"targetLocalZ={decision.TargetLocalZ:0.####}.");
                }
            }
            catch (Exception exception)
            {
                if (!tickFaultLogged)
                {
                    tickFaultLogged = true;
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogWarning(
                        "[RestoreSeam.metarigroot] owner_tick_faulted: " +
                        $"frame={Time.frameCount} reason='{exception.Message}' " +
                        "action='owner_keeps_running_fault_logged_once'.");
                }
            }
        }

        private static MetarigRootStateKind Classify(int stateHash)
        {
            return BaseLayerStateKinds.TryGetValue(stateHash, out MetarigRootStateKind kind)
                ? kind
                : MetarigRootStateKind.Unknown;
        }
    }
}
