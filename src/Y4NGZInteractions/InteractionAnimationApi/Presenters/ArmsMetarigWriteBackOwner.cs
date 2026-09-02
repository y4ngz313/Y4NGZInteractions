using System;
using System.Collections.Generic;
using GameNetcodeStuff;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// #37 round 13: per-frame LateUpdate owner of the LOCAL player's first-person arms
    /// metarig position for Base Layer states that do not key it. Runs from the DDOL
    /// <see cref="InteractionRuntimeHost"/> every frame, in AND out of live-body sessions —
    /// the post-drop break lives out of session (round-12 log: pure vanilla Walk writing the
    /// crouched 1.017 after a crouched drop). See
    /// <see cref="ArmsMetarigWriteBackOwnerPolicy"/> for the model and the round-12 evidence
    /// that ended capture-steering. Tolerance-gated: on a healthy capture every frame is a
    /// no-op, preserving the restore-to-vanilla contract in spirit; corrections log at most
    /// once per second. Touches ONLY metarig.localPosition — never the camera chain (#26).
    /// </summary>
    internal static class ArmsMetarigWriteBackOwner
    {
        // Classification of the 15 verified vanilla Base Layer states (round-8 ground truth,
        // byte-level clip read). The session shell's base layer is a byte-identical copy, so
        // the same map serves in-session and out. Anything else is Unknown → the owner stands
        // down rather than guess at third-party or weapon-specific states.
        private static readonly Dictionary<int, ArmsMetarigStateKind> BaseLayerStateKinds =
            BuildBaseLayerStateKinds();

        private static Dictionary<int, ArmsMetarigStateKind> BuildBaseLayerStateKinds()
        {
            var kinds = new Dictionary<int, ArmsMetarigStateKind>
            {
                [Animator.StringToHash("Idle1")] = ArmsMetarigStateKind.KeyedStanding,
                [Animator.StringToHash("CrouchIdle")] = ArmsMetarigStateKind.KeyedCrouch,
                [Animator.StringToHash("CrouchWalk")] = ArmsMetarigStateKind.KeyedCrouch,
                [Animator.StringToHash("CrouchDown")] = ArmsMetarigStateKind.KeyedRamp,
            };
            string[] unkeyedStanding =
            {
                "Walk", "Walk 0", "WalkSideways", "WalkHindered", "LimpWalk", "Sprint",
                "Jump", "FallNoJump", "JumpLand", "ClimbLadder", "PushLever",
            };
            for (int i = 0; i < unkeyedStanding.Length; i++)
                kinds[Animator.StringToHash(unkeyedStanding[i])] = ArmsMetarigStateKind.UnkeyedStanding;
            return kinds;
        }

        private static float nextCorrectionLogAllowedAtRealtime;
        private static bool tickFaultLogged;

        internal static void LateUpdateTick()
        {
            try
            {
                if (!InteractionAnimationApiRestoreDiagnostics.ArmsMetarigWriteBackOwnerEnabled)
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

                Transform metarig = player.playerModelArmsMetarig;
                if (metarig == null)
                    return;

                bool pristineAvailable = InteractionAnimationApiRestoreDiagnostics
                    .TryGetPristineCameraChainLocalPosition(
                        player, "playerModelArmsMetarig",
                        out Vector3 pristineLocal, out string restSource);

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

                ArmsMetarigStateKind currentKind = Classify(state.shortNameHash);
                ArmsMetarigStateKind nextKind = inTransition
                    ? Classify(nextStateHash)
                    : ArmsMetarigStateKind.Unknown;
                Vector3 local = metarig.localPosition;
                ArmsMetarigOwnerDecision decision = ArmsMetarigWriteBackOwnerPolicy.Decide(
                    pristineAvailable,
                    currentKind,
                    inTransition,
                    nextKind,
                    transitionNormalizedTime,
                    state.normalizedTime,
                    nextStateNormalizedTime,
                    local.y,
                    local.z,
                    pristineLocal.y,
                    pristineLocal.z,
                    ArmsMetarigWriteBackOwnerPolicy.DefaultCorrectionToleranceMeters);
                if (!decision.ShouldWrite)
                    return;

                metarig.localPosition = new Vector3(
                    pristineLocal.x, decision.TargetLocalY, decision.TargetLocalZ);

                if (Time.realtimeSinceStartup >= nextCorrectionLogAllowedAtRealtime)
                {
                    nextCorrectionLogAllowedAtRealtime = Time.realtimeSinceStartup + 1f;
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                        "[RestoreSeam.armsmetarig] owner_corrected: " +
                        $"frame={Time.frameCount} state={state.shortNameHash} " +
                        $"inTransition={inTransition} nextState={nextStateHash} " +
                        $"transitionNormalizedTime={transitionNormalizedTime:0.###} " +
                        $"nextStateNormalizedTime={nextStateNormalizedTime:0.###} " +
                        $"readLocalY={local.y:0.####} targetLocalY={decision.TargetLocalY:0.####} " +
                        $"readLocalZ={local.z:0.####} targetLocalZ={decision.TargetLocalZ:0.####} " +
                        $"standingRest={pristineLocal.y:0.####} restSource='{restSource}'.");
                }
            }
            catch (Exception exception)
            {
                if (!tickFaultLogged)
                {
                    tickFaultLogged = true;
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogWarning(
                        "[RestoreSeam.armsmetarig] owner_tick_faulted: " +
                        $"frame={Time.frameCount} reason='{exception.Message}' " +
                        "action='owner_keeps_running_fault_logged_once'.");
                }
            }
        }

        private static ArmsMetarigStateKind Classify(int stateHash)
        {
            return BaseLayerStateKinds.TryGetValue(stateHash, out ArmsMetarigStateKind kind)
                ? kind
                : ArmsMetarigStateKind.Unknown;
        }
    }
}
