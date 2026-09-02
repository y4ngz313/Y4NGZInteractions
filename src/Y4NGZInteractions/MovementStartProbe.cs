using System;
using System.Reflection;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;
using Y4NGZInteractions.InteractionAnimationApi;

namespace Y4NGZInteractions
{
    /// <summary>
    /// #37 round 17: per-frame movement-start probe for the local player. Diagnostics only,
    /// gated by <c>Enable Movement Start Probe</c>. Around every movement start (move input
    /// rising above 0.1, or vanilla's isWalking rising) it logs the preceding frames from a
    /// ring buffer and the following frames live, one line each, so a stalled start followed by
    /// a snap can be attributed to zeroed input, a slow walkForce ramp, a blocked
    /// CharacterController move, a frame hitch, or a camera lagging the body. Any frame longer
    /// than 50 ms is logged regardless of movement state.
    /// </summary>
    internal static class MovementStartProbe
    {
        private const int PreFrames = 6;
        private const int PostFrames = 45;
        private const float InputRiseThreshold = 0.1f;
        private const float HitchSeconds = 0.05f;
        private const float HitchLogIntervalSeconds = 0.2f;
        private const string Tag = "[MovementStartProbe] ";

        private static readonly FieldInfo WalkForceField =
            AccessTools.Field(typeof(PlayerControllerB), "walkForce");
        private static readonly FieldInfo SprintMultiplierField =
            AccessTools.Field(typeof(PlayerControllerB), "sprintMultiplier");
        private static readonly FieldInfo IsWalkingField =
            AccessTools.Field(typeof(PlayerControllerB), "isWalking");
        private static readonly FieldInfo IsJumpingField =
            AccessTools.Field(typeof(PlayerControllerB), "isJumping");
        private static readonly FieldInfo IsFallingNoJumpField =
            AccessTools.Field(typeof(PlayerControllerB), "isFallingNoJump");
        private static readonly FieldInfo IsFallingFromJumpField =
            AccessTools.Field(typeof(PlayerControllerB), "isFallingFromJump");
        private static readonly FieldInfo CrouchMeterField =
            AccessTools.Field(typeof(PlayerControllerB), "crouchMeter");

        private static readonly string[] Ring = new string[PreFrames];
        private static int ringCount;
        private static int ringNext;
        private static int postRemaining;
        private static int startCount;
        private static bool wasWalking;
        private static float lastInputMagnitude;
        private static Vector3 lastPosition;
        private static bool hasLastPosition;
        private static float lastDeltaTime;
        private static float nextHitchLogAllowedAt;
        private static bool failureLogged;
        private static PlayerControllerB lastPlayer;
        private static Vector3 lastCameraWorld;
        private static bool hasLastCameraWorld;

        internal static void LateUpdateTick()
        {
            try
            {
                if (!InteractionAnimationApiRestoreDiagnostics.MovementStartProbeEnabled)
                    return;

                PlayerControllerB player = GameNetworkManager.Instance != null
                    ? GameNetworkManager.Instance.localPlayerController
                    : null;
                if (player == null && StartOfRound.Instance != null)
                    player = StartOfRound.Instance.localPlayerController;
                if (player == null || player.isPlayerDead || player.transform == null)
                {
                    hasLastPosition = false;
                    return;
                }
                if (!ReferenceEquals(player, lastPlayer))
                {
                    lastPlayer = player;
                    hasLastPosition = false;
                    ringCount = 0;
                    ringNext = 0;
                    postRemaining = 0;
                }

                float deltaTime = Time.deltaTime;
                Vector2 input = player.moveInputVector;
                float inputMagnitude = input.magnitude;
                Vector3 position = player.transform.position;
                Vector3 delta = hasLastPosition ? position - lastPosition : Vector3.zero;
                float planarDelta = new Vector2(delta.x, delta.z).magnitude;

                CharacterController controller = player.thisController;
                Vector3 velocity = controller != null ? controller.velocity : Vector3.zero;
                float planarSpeed = new Vector2(velocity.x, velocity.z).magnitude;
                string grounded = controller != null ? controller.isGrounded.ToString() : "n/a";

                bool walking = ReadBool(IsWalkingField, player);
                Vector3 walkForce = ReadVector3(WalkForceField, player);
                float sprintMultiplier = ReadFloat(SprintMultiplierField, player);
                float crouchMeter = ReadFloat(CrouchMeterField, player);

                string cameraLocal = "n/a";
                if (player.gameplayCamera != null)
                {
                    Vector3 local = player.transform.InverseTransformPoint(
                        player.gameplayCamera.transform.position);
                    cameraLocal = $"({local.x:0.000},{local.y:0.000},{local.z:0.000})";
                }

                string rootLocal = "n/a";
                string armsLocal = "n/a";
                string cameraWorldDelta = "n/a";
                if (player.playerBodyAnimator != null && player.playerBodyAnimator.transform != null)
                {
                    Vector3 r = player.playerBodyAnimator.transform.localPosition;
                    rootLocal = $"({r.x:0.000},{r.y:0.000},{r.z:0.000})";
                }
                if (player.playerModelArmsMetarig != null)
                {
                    Vector3 a = player.playerModelArmsMetarig.localPosition;
                    armsLocal = $"({a.x:0.000},{a.y:0.000},{a.z:0.000})";
                }
                if (player.gameplayCamera != null)
                {
                    Vector3 cameraWorld = player.gameplayCamera.transform.position;
                    if (hasLastCameraWorld)
                    {
                        Vector3 cd = cameraWorld - lastCameraWorld;
                        cameraWorldDelta = $"{new Vector2(cd.x, cd.z).magnitude:0.0000}";
                    }
                    lastCameraWorld = cameraWorld;
                    hasLastCameraWorld = true;
                }
                else
                {
                    hasLastCameraWorld = false;
                }

                string animatorState = "n/a";
                Animator animator = player.playerBodyAnimator;
                if (animator != null && animator.isActiveAndEnabled &&
                    animator.runtimeAnimatorController != null && animator.layerCount > 0)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(0);
                    bool inTransition = animator.IsInTransition(0);
                    animatorState = state.shortNameHash + (inTransition ? "*" : string.Empty);
                }

                string line =
                    $"frame={Time.frameCount} dt={deltaTime * 1000f:0.0}ms " +
                    $"udt={Time.unscaledDeltaTime * 1000f:0.0}ms " +
                    $"input=({input.x:0.00},{input.y:0.00}) walking={walking} " +
                    $"sprinting={player.isSprinting} walkForce={walkForce.magnitude:0.000} " +
                    $"sprintMult={sprintMultiplier:0.00} speed={player.movementSpeed:0.00} " +
                    $"carry={player.carryWeight:0.000} vel={planarSpeed:0.000} " +
                    $"dPos={planarDelta:0.0000} dPosY={delta.y:0.0000} grounded={grounded} " +
                    $"fallNoJump={ReadBool(IsFallingNoJumpField, player)} " +
                    $"fallFromJump={ReadBool(IsFallingFromJumpField, player)} " +
                    $"jumping={ReadBool(IsJumpingField, player)} crouching={player.isCrouching} " +
                    $"crouchMeter={crouchMeter:0.00} hindered={player.isMovementHindered} " +
                    $"disableMove={player.disableMoveInput} special={player.inSpecialInteractAnimation} " +
                    $"camLocal={cameraLocal} camWorldD={cameraWorldDelta} root={rootLocal} arms={armsLocal} " +
                    $"state={animatorState} timeScale={Time.timeScale:0.00}";

                bool inputRise = hasLastPosition &&
                    inputMagnitude >= InputRiseThreshold && lastInputMagnitude < InputRiseThreshold;
                bool walkRise = hasLastPosition && walking && !wasWalking;
                bool hitch = hasLastPosition && deltaTime > HitchSeconds;

                if (hitch && Time.realtimeSinceStartup >= nextHitchLogAllowedAt)
                {
                    nextHitchLogAllowedAt = Time.realtimeSinceStartup + HitchLogIntervalSeconds;
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                        Tag + $"hitch: prevDt={lastDeltaTime * 1000f:0.0}ms " + line);
                }

                if ((inputRise || walkRise) && postRemaining <= 0)
                {
                    startCount++;
                    string trigger = inputRise && walkRise ? "input+walking"
                        : inputRise ? "input" : "walking";
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                        Tag + $"movement_start #{startCount} trigger='{trigger}' " +
                        $"frame={Time.frameCount} preFrames={ringCount} postFrames={PostFrames}.");
                    for (int i = 0; i < ringCount; i++)
                    {
                        int index = (ringNext - ringCount + i + PreFrames) % PreFrames;
                        InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                            Tag + $"pre#{startCount}: " + Ring[index]);
                    }
                    ringCount = 0;
                    postRemaining = PostFrames;
                }

                if (postRemaining > 0)
                {
                    InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                        Tag + $"post#{startCount}: " + line);
                    postRemaining--;
                    if (postRemaining == 0)
                    {
                        InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogInfo(
                            Tag + $"movement_start_end #{startCount} frame={Time.frameCount}.");
                    }
                }
                else
                {
                    Ring[ringNext] = line;
                    ringNext = (ringNext + 1) % PreFrames;
                    if (ringCount < PreFrames)
                        ringCount++;
                }

                wasWalking = walking;
                lastInputMagnitude = inputMagnitude;
                lastPosition = position;
                hasLastPosition = true;
                lastDeltaTime = deltaTime;
            }
            catch (Exception exception)
            {
                if (failureLogged)
                    return;
                failureLogged = true;
                InteractionAnimationApiRestoreDiagnostics.StaticLogger?.LogWarning(
                    Tag + "tick failed; probe disabled for this session: " + exception.Message);
            }
        }

        private static bool ReadBool(FieldInfo field, PlayerControllerB player)
        {
            try { return field != null && field.GetValue(player) is bool value && value; }
            catch { return false; }
        }

        private static float ReadFloat(FieldInfo field, PlayerControllerB player)
        {
            try { return field != null && field.GetValue(player) is float value ? value : float.NaN; }
            catch { return float.NaN; }
        }

        private static Vector3 ReadVector3(FieldInfo field, PlayerControllerB player)
        {
            try { return field != null && field.GetValue(player) is Vector3 value ? value : Vector3.zero; }
            catch { return Vector3.zero; }
        }
    }
}
