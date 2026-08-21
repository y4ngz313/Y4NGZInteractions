using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Text;
using BepInEx.Configuration;
using BepInEx.Logging;
using GameNetcodeStuff;
using HarmonyLib;
using UnityEngine;

namespace Y4NGZInteractions.InteractionAnimationApi
{
    internal static class InteractionAnimationApiRestoreDiagnostics
    {
        internal const string ConfigSection = "Interaction Animation API Restore Diagnostics";
        internal const string DefaultRestoreStateMode = "fresh";

        private const int RestoreHistoryFrames = 3;
        private const int RestoreFutureFrames = 6;
        private const int StartRenderFutureFrames = 3;
        private const int StopRenderFutureFrames = 5;
        private const int MaxAnimatorLayers = 32;
        private const float RigPositionDeltaThreshold = 0.0005f;
        private const float RigRotationDeltaThresholdDegrees = 0.05f;
        private const float ThirdPersonRigRestPositionThreshold = 0.05f;
        private const float ThirdPersonRigRestRotationThresholdDegrees = 8f;
        private const float ThirdPersonRigRestScaleThreshold = 0.01f;
        private const float RemoteRigDiffPostRestoreSeconds = 5f;
        // ChainIKConstraintJobBinder/TwoBoneIKConstraintJobBinder bake world-space link lengths,
        // maxReach, and the maintain*Offset target offsets into persistent native arrays at
        // RigBuilder.Build() time and never re-derive them. A link measured across a collapsed
        // bone (scale ~0) bakes a near-zero length that survives the session.
        private const float IkBakeDegenerateThreshold = 1e-3f;
        private const int IkBakeChainWalkGuard = 64;
        internal const string IkBakeProbePhaseAwake = "awake";
        internal const string IkBakeProbePhasePreStartBuild = "pre-start-build";
        internal const string IkBakeProbePhasePreRestoreBuild = "pre-restore-build";
        internal const string IkBakeProbePhasePostRestore = "post-restore";
        private const string TwoBoneIkConstraintTypeName =
            "UnityEngine.Animations.Rigging.TwoBoneIKConstraint";
        private const string ChainIkConstraintTypeName =
            "UnityEngine.Animations.Rigging.ChainIKConstraint";
        private const string RigTypeName = "UnityEngine.Animations.Rigging.Rig";
        private const string RigBuilderTypeName = "UnityEngine.Animations.Rigging.RigBuilder";

        private static readonly RestoreFrameSample[] RestoreHistory =
        {
            new RestoreFrameSample(MaxAnimatorLayers),
            new RestoreFrameSample(MaxAnimatorLayers),
            new RestoreFrameSample(MaxAnimatorLayers)
        };

        private static ManualLogSource logger;
        private static ConfigEntry<bool> enableRestoreSeamFrameLogger;
        private static ConfigEntry<bool> enableRestoreRigStateLogger;
        private static ConfigEntry<bool> enablePristineRigDiffProbe;
        private static ConfigEntry<bool> restoreRigControlPose;
        private static ConfigEntry<bool> restorePristineRigControlPose;
        private static ConfigEntry<bool> restoreThirdPersonRigControlPose;
        private static ConfigEntry<bool> restorePristineThirdPersonRigControlPose;
        private static ConfigEntry<bool> recaptureImplausiblePristineRigBaseline;
        private static ConfigEntry<bool> restoreVanillaArmsGlue;
        private static ConfigEntry<bool> restoreCameraPin;
        private static ConfigEntry<bool> restoreCameraRotation;
        private static ConfigEntry<bool> stabilizeCameraRotationDuringSession;
        private static ConfigEntry<bool> restoreCameraRotationSnapToRest;
        private static ConfigEntry<bool> restoreCameraChainPositionSnapToRest;
        private static ConfigEntry<bool> healCameraDriftAtSessionStart;
        private static ConfigEntry<bool> restoreVisorPose;
        private static ConfigEntry<bool> hardVisorGlueDuringSession;
        private static ConfigEntry<bool> enableExternalCameraPresentationLogger;
        private static ConfigEntry<bool> enableRemoteRigDiffProbe;
        private static ConfigEntry<bool> enableIkBakeProbe;
        private static ConfigEntry<string> restoreStateMode;
        private static InteractionAnimationRestoreDiagnosticsRunner runner;
        private static PlayerControllerB observedPlayer;
        private static PlayerControllerB renderSessionPlayer;
        private static Camera renderSessionGameplayCamera;
        private static Transform renderSessionCamera;
        private static Transform renderSessionRightHand;
        private static Transform renderSessionLeftHand;
        private static Transform renderSessionArmsMetarig;
        private static Transform renderSessionLocalArms;
        private static Transform renderSessionLocalVisor;
        private static Transform renderSessionLocalVisorTargetPoint;
        private static Camera renderSessionVisorCamera;
        private static readonly RenderSeamFrameSample LatestRenderFrame =
            new RenderSeamFrameSample();
        private static readonly RenderSeamFrameSample PendingStartBeforeRenderFrame =
            new RenderSeamFrameSample();
        private static readonly RenderSeamWindow StartRenderSeam =
            new RenderSeamWindow("start");
        private static readonly RenderSeamWindow StopRenderSeam =
            new RenderSeamWindow("stop");
        private static readonly List<Renderer> RenderVisibilityBuffer =
            new List<Renderer>(16);
        private static PristineRigSnapshot pristineRig;
        private static readonly Dictionary<PlayerControllerB, ThirdPersonRigPoseSnapshot>
            PristineThirdPersonRigPoses =
                new Dictionary<PlayerControllerB, ThirdPersonRigPoseSnapshot>();
        private static readonly Dictionary<PlayerControllerB, CameraChainPoseSnapshot>
            PristineCameraChainPoses =
                new Dictionary<PlayerControllerB, CameraChainPoseSnapshot>();
        private static readonly Dictionary<PlayerControllerB, int> ActiveRemoteRigProbeIds =
            new Dictionary<PlayerControllerB, int>();
        private static readonly List<RemoteRigProbeSchedule> PendingRemoteRigProbes =
            new List<RemoteRigProbeSchedule>();
        // Keyed per player like the sibling Awake captures: the local player is unknown at Awake,
        // so every player gets its own reference bake line instead of one global first-Awake line.
        private static readonly HashSet<PlayerControllerB> IkBakeProbeAwakeLoggedPlayers =
            new HashSet<PlayerControllerB>();
        private static bool customAnimationHasRun;
        private static bool coordinatorLateUpdateTick;
        private static bool initialized;
        private static int restoreHistoryNext;
        private static int restoreHistoryCount;
        private static int restoreFutureFramesRemaining;
        private static int restoreFutureFrameIndex;
        private static int activeStopFrame;
        private static string activeStopInvocation = "consumer_call";
        private static bool renderSamplerSubscribed;
        private static int pendingRigDiffLateUpdates = -1;
        private static bool samplerFailureLogged;
        private static bool twoBoneIkTypeUnavailableLogged;
        private static bool chainIkTypeUnavailableLogged;
        private static bool invalidRestoreStateModeWarningLogged;
        private static bool prepareForLiveBodyStartUninitializedLogged;
        private static bool remoteRigProbeSessionBeginUninitializedLogged;
        private static bool playerAwakeCaptureUninitializedLogged;
        private static PlayerControllerB ikBakeProbePlayer;
        private static int pendingIkBakeProbeLateUpdates = -1;
        private static int nextRemoteRigProbeId;

        internal static bool RestoreScopedCameraPinEnabled =>
            initialized && ReadEnabled(restoreCameraPin, true);

        internal static bool RestoreCameraRotationEnabled =>
            initialized && ReadEnabled(restoreCameraRotation, true);

        internal static bool StabilizeCameraRotationDuringSessionEnabled =>
            initialized && ReadEnabled(stabilizeCameraRotationDuringSession, true);

        internal static bool RestoreCameraRotationSnapToRestEnabled =>
            initialized && ReadEnabled(restoreCameraRotationSnapToRest, true);

        internal static bool RestoreCameraChainPositionSnapToRestEnabled =>
            initialized && ReadEnabled(restoreCameraChainPositionSnapToRest, true);

        internal static bool HealCameraDriftAtSessionStartEnabled =>
            initialized && ReadEnabled(healCameraDriftAtSessionStart, true);

        internal static ManualLogSource StaticLogger => logger ?? Plugin.Log;

        internal static bool RestoreVisorPoseEnabled =>
            initialized && ReadEnabled(restoreVisorPose, true);

        internal static bool HardVisorGlueDuringSessionEnabled =>
            initialized && ReadEnabled(hardVisorGlueDuringSession, true);

        internal static bool ExternalCameraPresentationLoggerEnabled =>
            initialized && ReadEnabled(enableExternalCameraPresentationLogger, false);

        internal static bool RestoreSeamFrameLoggerEnabled =>
            initialized && ReadEnabled(enableRestoreSeamFrameLogger, false);

        internal static bool RestoreRigControlPoseEnabled =>
            initialized && ReadEnabled(restoreRigControlPose, true);

        internal static bool RestorePristineRigControlPoseEnabled =>
            initialized && ReadEnabled(restorePristineRigControlPose, true);

        internal static bool RestoreThirdPersonRigControlPoseEnabled =>
            initialized && ReadEnabled(restoreThirdPersonRigControlPose, true);

        internal static bool RestorePristineThirdPersonRigControlPoseEnabled =>
            initialized && ReadEnabled(restorePristineThirdPersonRigControlPose, true);

        internal static bool RecaptureImplausiblePristineRigBaselineEnabled =>
            initialized && ReadEnabled(recaptureImplausiblePristineRigBaseline, true);

        internal static bool IkBakeProbeEnabled =>
            initialized && ReadEnabled(enableIkBakeProbe, true);

        internal static bool RestoreVanillaArmsGlueEnabled =>
            initialized && ReadEnabled(restoreVanillaArmsGlue, true);

        internal static AnimatorStateRestoreMode ReadRestoreStateMode()
        {
            string configuredMode;
            try
            {
                configuredMode = restoreStateMode != null
                    ? restoreStateMode.Value
                    : DefaultRestoreStateMode;
            }
            catch
            {
                configuredMode = DefaultRestoreStateMode;
            }

            if (string.Equals(configuredMode, "fresh", StringComparison.OrdinalIgnoreCase))
                return AnimatorStateRestoreMode.Fresh;
            if (string.Equals(configuredMode, "crossfade", StringComparison.OrdinalIgnoreCase))
                return AnimatorStateRestoreMode.Crossfade;
            if (string.Equals(configuredMode, "replay", StringComparison.OrdinalIgnoreCase))
                return AnimatorStateRestoreMode.Replay;

            if (!invalidRestoreStateModeWarningLogged)
            {
                invalidRestoreStateModeWarningLogged = true;
                logger?.LogWarning(
                    "[RestoreSeam.mode] invalid Restore State Mode " +
                    $"'{configuredMode ?? "<null>"}'; using '{DefaultRestoreStateMode}'.");
            }

            return AnimatorStateRestoreMode.Fresh;
        }

        internal static void Initialize(ConfigFile config, ManualLogSource log)
        {
            if (initialized)
                return;

            logger = log;
            if (config != null)
            {
                enableRestoreSeamFrameLogger = config.Bind(
                    ConfigSection,
                    "Enable Restore Seam Frame Logger",
                    false,
                    "Samples local-player state around each live-body Stop and samples final rendered transforms, visibility, camera parameters, and timing around both Start and Stop seams.");
                enableRestoreRigStateLogger = config.Bind(
                    ConfigSection,
                    "Enable Restore Rig State Logger",
                    false,
                    "Logs every player-body animator layer immediately before and after the restore RigBuilder.Build loop, then after the final Animator.Update(0).");
                enablePristineRigDiffProbe = config.Bind(
                    ConfigSection,
                    "Enable Pristine Rig Diff Probe",
                    false,
                    "Captures the local first-person RigArms controls before any API live-body animation and enables automatic post-restore diffs.");
                restoreRigControlPose = config.Bind(
                    ConfigSection,
                    "Restore Rig Control Pose",
                    true,
                    "When enabled, every live-body session captures the player's RigArms control subtree before the controller swap and restores it after the vanilla animator snapshot but before RigBuilder rebuilds.");
                restorePristineRigControlPose = config.Bind(
                    ConfigSection,
                    "Restore Pristine Rig Control Pose",
                    true,
                    "When enabled, local live-body restores use the startup-pristine RigArms local poses first and use the equip-time capture only for transforms with no pristine baseline.");
                restoreThirdPersonRigControlPose = config.Bind(
                    ConfigSection,
                    "Restore Third-Person Rig Control Pose",
                    true,
                    "When enabled, every local or remote live-body session captures and restores the third-person arm targets, leg control groups, and shin pole-seed rotations. This is a new kill-switch so existing profiles receive the default-on fix.");
                restorePristineThirdPersonRigControlPose = config.Bind(
                    ConfigSection,
                    "Restore Pristine Third-Person Rig Control Pose",
                    true,
                    "When enabled, third-person rig restores use each player's serialized authored-default pose captured before PlayerControllerB.Awake as the primary source and the session-entry pose only as fallback. Vanilla-rest plausibility is logged as secondary sanity telemetry and does not reject the authored baseline.");
                recaptureImplausiblePristineRigBaseline = config.Bind(
                    ConfigSection,
                    "Recapture Implausible Pristine Rig Baseline",
                    true,
                    "When enabled, a third-person rig baseline captured at PlayerControllerB.Awake that the vanilla-rest sanity check flagged implausible is replaced by a fresh capture taken at the first verified-clean idle session entry (standing, not in a special animation, near-zero horizontal speed, camera at vanilla rest). The recapture runs only for the local player and only through the session-entry camera-drift heal, so it requires Heal Camera Drift At Session Start to be enabled and does not repair remote players' baselines (their Stops keep restoring the Awake-time capture). This is a new key so existing profiles receive the default-on fix.");
                restoreVanillaArmsGlue = config.Bind(
                    ConfigSection,
                    "Restore Vanilla Arms Glue",
                    true,
                    "When enabled, local live-body restores apply vanilla's camera-Y and first-person arm glue before the same-frame Animator and RigBuilder evaluation.");
                restoreCameraPin = config.Bind(
                    ConfigSection,
                    "Restore Camera Pin",
                    true,
                    "When enabled, every local live-body Stop pins the camera at its Stop-entry player-local position through the restore and two LateUpdates. Supersedes the old Enable Restore-Scoped Camera Pin key so existing profiles receive the default-on setting.");
                restoreCameraRotation = config.Bind(
                    ConfigSection,
                    "Restore Camera Rotation",
                    true,
                    "When enabled, local live-body Start and Stop preserve the gameplay camera and camera-container local rotations across controller swaps and Animator.Rebind. This is a new key so existing profiles receive the default-on seam fix.");
                stabilizeCameraRotationDuringSession = config.Bind(
                    ConfigSection,
                    "Stabilize Camera Rotation During Session",
                    true,
                    "When enabled, local live-body sessions preserve vanilla-owned gameplay-camera pitch while writing local yaw and roll as absolute values from the session-entry baseline after other LateUpdate writers and immediately before rendering. This prevents consumer camera effects from feeding their previous output back into later frames.");
                restoreCameraRotationSnapToRest = config.Bind(
                    ConfigSection,
                    "Restore Camera Rotation Snap To Rest",
                    true,
                    "When enabled, local live-body Stop preserves gameplay-camera pitch but discards stop-entry yaw/roll, snapping them to zero, and restores CameraContainer exactly to its authored local rest rotation (90, 359.8182, 0). The stop gate logs the discarded residue.");
                restoreCameraChainPositionSnapToRest = config.Bind(
                    ConfigSection,
                    "Restore Camera Chain Position Snap To Rest",
                    true,
                    "When enabled, local live-body Stop restores the local positions of CameraContainer, the gameplay camera, the first-person arms metarig root, and the local-arms transform to the authored defaults captured before PlayerControllerB.Awake. Authored clips can write positions on these transforms that vanilla never rewrites; without this snap each session leaves a small permanent viewpoint offset that accumulates. This is a new key so existing profiles receive the default-on fix.");
                healCameraDriftAtSessionStart = config.Bind(
                    ConfigSection,
                    "Heal Camera Drift At Session Start",
                    true,
                    "When enabled, each local live-body session start measures the gameplay camera against its vanilla player-local rest position (0, 2.35, 0.01) and, when it deviates by more than the heal threshold (2 cm) but less than the displacement-guard threshold, restores the camera chain local positions to the authored defaults before capturing the session baseline. This repairs viewpoint drift accumulated by earlier sessions or other mods instead of adopting the contaminated pose as the restore target. This is a new key so existing profiles receive the default-on fix.");
                restoreVisorPose = config.Bind(
                    ConfigSection,
                    "Restore Visor Pose",
                    true,
                    "When enabled, local live-body Start and Stop preserve animator-owned helmet-visor transforms across controller swaps and Animator.Rebind, then synchronously restore vanilla visor position glue without advancing its rotation lerp. This is a new key so existing profiles receive the default-on seam fix.");
                hardVisorGlueDuringSession = config.Bind(
                    ConfigSection,
                    "Hard Visor Glue During Session",
                    true,
                    "When enabled, first-person local live-body sessions re-glue the helmet visor to its camera target point (position AND rotation, no lerp) just before each render. Vanilla's own glue smooths rotation at 53 deg/s, which lags fast scripted or animated camera moves and sweeps the mask edge into frame; this keeps the mask stable so consumers no longer need to hide it. A visor a consumer has parked away from the camera is left alone, and consumer-owned camera sessions always stand down because their camera owner also owns visor presentation.");
                enableExternalCameraPresentationLogger = config.Bind(
                    ConfigSection,
                    "Enable External Camera Presentation Logger",
                    false,
                    "For consumer-owned-camera BodyWorld sessions, logs the final pre-render body, first-person-arms, and local-visor renderer state once at entry and again only when render eligibility changes. This low-noise invariant probe identifies local presentation leaks without enabling the full restore seam frame logger.");
                enableRemoteRigDiffProbe = config.Bind(
                    ConfigSection,
                    "Enable Remote Rig Diff Probe",
                    false,
                    "When enabled, every remote live-body session logs third-person arm-target, leg-target, and shin local TRS at session begin, two LateUpdates after restore, and five seconds after restore. This verbose diagnostics probe remains opt-in.");
                enableIkBakeProbe = config.Bind(
                    ConfigSection,
                    "Enable IK Bake Probe",
                    true,
                    "When enabled, the ChainIK and TwoBoneIK constraints under the local player's first-person arms metarig are sampled at four checkpoints per live-body session (first PlayerControllerB.Awake, immediately before the start rebuild, immediately before the restore rebuild, and two LateUpdates after restore) and each sample logs the world-space link distances, maxReach, and tip-versus-target offsets that RigBuilder.Build() bakes permanently into its job arrays. A link distance or bone lossy scale below 0.001 at either pre-build checkpoint raises a warning, because that bake outlives the session and leaves the affected limb mispositioned in vanilla animation. Four compact rounds per session; safe to leave on.");
                restoreStateMode = config.Bind(
                    ConfigSection,
                    "Restore State Mode",
                    DefaultRestoreStateMode,
                    new ConfigDescription(
                        "Selects how vanilla animator layer states resume at live-body Stop: fresh starts from controller defaults, crossfade blends to captured states, and replay immediately restores captured states.",
                        new AcceptableValueList<string>("fresh", "crossfade", "replay")));
            }

            bool frameLoggerEnabled = ReadEnabled(enableRestoreSeamFrameLogger, false);
            bool rigStateLoggerEnabled = ReadEnabled(enableRestoreRigStateLogger, false);
            bool rigDiffEnabled = ReadEnabled(enablePristineRigDiffProbe, false);
            bool rigControlPoseEnabled = ReadEnabled(restoreRigControlPose, true);
            bool pristineRigControlPoseEnabled =
                ReadEnabled(restorePristineRigControlPose, true);
            bool thirdPersonRigControlPoseEnabled =
                ReadEnabled(restoreThirdPersonRigControlPose, true);
            bool pristineThirdPersonRigControlPoseEnabled =
                ReadEnabled(restorePristineThirdPersonRigControlPose, true);
            bool vanillaArmsGlueEnabled = ReadEnabled(restoreVanillaArmsGlue, true);
            bool cameraPinEnabled = ReadEnabled(restoreCameraPin, true);
            bool cameraRotationEnabled = ReadEnabled(restoreCameraRotation, true);
            bool stabilizeCameraRotationDuringSessionEnabled =
                ReadEnabled(stabilizeCameraRotationDuringSession, true);
            bool cameraRotationSnapToRestEnabled =
                ReadEnabled(restoreCameraRotationSnapToRest, true);
            bool cameraChainPositionSnapToRestEnabled =
                ReadEnabled(restoreCameraChainPositionSnapToRest, true);
            bool healCameraDriftAtSessionStartEnabled =
                ReadEnabled(healCameraDriftAtSessionStart, true);
            bool visorPoseEnabled = ReadEnabled(restoreVisorPose, true);
            bool externalCameraPresentationLoggerEnabled =
                ReadEnabled(enableExternalCameraPresentationLogger, false);
            bool remoteRigDiffProbeEnabled =
                ReadEnabled(enableRemoteRigDiffProbe, false);
            bool ikBakeProbeEnabled = ReadEnabled(enableIkBakeProbe, true);
            initialized = true;

            if (frameLoggerEnabled)
                SubscribeRenderSampler();

            if (frameLoggerEnabled || rigDiffEnabled || pristineRigControlPoseEnabled ||
                pristineThirdPersonRigControlPoseEnabled || remoteRigDiffProbeEnabled ||
                ikBakeProbeEnabled)
            {
                try
                {
                    GameObject host = Plugin.Instance != null ? Plugin.Instance.gameObject : null;
                    if (host != null)
                        runner = host.AddComponent<InteractionAnimationRestoreDiagnosticsRunner>();
                    else
                        logger?.LogWarning(
                            "[RestoreSeam] diagnostics_runner_unavailable: plugin GameObject was not available.");
                }
                catch (Exception exception)
                {
                    logger?.LogWarning(
                        "[RestoreSeam] diagnostics_runner_failed: " + exception.Message);
                }
            }

            if (frameLoggerEnabled || rigStateLoggerEnabled || rigDiffEnabled ||
                rigControlPoseEnabled || pristineRigControlPoseEnabled ||
                thirdPersonRigControlPoseEnabled ||
                pristineThirdPersonRigControlPoseEnabled ||
                vanillaArmsGlueEnabled || cameraPinEnabled || cameraRotationEnabled ||
                stabilizeCameraRotationDuringSessionEnabled ||
                cameraRotationSnapToRestEnabled ||
                cameraChainPositionSnapToRestEnabled ||
                healCameraDriftAtSessionStartEnabled ||
                visorPoseEnabled || externalCameraPresentationLoggerEnabled ||
                remoteRigDiffProbeEnabled || ikBakeProbeEnabled)
            {
                logger?.LogInfo(
                    "[RestoreSeam] diagnostics_ready: " +
                    $"frameLogger={frameLoggerEnabled} " +
                    $"rigStateLogger={rigStateLoggerEnabled} " +
                    $"rigDiff={rigDiffEnabled} " +
                    $"restoreRigControlPose={rigControlPoseEnabled} " +
                    $"restorePristineRigControlPose={pristineRigControlPoseEnabled} " +
                    $"restoreThirdPersonRigControlPose={thirdPersonRigControlPoseEnabled} " +
                    $"restorePristineThirdPersonRigControlPose={pristineThirdPersonRigControlPoseEnabled} " +
                    $"restoreVanillaArmsGlue={vanillaArmsGlueEnabled} " +
                    $"restoreCameraPin={cameraPinEnabled} " +
                    $"restoreCameraRotation={cameraRotationEnabled} " +
                    $"stabilizeCameraRotationDuringSession={stabilizeCameraRotationDuringSessionEnabled} " +
                    $"restoreCameraRotationSnapToRest={cameraRotationSnapToRestEnabled} " +
                    $"restoreCameraChainPositionSnapToRest={cameraChainPositionSnapToRestEnabled} " +
                    $"healCameraDriftAtSessionStart={healCameraDriftAtSessionStartEnabled} " +
                    $"restoreVisorPose={visorPoseEnabled} " +
                    $"externalCameraPresentationLogger={externalCameraPresentationLoggerEnabled} " +
                    $"remoteRigDiffProbe={remoteRigDiffProbeEnabled} " +
                    $"ikBakeProbe={ikBakeProbeEnabled}.");
            }
        }

        internal static void Shutdown()
        {
            initialized = false;
            coordinatorLateUpdateTick = false;
            UnsubscribeRenderSampler();

            if (runner != null)
            {
                try
                {
                    runner.enabled = false;
                    UnityEngine.Object.Destroy(runner);
                }
                catch { }
            }

            runner = null;
            observedPlayer = null;
            ClearRenderSessionTransforms();
            pristineRig = null;
            PristineThirdPersonRigPoses.Clear();
            PristineCameraChainPoses.Clear();
            ActiveRemoteRigProbeIds.Clear();
            PendingRemoteRigProbes.Clear();
            customAnimationHasRun = false;
            ResetRestoreHistory();
            pendingRigDiffLateUpdates = -1;
            samplerFailureLogged = false;
            twoBoneIkTypeUnavailableLogged = false;
            chainIkTypeUnavailableLogged = false;
            invalidRestoreStateModeWarningLogged = false;
            prepareForLiveBodyStartUninitializedLogged = false;
            remoteRigProbeSessionBeginUninitializedLogged = false;
            playerAwakeCaptureUninitializedLogged = false;
            IkBakeProbeAwakeLoggedPlayers.Clear();
            ikBakeProbePlayer = null;
            pendingIkBakeProbeLateUpdates = -1;
            nextRemoteRigProbeId = 0;
            enableRestoreSeamFrameLogger = null;
            enableRestoreRigStateLogger = null;
            enablePristineRigDiffProbe = null;
            restoreRigControlPose = null;
            restorePristineRigControlPose = null;
            restoreThirdPersonRigControlPose = null;
            restorePristineThirdPersonRigControlPose = null;
            recaptureImplausiblePristineRigBaseline = null;
            restoreVanillaArmsGlue = null;
            restoreCameraPin = null;
            restoreCameraRotation = null;
            stabilizeCameraRotationDuringSession = null;
            restoreCameraRotationSnapToRest = null;
            restoreCameraChainPositionSnapToRest = null;
            healCameraDriftAtSessionStart = null;
            restoreVisorPose = null;
            hardVisorGlueDuringSession = null;
            enableExternalCameraPresentationLogger = null;
            enableRemoteRigDiffProbe = null;
            enableIkBakeProbe = null;
            restoreStateMode = null;
            logger = null;
        }

        internal static void BeginCoordinatorLateUpdateTick()
        {
            if (!initialized || !ReadEnabled(enableRestoreSeamFrameLogger, false))
                return;

            coordinatorLateUpdateTick = true;
        }

        internal static void EndCoordinatorLateUpdateTick()
        {
            coordinatorLateUpdateTick = false;
        }

        internal static void PrepareForLiveBodyStart(PlayerControllerB player)
        {
            if (!initialized)
            {
                if (!prepareForLiveBodyStartUninitializedLogged)
                {
                    prepareForLiveBodyStartUninitializedLogged = true;
                    StaticLogger?.LogInfo(
                        "[RestoreSeam.tprig] pristine_capture_skipped: " +
                        $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                        "initialized=False reason='restore_diagnostics_not_initialized' " +
                        "action='skip_prepare_for_live_body_start'.");
                }
                return;
            }

            LogPristineThirdPersonRigPoseAvailability(player);
            if (!IsLocalPlayer(player))
                return;

            ObservePlayer(player);
            if (ReadEnabled(enableRestoreSeamFrameLogger, false))
            {
                SubscribeRenderSampler();
                CacheRenderSessionTransforms(player);
                if (LatestRenderFrame.Valid &&
                    ReferenceEquals(LatestRenderFrame.Player, player))
                {
                    PendingStartBeforeRenderFrame.CopyFrom(LatestRenderFrame);
                }
                else
                {
                    CaptureRenderFrame(player, PendingStartBeforeRenderFrame);
                }
            }

            if (!PristineRigCaptureEnabled())
                return;

            if (!customAnimationHasRun)
                TryCapturePristineRig(player);
        }

        internal static void NotifyLiveBodyStarted(
            PlayerControllerB player,
            GameObject animatedProp)
        {
            if (!initialized)
                return;

            if (!ReadEnabled(enableRestoreSeamFrameLogger, false) ||
                !IsLocalPlayer(player))
            {
                return;
            }

            try
            {
                ObservePlayer(player);
                if (!ReferenceEquals(renderSessionPlayer, player))
                    CacheRenderSessionTransforms(player);
                SubscribeRenderSampler();

                int startFrame = Time.frameCount;
                StartRenderSeam.Activate(
                    startFrame,
                    startFrame + StartRenderFutureFrames,
                    animatedProp);
                if (PendingStartBeforeRenderFrame.Valid &&
                    ReferenceEquals(PendingStartBeforeRenderFrame.Player, player))
                {
                    LogRenderFrame(
                        PendingStartBeforeRenderFrame,
                        StartRenderSeam,
                        "before",
                        captureAnimatedProp: false);
                }
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RestoreSeam.render] start_activate_failed: " + exception.Message);
            }
        }

        internal static void NotifyLiveBodyAnimationRan(PlayerControllerB player)
        {
            if (!initialized || !PristineRigCaptureEnabled() || !IsLocalPlayer(player))
            {
                return;
            }

            ObservePlayer(player);
            customAnimationHasRun = true;
        }

        internal static bool TryRestorePristineRigControlPose(
            PlayerControllerB player,
            Transform rigArms,
            ISet<Transform> restoredTransforms,
            out int restored)
        {
            restored = 0;
            if (!RestorePristineRigControlPoseEnabled || !IsLocalPlayer(player) ||
                rigArms == null || restoredTransforms == null)
            {
                return false;
            }

            try
            {
                if (pristineRig == null || !ReferenceEquals(pristineRig.Player, player))
                    return false;

                restored = pristineRig.RestoreRigControlPose(
                    rigArms,
                    restoredTransforms);
                return restored > 0;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RestoreSeam.rigpose] pristine_restore_failed: " + exception.Message);
                restored = 0;
                restoredTransforms.Clear();
                return false;
            }
        }

        internal static ThirdPersonRigPoseSnapshot CaptureThirdPersonRigControlPose(
            PlayerControllerB player)
        {
            return ThirdPersonRigPoseSnapshot.Capture(player);
        }

        internal static bool TryRestorePristineThirdPersonRigControlPose(
            PlayerControllerB player,
            ISet<Transform> restoredTransforms,
            out int fullPoseRestored,
            out int rotationOnlyRestored,
            out string gateReason)
        {
            fullPoseRestored = 0;
            rotationOnlyRestored = 0;
            gateReason = "unknown";
            if (!RestorePristineThirdPersonRigControlPoseEnabled)
            {
                gateReason = "kill_switch_disabled";
                return false;
            }

            if (player == null)
            {
                gateReason = "player_missing";
                return false;
            }

            if (restoredTransforms == null)
            {
                gateReason = "restore_set_missing";
                return false;
            }

            if (!PristineThirdPersonRigPoses.TryGetValue(
                    player,
                    out ThirdPersonRigPoseSnapshot pristinePose) ||
                pristinePose == null)
            {
                gateReason = "pristine_baseline_unavailable";
                return false;
            }

            try
            {
                pristinePose.RestoreExcept(
                    null,
                    restoredTransforms,
                    out fullPoseRestored,
                    out rotationOnlyRestored);
                gateReason =
                    fullPoseRestored + rotationOnlyRestored > 0
                        ? "pristine_primary_restored"
                        : "pristine_targets_unavailable";
                return fullPoseRestored + rotationOnlyRestored > 0;
            }
            catch (Exception exception)
            {
                gateReason = "restore_failed:" + exception.Message;
                fullPoseRestored = 0;
                rotationOnlyRestored = 0;
                restoredTransforms.Clear();
                return false;
            }
        }

        /// <summary>
        /// One-time upgrade of a player's pristine third-person rig baseline. The Awake-time
        /// capture can be contaminated (movement clips already evaluated, flagged implausible
        /// against vanilla rest); the caller verifies a clean idle session entry and this
        /// replaces the stored snapshot with a fresh, plausible capture. Returns false without
        /// touching the baseline when there is nothing to fix or the new candidate is itself
        /// implausible (a later clean entry retries).
        /// </summary>
        internal static bool TryRecapturePristineThirdPersonRigPoseIfImplausible(
            PlayerControllerB player,
            out string reason)
        {
            reason = string.Empty;
            if (!initialized)
            {
                reason = "restore_diagnostics_not_initialized";
                return false;
            }

            if (!RecaptureImplausiblePristineRigBaselineEnabled)
            {
                reason = "kill_switch_disabled";
                return false;
            }

            if (player == null)
            {
                reason = "player_missing";
                return false;
            }

            if (!PristineThirdPersonRigPoses.TryGetValue(
                    player,
                    out ThirdPersonRigPoseSnapshot stored) || stored == null)
            {
                reason = "pristine_baseline_unavailable";
                return false;
            }

            if (string.Equals(
                    stored.Source,
                    ThirdPersonRigRuntimeSettledSource,
                    StringComparison.Ordinal))
            {
                reason = "already_recaptured";
                return false;
            }

            if (stored.PlausibleAtCapture)
            {
                reason = "baseline_already_plausible";
                return false;
            }

            try
            {
                ThirdPersonRigPoseSnapshot candidate =
                    ThirdPersonRigPoseSnapshot.Capture(player);
                if (candidate == null || candidate.TotalCount == 0 || !candidate.IsComplete ||
                    !candidate.EvaluateVanillaRestPlausibility().Plausible)
                {
                    reason = "candidate_still_implausible";
                    logger?.LogInfo(
                        "[RestoreSeam.tprig] rest_baseline_recapture_skipped: " +
                        $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                        $"candidatePresent={candidate != null} " +
                        $"candidateComplete={candidate != null && candidate.IsComplete} " +
                        "reason='candidate_still_implausible' action='retry_next_clean_entry'.");
                    return false;
                }

                ThirdPersonRigPlausibility storedPlausibility =
                    stored.EvaluateVanillaRestPlausibility();
                candidate.SetCaptureMetadata(
                    plausibleAtCapture: true,
                    ThirdPersonRigRuntimeSettledSource);
                PristineThirdPersonRigPoses[player] = candidate;
                logger?.LogInfo(
                    "[RestoreSeam.tprig] rest_baseline_recaptured: " +
                    $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                    $"outliersBefore={storedPlausibility.PositionOutliers + storedPlausibility.RotationOutliers + storedPlausibility.ScaleOutliers} " +
                    $"fullPoseTransforms={candidate.FullPoseCount} " +
                    $"rotationOnlyTransforms={candidate.RotationOnlyCount} " +
                    $"source='{ThirdPersonRigRuntimeSettledSource}' " +
                    "action='replace_authored_default_with_runtime_settled'.");
                return true;
            }
            catch (Exception exception)
            {
                reason = "recapture_failed:" + exception.Message;
                logger?.LogInfo(
                    "[RestoreSeam.tprig] rest_baseline_recapture_skipped: " +
                    $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                    $"reason='{SanitizeLogValue(reason)}' action='retry_next_clean_entry'.");
                return false;
            }
        }

        internal static void NotifyStop(PlayerControllerB player, GameObject animatedProp)
        {
            if (!initialized || !ReadEnabled(enableRestoreSeamFrameLogger, false) ||
                !IsLocalPlayer(player))
            {
                return;
            }

            ObservePlayer(player);
            if (!ReferenceEquals(renderSessionPlayer, player))
                CacheRenderSessionTransforms(player);
            activeStopFrame = Time.frameCount;
            activeStopInvocation = coordinatorLateUpdateTick
                ? "coordinator_late_update"
                : "consumer_call";
            SubscribeRenderSampler();
            StopRenderSeam.Activate(
                activeStopFrame,
                activeStopFrame + StopRenderFutureFrames,
                animatedProp);

            logger?.LogInfo(
                "[RestoreSeam] " +
                $"frame={activeStopFrame} phase=stop_entry stopInvocation='{activeStopInvocation}' " +
                $"bufferedBefore={restoreHistoryCount} futureFrames={RestoreFutureFrames}.");

            for (int i = 0; i < restoreHistoryCount; i++)
            {
                int sampleIndex =
                    (restoreHistoryNext - restoreHistoryCount + i + RestoreHistoryFrames) %
                    RestoreHistoryFrames;
                int beforeNumber = restoreHistoryCount - i;
                LogRestoreFrame(
                    RestoreHistory[sampleIndex],
                    "before-" + beforeNumber.ToString(CultureInfo.InvariantCulture));
            }

            restoreFutureFramesRemaining = RestoreFutureFrames;
            restoreFutureFrameIndex = 0;
        }

        internal static void NotifyRestoreCompleted(PlayerControllerB player)
        {
            if (!initialized)
                return;

            NotifyRemoteRigProbeRestoreCompleted(player);
            ScheduleIkBakeProbeAfterRestore(player);
            if (!ReadEnabled(enablePristineRigDiffProbe, false) ||
                pristineRig == null || !ReferenceEquals(pristineRig.Player, player))
            {
                return;
            }

            pendingRigDiffLateUpdates = 2;
        }

        internal static void NotifyRemoteRigProbeSessionBegin(PlayerControllerB player)
        {
            if (!initialized)
            {
                if (!remoteRigProbeSessionBeginUninitializedLogged)
                {
                    remoteRigProbeSessionBeginUninitializedLogged = true;
                    StaticLogger?.LogInfo(
                        "[RemoteRigDiff] gate: " +
                        $"frame={Time.frameCount} phase='session_begin' " +
                        $"player='{DescribePlayer(player)}' initialized=False " +
                        "action='skip_restore_diagnostics_not_initialized'.");
                }
                return;
            }

            if (player == null || IsLocalPlayer(player))
                return;

            bool enabled = ReadEnabled(enableRemoteRigDiffProbe, false);
            string playerDescription = DescribePlayer(player);
            if (!enabled)
            {
                logger?.LogInfo(
                    "[RemoteRigDiff] gate: " +
                    $"frame={Time.frameCount} phase='session_begin' " +
                    $"player='{playerDescription}' enabled=False action='skip'.");
                return;
            }

            int probeId = ++nextRemoteRigProbeId;
            ActiveRemoteRigProbeIds[player] = probeId;
            LogRemoteRigPose(player, probeId, "session_begin");
        }

        private static void NotifyRemoteRigProbeRestoreCompleted(PlayerControllerB player)
        {
            if (player == null || IsLocalPlayer(player))
                return;

            bool enabled = ReadEnabled(enableRemoteRigDiffProbe, false);
            string playerDescription = DescribePlayer(player);
            ActiveRemoteRigProbeIds.TryGetValue(player, out int probeId);
            ActiveRemoteRigProbeIds.Remove(player);
            if (probeId <= 0)
                probeId = ++nextRemoteRigProbeId;

            if (!enabled)
            {
                logger?.LogInfo(
                    "[RemoteRigDiff] gate: " +
                    $"frame={Time.frameCount} phase='restore_schedule' " +
                    $"probeId={probeId} player='{playerDescription}' " +
                    "enabled=False action='skip'.");
                return;
            }

            var schedule = new RemoteRigProbeSchedule(
                player,
                probeId,
                Time.frameCount,
                Time.realtimeSinceStartup + RemoteRigDiffPostRestoreSeconds);
            PendingRemoteRigProbes.Add(schedule);
            logger?.LogInfo(
                "[RemoteRigDiff] restore_scheduled: " +
                $"frame={Time.frameCount} probeId={probeId} " +
                $"player='{playerDescription}' lateUpdates=2 " +
                $"seconds={FormatFloat(RemoteRigDiffPostRestoreSeconds)}.");
        }

        private static void TickRemoteRigDiffProbes(bool enabled)
        {
            for (int i = PendingRemoteRigProbes.Count - 1; i >= 0; i--)
            {
                RemoteRigProbeSchedule schedule = PendingRemoteRigProbes[i];
                if (schedule == null)
                {
                    PendingRemoteRigProbes.RemoveAt(i);
                    continue;
                }

                if (!enabled)
                {
                    logger?.LogInfo(
                        "[RemoteRigDiff] gate: " +
                        $"frame={Time.frameCount} phase='scheduled_samples' " +
                        $"probeId={schedule.ProbeId} " +
                        $"player='{DescribePlayer(schedule.Player)}' " +
                        "enabled=False action='cancel'.");
                    PendingRemoteRigProbes.RemoveAt(i);
                    continue;
                }

                if (!schedule.LateUpdateSampleLogged &&
                    Time.frameCount > schedule.RestoreFrame)
                {
                    schedule.LateUpdatesRemaining--;
                    if (schedule.LateUpdatesRemaining <= 0)
                    {
                        schedule.LateUpdateSampleLogged = true;
                        LogRemoteRigPose(
                            schedule.Player,
                            schedule.ProbeId,
                            "restore_plus_2_lateupdates");
                    }
                }

                if (!schedule.FiveSecondSampleLogged &&
                    Time.realtimeSinceStartup >= schedule.FiveSecondRealtime)
                {
                    schedule.FiveSecondSampleLogged = true;
                    LogRemoteRigPose(
                        schedule.Player,
                        schedule.ProbeId,
                        "restore_plus_5_seconds");
                }

                if (schedule.LateUpdateSampleLogged &&
                    schedule.FiveSecondSampleLogged)
                {
                    PendingRemoteRigProbes.RemoveAt(i);
                }
            }
        }

        private static void LogRemoteRigPose(
            PlayerControllerB player,
            int probeId,
            string phase)
        {
            string playerDescription = DescribePlayer(player);
            Transform animatorRoot = null;
            try
            {
                animatorRoot = player != null && player.playerBodyAnimator != null
                    ? player.playerBodyAnimator.transform
                    : null;
            }
            catch { }

            Transform spine003 = FindChildRecursive(animatorRoot, "spine.003");
            Transform rigOne = FindChildRecursive(animatorRoot, "Rig 1");
            Transform leftLeg = FindDirectChild(rigOne, "LeftLeg");
            Transform rightLeg = FindDirectChild(rigOne, "RightLeg");
            Transform leftArmTarget = FindDirectChild(spine003, "LeftArm_target");
            Transform rightArmTarget = FindDirectChild(spine003, "RightArm_target");
            Transform leftLegTarget = FindDirectChild(leftLeg, "LeftLeg_target");
            Transform rightLegTarget = FindDirectChild(rightLeg, "RightLeg_target");
            Transform leftShin = FindChildRecursive(animatorRoot, "shin.L");
            Transform rightShin = FindChildRecursive(animatorRoot, "shin.R");

            logger?.LogInfo(
                "[RemoteRigDiff] sample_begin: " +
                $"frame={Time.frameCount} phase='{phase}' probeId={probeId} " +
                $"player='{playerDescription}' animatorRootPresent={animatorRoot != null}.");
            int present = 0;
            present += LogRemoteRigTransform(
                leftArmTarget, "spine.003/LeftArm_target", playerDescription, probeId, phase);
            present += LogRemoteRigTransform(
                rightArmTarget, "spine.003/RightArm_target", playerDescription, probeId, phase);
            present += LogRemoteRigTransform(
                leftLegTarget, "Rig 1/LeftLeg/LeftLeg_target", playerDescription, probeId, phase);
            present += LogRemoteRigTransform(
                rightLegTarget, "Rig 1/RightLeg/RightLeg_target", playerDescription, probeId, phase);
            present += LogRemoteRigTransform(
                leftShin, "spine/thigh.L/shin.L", playerDescription, probeId, phase);
            present += LogRemoteRigTransform(
                rightShin, "spine/thigh.R/shin.R", playerDescription, probeId, phase);
            logger?.LogInfo(
                "[RemoteRigDiff] sample_end: " +
                $"frame={Time.frameCount} phase='{phase}' probeId={probeId} " +
                $"player='{playerDescription}' present={present} missing={6 - present}.");
        }

        private static int LogRemoteRigTransform(
            Transform transform,
            string path,
            string playerDescription,
            int probeId,
            string phase)
        {
            if (transform == null)
            {
                logger?.LogInfo(
                    "[RemoteRigDiff] transform_unavailable: " +
                    $"frame={Time.frameCount} phase='{phase}' probeId={probeId} " +
                    $"player='{playerDescription}' path='{path}'.");
                return 0;
            }

            try
            {
                logger?.LogInfo(
                    "[RemoteRigDiff] transform: " +
                    $"frame={Time.frameCount} phase='{phase}' probeId={probeId} " +
                    $"player='{playerDescription}' path='{path}' " +
                    $"localPosition={FormatVector(transform.localPosition)} " +
                    $"localRotation={FormatQuaternion(transform.localRotation)} " +
                    $"localEuler={FormatVector(transform.localEulerAngles)} " +
                    $"localScale={FormatVector(transform.localScale)}.");
                return 1;
            }
            catch (Exception exception)
            {
                logger?.LogInfo(
                    "[RemoteRigDiff] transform_unavailable: " +
                    $"frame={Time.frameCount} phase='{phase}' probeId={probeId} " +
                    $"player='{playerDescription}' path='{path}' " +
                    $"reason='read_failed:{SanitizeLogValue(exception.Message)}'.");
                return 0;
            }
        }

        /// <summary>
        /// Records the reference bake state at PlayerControllerB.Awake, before any API session can
        /// have collapsed a bone. The local player is not resolvable this early, so this samples
        /// once per player — the line carries the player identity, and the local player's own
        /// awake line is the baseline its later checkpoints are read against.
        /// </summary>
        internal static void LogIkBakeProbeAtPlayerAwake(PlayerControllerB player)
        {
            if (!IkBakeProbeEnabled || player == null)
                return;

            if (!IkBakeProbeAwakeLoggedPlayers.Add(player))
                return;

            LogIkBakeProbeCore(player, IkBakeProbePhaseAwake);
        }

        /// <summary>
        /// Samples every ChainIK and TwoBoneIK constraint under the local player's first-person
        /// arms metarig — the rig this mod rebuilds — and logs the
        /// world-space measurements RigBuilder.Build() bakes into its persistent job arrays. The
        /// pre-build checkpoints also warn when a link distance or bone lossy scale is degenerate,
        /// because Build() never re-derives those values once the session ends.
        /// </summary>
        internal static void LogIkBakeProbe(PlayerControllerB player, string phase)
        {
            if (!IkBakeProbeEnabled || !IsLocalPlayer(player))
                return;

            LogIkBakeProbeCore(player, phase);
        }

        private static void ScheduleIkBakeProbeAfterRestore(PlayerControllerB player)
        {
            if (!IkBakeProbeEnabled || !IsLocalPlayer(player))
                return;

            ikBakeProbePlayer = player;
            pendingIkBakeProbeLateUpdates = 2;
        }

        private static void TickIkBakeProbe()
        {
            if (pendingIkBakeProbeLateUpdates <= 0)
                return;

            pendingIkBakeProbeLateUpdates--;
            if (pendingIkBakeProbeLateUpdates > 0)
                return;

            PlayerControllerB player = ikBakeProbePlayer;
            ikBakeProbePlayer = null;
            pendingIkBakeProbeLateUpdates = -1;
            LogIkBakeProbeCore(player, IkBakeProbePhasePostRestore);
        }

        private static void LogIkBakeProbeCore(PlayerControllerB player, string phase)
        {
            string playerDescription = DescribePlayer(player);
            try
            {
                Transform playerRoot = player != null ? player.transform : null;
                Transform armsMetarig = player != null ? player.playerModelArmsMetarig : null;
                // Scope to the first-person arms rig this mod actually rebuilds. Scanning the whole
                // player would also sweep the third-person model, held items, and other mods' rigs,
                // so degenerate warnings could fire for constraints nothing here ever touched.
                Transform scanRoot = armsMetarig;
                string scanRootSource = "arms-metarig";
                if (scanRoot == null)
                {
                    scanRoot = FindChildRecursive(playerRoot, "RigArms");
                    scanRootSource = "rig-arms";
                }

                if (scanRoot == null)
                {
                    scanRoot = playerRoot;
                    scanRootSource = "player-fallback";
                }

                if (scanRoot == null)
                {
                    logger?.LogInfo(
                        "[IkBakeProbe] sample_unavailable: " +
                        $"frame={Time.frameCount} phase='{phase}' " +
                        $"player='{playerDescription}' reason='rig_root_unavailable'.");
                    return;
                }

                Type twoBoneIkConstraintType = ResolveTwoBoneIkConstraintType();
                Type chainIkConstraintType = ResolveChainIkConstraintType();
                Component[] twoBoneConstraints =
                    ReadIkConstraints(scanRoot, twoBoneIkConstraintType);
                Component[] chainConstraints =
                    ReadIkConstraints(scanRoot, chainIkConstraintType);
                logger?.LogInfo(
                    "[IkBakeProbe] sample_begin: " +
                    $"frame={Time.frameCount} phase='{phase}' player='{playerDescription}' " +
                    $"scanRoot='{scanRootSource}' root='{scanRoot.name}' " +
                    $"twoBoneIkConstraints={twoBoneConstraints.Length} " +
                    $"chainIkConstraints={chainConstraints.Length} " +
                    $"degenerateThreshold={FormatFloat(IkBakeDegenerateThreshold)}.");

                bool warnOnDegenerate =
                    string.Equals(phase, IkBakeProbePhasePreStartBuild, StringComparison.Ordinal) ||
                    string.Equals(phase, IkBakeProbePhasePreRestoreBuild, StringComparison.Ordinal);
                int logged = 0;
                int degenerate = 0;
                for (int i = 0; i < twoBoneConstraints.Length; i++)
                {
                    if (LogIkBakeConstraint(
                            twoBoneConstraints[i],
                            "TwoBoneIK",
                            scanRoot,
                            phase,
                            playerDescription,
                            warnOnDegenerate,
                            out bool constraintDegenerate))
                    {
                        logged++;
                    }

                    if (constraintDegenerate)
                        degenerate++;
                }

                for (int i = 0; i < chainConstraints.Length; i++)
                {
                    if (LogIkBakeConstraint(
                            chainConstraints[i],
                            "ChainIK",
                            scanRoot,
                            phase,
                            playerDescription,
                            warnOnDegenerate,
                            out bool constraintDegenerate))
                    {
                        logged++;
                    }

                    if (constraintDegenerate)
                        degenerate++;
                }

                logger?.LogInfo(
                    "[IkBakeProbe] sample_end: " +
                    $"frame={Time.frameCount} phase='{phase}' player='{playerDescription}' " +
                    $"constraints={logged} degenerate={degenerate}.");
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[IkBakeProbe] sample_failed: " +
                    $"frame={Time.frameCount} phase='{phase}' player='{playerDescription}' " +
                    $"reason='{SanitizeLogValue(exception.Message)}'.");
            }
        }

        private static Component[] ReadIkConstraints(Transform root, Type constraintType)
        {
            if (root == null || constraintType == null)
                return Array.Empty<Component>();

            try
            {
                return root.GetComponentsInChildren(constraintType, includeInactive: true) ??
                       Array.Empty<Component>();
            }
            catch
            {
                return Array.Empty<Component>();
            }
        }

        private static bool LogIkBakeConstraint(
            Component component,
            string kind,
            Transform pathRoot,
            string phase,
            string playerDescription,
            bool warnOnDegenerate,
            out bool degenerate)
        {
            degenerate = false;
            if (component == null)
                return false;

            string path = "<unknown>";
            try
            {
                path = GetRelativePath(pathRoot, component.transform);
                object data = ReadMember(component, null, "m_Data");
                Transform root = ReadTransformMember(data, "root", "m_Root");
                Transform mid = ReadTransformMember(data, "mid", "m_Mid");
                Transform tip = ReadTransformMember(data, "tip", "m_Tip");
                Transform target = ReadTransformMember(data, "target", "m_Target");
                float weight = ReadFloat(component, "weight", "m_Weight");
                bool maintainTargetPositionOffset = ReadBool(
                    data, "maintainTargetPositionOffset", "m_MaintainTargetPositionOffset");
                bool maintainTargetRotationOffset = ReadBool(
                    data, "maintainTargetRotationOffset", "m_MaintainTargetRotationOffset");

                // Mirrors ConstraintsUtils.ExtractChain: TwoBoneIK bakes root->mid->tip, ChainIK
                // walks tip up to root. These are the exact segments Build() measures.
                List<Transform> chain = mid != null
                    ? BuildIkBoneList(root, mid, tip)
                    : ExtractIkChain(root, tip);
                var links = new StringBuilder();
                float maxReach = 0f;
                float minLinkDistance = float.PositiveInfinity;
                string minLinkName = "<none>";
                for (int i = 0; i + 1 < chain.Count; i++)
                {
                    Transform from = chain[i];
                    Transform to = chain[i + 1];
                    if (from == null || to == null)
                        continue;

                    float distance = Vector3.Distance(from.position, to.position);
                    maxReach += distance;
                    if (distance < minLinkDistance)
                    {
                        minLinkDistance = distance;
                        minLinkName = from.name + ">" + to.name;
                    }

                    if (links.Length > 0)
                        links.Append('|');
                    links.Append(from.name).Append('>').Append(to.name).Append('=')
                        .Append(FormatFloat(distance));
                }

                float minLossyScale = float.PositiveInfinity;
                string minLossyScaleName = "<none>";
                TrackMinLossyScale(root, ref minLossyScale, ref minLossyScaleName);
                TrackMinLossyScale(mid, ref minLossyScale, ref minLossyScaleName);
                TrackMinLossyScale(tip, ref minLossyScale, ref minLossyScaleName);

                float tipToTargetDistance = tip != null && target != null
                    ? (tip.position - target.position).magnitude
                    : float.NaN;
                float tipToTargetAngle = tip != null && target != null
                    ? Quaternion.Angle(target.rotation, tip.rotation)
                    : float.NaN;
                degenerate =
                    (chain.Count > 1 && minLinkDistance < IkBakeDegenerateThreshold) ||
                    minLossyScale < IkBakeDegenerateThreshold;

                logger?.LogInfo(
                    "[IkBakeProbe] constraint: " +
                    $"frame={Time.frameCount} phase='{phase}' player='{playerDescription}' " +
                    $"kind='{kind}' path='{path}' weight={FormatFloat(weight)} " +
                    $"maintainTargetPositionOffset={maintainTargetPositionOffset} " +
                    $"maintainTargetRotationOffset={maintainTargetRotationOffset} " +
                    $"rootLossyScaleX={FormatLossyScaleX(root)} " +
                    $"midLossyScaleX={FormatLossyScaleX(mid)} " +
                    $"tipLossyScaleX={FormatLossyScaleX(tip)} " +
                    $"chainLength={chain.Count} links='{DescribeIkLinks(links, root, mid, tip)}' " +
                    $"maxReach={FormatFloat(maxReach)} " +
                    $"tipToTargetDistance={FormatFloat(tipToTargetDistance)} " +
                    $"tipToTargetAngleDegrees={FormatFloat(tipToTargetAngle)} " +
                    $"degenerate={degenerate}.");

                if (degenerate && warnOnDegenerate)
                {
                    logger?.LogWarning(
                        "[IkBakeProbe] degenerate_bake_input: " +
                        $"frame={Time.frameCount} phase='{phase}' player='{playerDescription}' " +
                        $"kind='{kind}' path='{path}' " +
                        $"smallestLink='{minLinkName}' " +
                        $"smallestLinkDistance={FormatFloat(minLinkDistance)} " +
                        $"smallestLossyScaleBone='{minLossyScaleName}' " +
                        $"smallestLossyScaleComponent={FormatFloat(minLossyScale)} " +
                        $"threshold={FormatFloat(IkBakeDegenerateThreshold)} " +
                        $"rootLossyScale={FormatLossyScale(root)} " +
                        $"midLossyScale={FormatLossyScale(mid)} " +
                        $"tipLossyScale={FormatLossyScale(tip)} " +
                        "impact='RigBuilder.Build() is about to bake these world-space link " +
                        "lengths, maxReach, and maintain-offset values permanently into the IK " +
                        "job arrays; they are never re-derived, so a collapsed bone here leaves " +
                        "this limb mispositioned in vanilla animation after the session ends'.");
                }

                return true;
            }
            catch (Exception exception)
            {
                logger?.LogInfo(
                    "[IkBakeProbe] constraint_unavailable: " +
                    $"frame={Time.frameCount} phase='{phase}' player='{playerDescription}' " +
                    $"kind='{kind}' path='{path}' " +
                    $"reason='read_failed:{SanitizeLogValue(exception.Message)}'.");
                return false;
            }
        }

        /// <summary>
        /// Distinguishes "no measurable links" causes so an unresolvable chain does not read like a
        /// collapsed one: an empty link list with real bones means the tip is not under the root.
        /// </summary>
        private static string DescribeIkLinks(
            StringBuilder links,
            Transform root,
            Transform mid,
            Transform tip)
        {
            if (links != null && links.Length > 0)
                return links.ToString();
            if (root == null || tip == null)
                return "<bones-missing>";
            return mid != null ? "<none>" : "<tip-not-under-root>";
        }

        private static List<Transform> BuildIkBoneList(Transform root, Transform mid, Transform tip)
        {
            var bones = new List<Transform>(3);
            if (root != null)
                bones.Add(root);
            if (mid != null)
                bones.Add(mid);
            if (tip != null)
                bones.Add(tip);
            return bones;
        }

        private static List<Transform> ExtractIkChain(Transform root, Transform tip)
        {
            var chain = new List<Transform>();
            if (root == null || tip == null)
                return chain;

            Transform current = tip;
            int guard = 0;
            while (current != null && !ReferenceEquals(current, root) &&
                   guard++ < IkBakeChainWalkGuard)
            {
                chain.Add(current);
                current = current.parent;
            }

            if (!ReferenceEquals(current, root))
            {
                chain.Clear();
                return chain;
            }

            chain.Add(root);
            chain.Reverse();
            return chain;
        }

        private static void TrackMinLossyScale(
            Transform transform,
            ref float minComponent,
            ref string minName)
        {
            if (transform == null)
                return;

            Vector3 lossyScale = transform.lossyScale;
            float smallest = Mathf.Min(
                Mathf.Abs(lossyScale.x),
                Mathf.Min(Mathf.Abs(lossyScale.y), Mathf.Abs(lossyScale.z)));
            if (smallest >= minComponent)
                return;

            minComponent = smallest;
            minName = transform.name;
        }

        private static string FormatLossyScaleX(Transform transform)
        {
            return transform != null ? FormatFloat(transform.lossyScale.x) : "<none>";
        }

        private static string FormatLossyScale(Transform transform)
        {
            return transform != null ? FormatVector(transform.lossyScale) : "<none>";
        }

        private static Transform ReadTransformMember(
            object instance,
            string propertyName,
            string fieldName)
        {
            return ReadMember(instance, propertyName, fieldName) as Transform;
        }

        internal static void LogRigAnimatorStates(Animator animator, string checkpoint)
        {
            if (!initialized || !ReadEnabled(enableRestoreRigStateLogger, false))
                return;

            try
            {
                if (animator == null)
                {
                    logger?.LogInfo(
                        "[RestoreSeam.rig] " +
                        $"frame={Time.frameCount} checkpoint='{checkpoint}' animator='<null>'.");
                    return;
                }

                int layerCount = Math.Max(0, animator.layerCount);
                string controllerName =
                    animator.runtimeAnimatorController != null
                        ? animator.runtimeAnimatorController.name
                        : "<null>";
                if (layerCount == 0)
                {
                    logger?.LogInfo(
                        "[RestoreSeam.rig] " +
                        $"frame={Time.frameCount} checkpoint='{checkpoint}' " +
                        $"controller='{controllerName}' layerCount=0.");
                    return;
                }

                for (int i = 0; i < layerCount; i++)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(i);
                    logger?.LogInfo(
                        "[RestoreSeam.rig] " +
                        $"frame={Time.frameCount} checkpoint='{checkpoint}' " +
                        $"controller='{controllerName}' layer={i} " +
                        $"fullPathHash={state.fullPathHash} " +
                        $"normalizedTime={state.normalizedTime.ToString("R", CultureInfo.InvariantCulture)}.");
                }
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RestoreSeam.rig] " +
                    $"frame={Time.frameCount} checkpoint='{checkpoint}' error='{exception.Message}'.");
            }
        }

        internal static void LateUpdate()
        {
            if (!initialized)
                return;

            bool frameLoggerEnabled = ReadEnabled(enableRestoreSeamFrameLogger, false);
            bool rigDiffEnabled = ReadEnabled(enablePristineRigDiffProbe, false);
            bool pristineRigCaptureEnabled = PristineRigCaptureEnabled();
            bool remoteRigDiffProbeEnabled =
                ReadEnabled(enableRemoteRigDiffProbe, false);
            if (!frameLoggerEnabled && !pristineRigCaptureEnabled &&
                !remoteRigDiffProbeEnabled && PendingRemoteRigProbes.Count == 0 &&
                pendingIkBakeProbeLateUpdates <= 0)
                return;

            try
            {
                // Drained first: a throwing remote probe must not strand the ik probe's countdown,
                // which would leave its early-return gate defeated for every later frame.
                TickIkBakeProbe();
                TickRemoteRigDiffProbes(remoteRigDiffProbeEnabled);
                if (!frameLoggerEnabled && !pristineRigCaptureEnabled)
                    return;

                PlayerControllerB player = ResolveLocalPlayer();
                if (player == null)
                {
                    if (observedPlayer != null)
                        ObservePlayer(null);
                    return;
                }

                ObservePlayer(player);

                if (frameLoggerEnabled)
                {
                    RestoreFrameSample sample = RestoreHistory[restoreHistoryNext];
                    CaptureRestoreFrame(player, sample);
                    restoreHistoryNext = (restoreHistoryNext + 1) % RestoreHistoryFrames;
                    if (restoreHistoryCount < RestoreHistoryFrames)
                        restoreHistoryCount++;

                    if (restoreFutureFramesRemaining > 0)
                    {
                        restoreFutureFrameIndex++;
                        LogRestoreFrame(
                            sample,
                            "after+" + restoreFutureFrameIndex.ToString(CultureInfo.InvariantCulture));
                        restoreFutureFramesRemaining--;
                    }
                }

                if (pristineRigCaptureEnabled && !customAnimationHasRun && pristineRig == null)
                    TryCapturePristineRig(player);

                if (!rigDiffEnabled)
                    return;


                if (pendingRigDiffLateUpdates > 0)
                {
                    pendingRigDiffLateUpdates--;
                    if (pendingRigDiffLateUpdates == 0)
                    {
                        DumpRigDiff(
                            player,
                            "auto_restore_plus_2_lateupdates");
                        pendingRigDiffLateUpdates = -1;
                    }
                }
            }
            catch (Exception exception)
            {
                if (samplerFailureLogged)
                    return;

                samplerFailureLogged = true;
                logger?.LogWarning(
                    "[RestoreSeam] diagnostics_late_update_failed: " + exception.Message);
            }
        }

        private static void ObservePlayer(PlayerControllerB player)
        {
            if (ReferenceEquals(observedPlayer, player))
                return;

            observedPlayer = player;
            ClearRenderSessionTransforms();
            pristineRig = null;
            customAnimationHasRun = false;
            pendingRigDiffLateUpdates = -1;
            // Pooled player objects survive a menu return; clearing here re-baselines the next
            // round's Awake instead of suppressing it as already-logged.
            IkBakeProbeAwakeLoggedPlayers.Clear();
            ikBakeProbePlayer = null;
            pendingIkBakeProbeLateUpdates = -1;
            ResetRestoreHistory();
        }

        private static void ResetRestoreHistory()
        {
            restoreHistoryNext = 0;
            restoreHistoryCount = 0;
            restoreFutureFramesRemaining = 0;
            restoreFutureFrameIndex = 0;
            activeStopFrame = 0;
            activeStopInvocation = "consumer_call";
            for (int i = 0; i < RestoreHistory.Length; i++)
                RestoreHistory[i].Valid = false;
        }

        private static void CacheRenderSessionTransforms(PlayerControllerB player)
        {
            renderSessionPlayer = player;
            renderSessionGameplayCamera = null;
            renderSessionCamera = null;
            renderSessionRightHand = null;
            renderSessionLeftHand = null;
            renderSessionArmsMetarig = null;
            renderSessionLocalArms = null;
            renderSessionLocalVisor = null;
            renderSessionLocalVisorTargetPoint = null;
            renderSessionVisorCamera = null;

            try
            {
                Camera camera = player != null ? player.gameplayCamera : null;
                renderSessionGameplayCamera = camera;
                renderSessionCamera = camera != null ? camera.transform : null;
                renderSessionArmsMetarig =
                    player != null ? player.playerModelArmsMetarig : null;
                renderSessionLocalArms =
                    player != null ? player.localArmsTransform : null;
                renderSessionLocalVisor = player != null ? player.localVisor : null;
                renderSessionLocalVisorTargetPoint =
                    player != null ? player.localVisorTargetPoint : null;
                renderSessionVisorCamera = player != null ? player.visorCamera : null;
                renderSessionRightHand =
                    FindChildRecursive(renderSessionArmsMetarig, "hand.R");
                renderSessionLeftHand =
                    FindChildRecursive(renderSessionArmsMetarig, "hand.L");
            }
            catch
            {
                renderSessionGameplayCamera = null;
                renderSessionCamera = null;
                renderSessionRightHand = null;
                renderSessionLeftHand = null;
                renderSessionArmsMetarig = null;
                renderSessionLocalArms = null;
                renderSessionLocalVisor = null;
                renderSessionLocalVisorTargetPoint = null;
                renderSessionVisorCamera = null;
            }
        }

        private static void ClearRenderSessionTransforms()
        {
            renderSessionPlayer = null;
            renderSessionGameplayCamera = null;
            renderSessionCamera = null;
            renderSessionRightHand = null;
            renderSessionLeftHand = null;
            renderSessionArmsMetarig = null;
            renderSessionLocalArms = null;
            renderSessionLocalVisor = null;
            renderSessionLocalVisorTargetPoint = null;
            renderSessionVisorCamera = null;
            LatestRenderFrame.Reset();
            PendingStartBeforeRenderFrame.Reset();
            StartRenderSeam.Reset();
            StopRenderSeam.Reset();
        }

        private static void SubscribeRenderSampler()
        {
            if (renderSamplerSubscribed || !initialized ||
                !ReadEnabled(enableRestoreSeamFrameLogger, false))
            {
                return;
            }

            try
            {
                Application.onBeforeRender += LogRestoreRenderFrame;
                renderSamplerSubscribed = true;
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RestoreSeam.render] subscribe_failed: " + exception.Message);
                UnsubscribeRenderSampler();
            }
        }

        private static void LogRestoreRenderFrame()
        {
            if (!renderSamplerSubscribed)
                return;

            try
            {
                if (!initialized || !ReadEnabled(enableRestoreSeamFrameLogger, false))
                {
                    UnsubscribeRenderSampler();
                    return;
                }

                PlayerControllerB player = ResolveLocalPlayer();
                if (player == null)
                    return;

                if (!ReferenceEquals(renderSessionPlayer, player))
                    CacheRenderSessionTransforms(player);

                CaptureRenderFrame(player, LatestRenderFrame);
                LogActiveRenderSeam(LatestRenderFrame, StartRenderSeam);
                LogActiveRenderSeam(LatestRenderFrame, StopRenderSeam);
            }
            catch (Exception exception)
            {
                if (!samplerFailureLogged)
                {
                    samplerFailureLogged = true;
                    logger?.LogWarning(
                        "[RestoreSeam.render] sample_failed: " + exception.Message);
                }
            }
        }

        private static void LogActiveRenderSeam(
            RenderSeamFrameSample sample,
            RenderSeamWindow seam)
        {
            if (!seam.Active || sample == null || !sample.Valid)
                return;

            if (sample.Frame > seam.EndFrame)
            {
                seam.Reset();
                return;
            }

            if (sample.Frame < seam.SeamFrame || sample.Frame == seam.LastLoggedFrame)
                return;

            seam.LastLoggedFrame = sample.Frame;
            int offset = sample.Frame - seam.SeamFrame;
            LogRenderFrame(
                sample,
                seam,
                offset == 0
                    ? "seam+0"
                    : "after+" + offset.ToString(CultureInfo.InvariantCulture),
                captureAnimatedProp: true);

            if (sample.Frame >= seam.EndFrame)
                seam.Reset();
        }

        private static void UnsubscribeRenderSampler()
        {
            if (renderSamplerSubscribed)
            {
                try { Application.onBeforeRender -= LogRestoreRenderFrame; } catch { }
            }

            renderSamplerSubscribed = false;
            StartRenderSeam.Reset();
            StopRenderSeam.Reset();
        }

        private static void CaptureRenderFrame(
            PlayerControllerB player,
            RenderSeamFrameSample sample)
        {
            if (sample == null)
                return;

            sample.Reset();
            sample.Player = player;

            try { sample.Frame = Time.frameCount; } catch { }
            try { sample.DeltaTime = Time.deltaTime; } catch { }
            try { sample.UnscaledDeltaTime = Time.unscaledDeltaTime; } catch { }

            sample.GameplayCamera = CaptureRenderPose(renderSessionCamera);
            sample.RightHand = CaptureRenderPose(renderSessionRightHand);
            sample.LeftHand = CaptureRenderPose(renderSessionLeftHand);
            sample.ArmsMetarig = CaptureRenderPose(renderSessionArmsMetarig);
            sample.LocalArms = CaptureRenderPose(renderSessionLocalArms);
            sample.LocalVisor = CaptureRenderPose(renderSessionLocalVisor);
            sample.LocalVisorTargetPoint =
                CaptureRenderPose(renderSessionLocalVisorTargetPoint);
            sample.LocalVisorVisibility = CaptureRenderVisibility(
                renderSessionLocalVisor != null
                    ? renderSessionLocalVisor.gameObject
                    : null);

            try
            {
                if (renderSessionVisorCamera != null)
                {
                    sample.VisorCameraPresent = true;
                    sample.VisorCameraEnabled = renderSessionVisorCamera.enabled;
                }
            }
            catch
            {
                sample.VisorCameraPresent = false;
            }

            try
            {
                if (renderSessionGameplayCamera != null)
                {
                    sample.CameraParametersPresent = true;
                    sample.CameraFieldOfView = renderSessionGameplayCamera.fieldOfView;
                    sample.CameraNearClipPlane = renderSessionGameplayCamera.nearClipPlane;
                }
            }
            catch
            {
                sample.CameraParametersPresent = false;
            }

            GameObject heldItem = null;
            try
            {
                GrabbableObject heldObject =
                    player != null ? player.currentlyHeldObjectServer : null;
                heldItem = heldObject != null ? heldObject.gameObject : null;
            }
            catch { }
            sample.HeldItem = CaptureRenderVisibility(heldItem);
            sample.Valid = true;
        }

        private static RenderPoseSample CaptureRenderPose(Transform transform)
        {
            var sample = new RenderPoseSample();
            try
            {
                if (transform == null)
                    return sample;

                sample.Present = true;
                sample.WorldPosition = transform.position;
                sample.WorldEulerAngles = transform.rotation.eulerAngles;
            }
            catch
            {
                sample.Present = false;
            }

            return sample;
        }

        private static RenderVisibilitySample CaptureRenderVisibility(GameObject root)
        {
            var sample = new RenderVisibilitySample
            {
                Name = "<null>"
            };

            try
            {
                if (root == null)
                    return sample;

                sample.Present = true;
                sample.Name = root.name ?? "<unnamed>";
                sample.ActiveInHierarchy = root.activeInHierarchy;
                RenderVisibilityBuffer.Clear();
                root.GetComponentsInChildren(true, RenderVisibilityBuffer);
                sample.RendererCount = RenderVisibilityBuffer.Count;
                for (int i = 0; i < RenderVisibilityBuffer.Count; i++)
                {
                    Renderer renderer = RenderVisibilityBuffer[i];
                    if (renderer == null || !renderer.enabled)
                        continue;

                    sample.EnabledRendererCount++;
                    sample.AnyRendererEnabled = true;
                }
            }
            catch
            {
                sample.ReadFailed = true;
            }

            return sample;
        }

        private static void LogRenderFrame(
            RenderSeamFrameSample sample,
            RenderSeamWindow seam,
            string samplePhase,
            bool captureAnimatedProp)
        {
            if (sample == null || !sample.Valid || seam == null)
                return;

            try
            {
                RenderVisibilitySample animatedProp =
                    CaptureRenderVisibility(
                        captureAnimatedProp ? seam.AnimatedProp : null);
                logger?.LogInfo(
                    "[RestoreSeam.render] " +
                    $"phase={seam.Phase} frame={sample.Frame} seamFrame={seam.SeamFrame} " +
                    $"sample={samplePhase} " +
                    $"deltaTime={FormatRenderFloat(sample.DeltaTime)} " +
                    $"unscaledDeltaTime={FormatRenderFloat(sample.UnscaledDeltaTime)} " +
                    $"cameraWorldPos={FormatRenderPosition(sample.GameplayCamera)} " +
                    $"cameraWorldEuler={FormatRenderEuler(sample.GameplayCamera)} " +
                    $"cameraFov={FormatOptionalRenderFloat(sample.CameraParametersPresent, sample.CameraFieldOfView)} " +
                    $"cameraNearClip={FormatOptionalRenderFloat(sample.CameraParametersPresent, sample.CameraNearClipPlane)} " +
                    $"hand.RWorldPos={FormatRenderPosition(sample.RightHand)} " +
                    $"hand.RWorldEuler={FormatRenderEuler(sample.RightHand)} " +
                    $"hand.LWorldPos={FormatRenderPosition(sample.LeftHand)} " +
                    $"hand.LWorldEuler={FormatRenderEuler(sample.LeftHand)} " +
                    $"playerModelArmsMetarigWorldPos={FormatRenderPosition(sample.ArmsMetarig)} " +
                    $"playerModelArmsMetarigWorldEuler={FormatRenderEuler(sample.ArmsMetarig)} " +
                    $"localArmsTransformWorldPos={FormatRenderPosition(sample.LocalArms)} " +
                    $"localArmsTransformWorldEuler={FormatRenderEuler(sample.LocalArms)} " +
                    $"animatedProp={FormatRenderVisibility(animatedProp)} " +
                    $"heldItem={FormatRenderVisibility(sample.HeldItem)} " +
                    $"localVisorWorldPos={FormatRenderPosition(sample.LocalVisor)} " +
                    $"localVisorWorldEuler={FormatRenderEuler(sample.LocalVisor)} " +
                    $"localVisorTargetPointWorldPos={FormatRenderPosition(sample.LocalVisorTargetPoint)} " +
                    $"localVisorTargetPointWorldEuler={FormatRenderEuler(sample.LocalVisorTargetPoint)} " +
                    $"localVisorRenderers={FormatRenderVisibility(sample.LocalVisorVisibility)} " +
                    $"visorCameraEnabled={FormatOptionalRenderBool(sample.VisorCameraPresent, sample.VisorCameraEnabled)}.");
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RestoreSeam.render] log_failed: " +
                    $"phase={seam.Phase} frame={sample.Frame} " +
                    $"seamFrame={seam.SeamFrame} error='{exception.Message}'.");
            }
        }

        private static string FormatRenderPosition(RenderPoseSample sample)
        {
            return sample.Present ? FormatVector(sample.WorldPosition) : "<unavailable>";
        }

        private static string FormatRenderEuler(RenderPoseSample sample)
        {
            return sample.Present ? FormatVector(sample.WorldEulerAngles) : "<unavailable>";
        }

        private static string FormatRenderFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string FormatOptionalRenderFloat(bool present, float value)
        {
            return present ? FormatRenderFloat(value) : "<unavailable>";
        }

        private static string FormatOptionalRenderBool(bool present, bool value)
        {
            return present ? FormatRenderBool(value) : "<unavailable>";
        }

        private static string FormatRenderVisibility(RenderVisibilitySample sample)
        {
            return string.Concat(
                "[present=", FormatRenderBool(sample.Present),
                " name='", SanitizeRenderLogValue(sample.Name),
                "' activeInHierarchy=", FormatRenderBool(sample.ActiveInHierarchy),
                " rendererCount=", sample.RendererCount.ToString(CultureInfo.InvariantCulture),
                " enabledRendererCount=", sample.EnabledRendererCount.ToString(CultureInfo.InvariantCulture),
                " anyRendererEnabled=", FormatRenderBool(sample.AnyRendererEnabled),
                " readFailed=", FormatRenderBool(sample.ReadFailed),
                "]");
        }

        private static string FormatRenderBool(bool value)
        {
            return value ? "true" : "false";
        }

        private static string SanitizeRenderLogValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "<empty>"
                : value.Replace('\r', ' ').Replace('\n', ' ').Replace('\'', '"');
        }

        private static void CaptureRestoreFrame(
            PlayerControllerB player,
            RestoreFrameSample sample)
        {
            sample.Valid = false;
            sample.Frame = Time.frameCount;
            sample.GameplayCameraWorldPosition = Vector3.zero;
            sample.CameraContainerLocalPosition = Vector3.zero;
            sample.CameraContainerWorldPosition = Vector3.zero;
            sample.ArmsMetarigWorldPosition = Vector3.zero;
            sample.ControllerName = "<null>";
            sample.ActualLayerCount = 0;
            sample.CapturedLayerCount = 0;

            Camera gameplayCamera = player.gameplayCamera;
            Transform cameraContainer = player.cameraContainerTransform;
            Transform armsMetarig = player.playerModelArmsMetarig;
            Animator animator = player.playerBodyAnimator;

            if (gameplayCamera != null)
                sample.GameplayCameraWorldPosition = gameplayCamera.transform.position;
            if (cameraContainer != null)
            {
                sample.CameraContainerLocalPosition = cameraContainer.localPosition;
                sample.CameraContainerWorldPosition = cameraContainer.position;
            }
            if (armsMetarig != null)
                sample.ArmsMetarigWorldPosition = armsMetarig.position;

            if (animator != null)
            {
                RuntimeAnimatorController controller = animator.runtimeAnimatorController;
                sample.ControllerName = controller != null ? controller.name : "<null>";
                sample.ActualLayerCount = Math.Max(0, animator.layerCount);
                sample.CapturedLayerCount = Math.Min(
                    sample.ActualLayerCount,
                    sample.LayerStateHashes.Length);
                for (int i = 0; i < sample.CapturedLayerCount; i++)
                {
                    AnimatorStateInfo state = animator.GetCurrentAnimatorStateInfo(i);
                    sample.LayerStateHashes[i] = state.fullPathHash;
                    sample.LayerNormalizedTimes[i] = state.normalizedTime;
                }
            }

            sample.Valid = true;
        }

        private static void LogRestoreFrame(RestoreFrameSample sample, string phase)
        {
            if (sample == null || !sample.Valid)
                return;

            var states = new StringBuilder(128);
            states.Append('[');
            for (int i = 0; i < sample.CapturedLayerCount; i++)
            {
                if (i > 0)
                    states.Append(',');
                states.Append(i);
                states.Append(':');
                states.Append(sample.LayerStateHashes[i]);
                states.Append('@');
                states.Append(
                    sample.LayerNormalizedTimes[i].ToString(
                        "R",
                        CultureInfo.InvariantCulture));
            }
            states.Append(']');

            logger?.LogInfo(
                "[RestoreSeam] " +
                $"frame={sample.Frame} phase={phase} stopFrame={activeStopFrame} " +
                $"stopInvocation='{activeStopInvocation}' " +
                $"gameplayCameraWorld={FormatVector(sample.GameplayCameraWorldPosition)} " +
                $"cameraContainerLocal={FormatVector(sample.CameraContainerLocalPosition)} " +
                $"cameraContainerWorld={FormatVector(sample.CameraContainerWorldPosition)} " +
                $"armsMetarigWorld={FormatVector(sample.ArmsMetarigWorldPosition)} " +
                $"controller='{sample.ControllerName}' " +
                $"layerCount={sample.ActualLayerCount} capturedLayers={sample.CapturedLayerCount} " +
                $"states={states}.");
        }

        internal static void CapturePristineThirdPersonRigPoseAtPlayerAwake(
            PlayerControllerB player)
        {
            if (!initialized)
            {
                if (!playerAwakeCaptureUninitializedLogged)
                {
                    playerAwakeCaptureUninitializedLogged = true;
                    StaticLogger?.LogInfo(
                        "[RestoreSeam.tprig] pristine_capture_skipped: " +
                        $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                        $"initialized=False source='{ThirdPersonRigAuthoredDefaultSource}' " +
                        "reason='restore_diagnostics_not_initialized' action='skip'.");
                }
                return;
            }

            TryCapturePristineThirdPersonRigPose(player);
        }

        /// <summary>
        /// Captures the authored-default local positions of the camera chain (CameraContainer,
        /// gameplay camera, first-person arms metarig root, local-arms transform) before
        /// PlayerControllerB.Awake runs. Authored live-body clips can write positions on these
        /// transforms; vanilla derives them from each other every frame instead of rewriting
        /// absolute values, so any residue left at Stop persists and accumulates across sessions
        /// (observed as the viewpoint sinking a few centimeters per weapon session). This baseline
        /// is the restore authority for the Stop snap and the session-start drift heal.
        /// </summary>
        internal static void CapturePristineCameraChainPoseAtPlayerAwake(
            PlayerControllerB player)
        {
            if (!initialized || player == null || PristineCameraChainPoses.ContainsKey(player))
                return;

            try
            {
                CameraChainPoseSnapshot candidate = CameraChainPoseSnapshot.Capture(player);
                if (candidate == null || candidate.Count == 0)
                {
                    StaticLogger?.LogInfo(
                        "[RestoreSeam.camerachain] pristine_capture_skipped: " +
                        $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                        $"source='{CameraChainAuthoredDefaultSource}' " +
                        "reason='camera_chain_unavailable' action='use_session_entry_fallback'.");
                    return;
                }

                PristineCameraChainPoses[player] = candidate;
                StaticLogger?.LogInfo(
                    "[RestoreSeam.camerachain] pristine_captured: " +
                    $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                    $"transforms={candidate.Count} missing='{candidate.MissingTargets}' " +
                    $"{candidate.DescribePositions()} " +
                    $"source='{CameraChainAuthoredDefaultSource}'.");
            }
            catch (Exception exception)
            {
                StaticLogger?.LogInfo(
                    "[RestoreSeam.camerachain] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                    $"source='{CameraChainAuthoredDefaultSource}' " +
                    $"reason='capture_failed:{SanitizeLogValue(exception.Message)}' " +
                    "action='use_session_entry_fallback'.");
            }
        }

        /// <summary>
        /// Restores the pristine camera-chain local positions for the player. Positions only:
        /// camera and container rotations are owned by the dedicated rotation seam restore and
        /// must not be touched here. Returns false when no pristine baseline exists.
        /// </summary>
        internal static bool TryRestorePristineCameraChainPositions(
            PlayerControllerB player,
            out int restored,
            out string reason,
            out string source)
        {
            restored = 0;
            reason = string.Empty;
            source = CameraChainAuthoredDefaultSource;
            if (player == null)
            {
                reason = "player_missing";
                return false;
            }

            if (!PristineCameraChainPoses.TryGetValue(
                    player,
                    out CameraChainPoseSnapshot pristine) || pristine == null)
            {
                reason = "pristine_baseline_unavailable";
                return false;
            }

            source = pristine.Source;
            restored = pristine.RestorePositions();
            if (restored == 0)
            {
                reason = "no_transforms_restored";
                return false;
            }

            return true;
        }

        /// <summary>
        /// One-time upgrade of a player's camera-chain rest baseline from the authored prefab
        /// pose to the current runtime-settled pose. The caller is responsible for verifying the
        /// chain is actually at rest (camera near vanilla rest expectation, player idle,
        /// standing, not in a special animation) before calling. Returns false without touching
        /// the baseline when it is missing or already refined.
        /// </summary>
        internal static bool TryRefineCameraChainRestBaseline(
            PlayerControllerB player,
            out string reason)
        {
            reason = string.Empty;
            if (player == null)
            {
                reason = "player_missing";
                return false;
            }

            if (!PristineCameraChainPoses.TryGetValue(
                    player,
                    out CameraChainPoseSnapshot pristine) || pristine == null)
            {
                reason = "pristine_baseline_unavailable";
                return false;
            }

            if (string.Equals(
                    pristine.Source,
                    CameraChainRuntimeSettledSource,
                    StringComparison.Ordinal))
            {
                reason = "already_refined";
                return false;
            }

            string beforePositions = pristine.DescribePositions();
            if (!pristine.RefinePositions(CameraChainRuntimeSettledSource))
            {
                reason = "no_transforms_refined";
                return false;
            }

            StaticLogger?.LogInfo(
                "[RestoreSeam.camerachain] rest_baseline_refined: " +
                $"frame={Time.frameCount} player='{DescribePlayer(player)}' " +
                $"before[{beforePositions}] after[{pristine.DescribePositions()}] " +
                $"source='{CameraChainRuntimeSettledSource}'.");
            return true;
        }

        /// <summary>
        /// Local-position-only snapshot of the transforms whose positions define the local
        /// first-person viewpoint height. Rotation and scale are deliberately excluded.
        /// </summary>
        internal const string CameraChainAuthoredDefaultSource =
            "player_awake_prefix_authored_default";
        internal const string CameraChainRuntimeSettledSource =
            "session_entry_runtime_settled";
        internal const string ThirdPersonRigAuthoredDefaultSource =
            "player_awake_prefix_authored_default";
        internal const string ThirdPersonRigRuntimeSettledSource =
            "session_entry_runtime_settled";

        internal sealed class CameraChainPoseSnapshot
        {
            private readonly Transform[] transforms;
            private readonly Vector3[] localPositions;
            private readonly string[] names;

            internal int Count => transforms.Length;
            internal string MissingTargets { get; }
            internal string Source { get; private set; } = CameraChainAuthoredDefaultSource;

            private CameraChainPoseSnapshot(
                Transform[] transforms,
                Vector3[] localPositions,
                string[] names,
                string missingTargets)
            {
                this.transforms = transforms;
                this.localPositions = localPositions;
                this.names = names;
                MissingTargets = missingTargets;
            }

            /// <summary>
            /// Replaces the stored rest positions with the transforms' current local positions.
            /// The authored prefab pose sits a few millimeters from where vanilla's runtime glue
            /// actually settles the chain; re-reading at a verified-clean, idle session entry
            /// makes later snaps target vanilla's true rest instead of breathing against it.
            /// </summary>
            internal bool RefinePositions(string newSource)
            {
                bool refined = false;
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform target = transforms[i];
                    if (target == null)
                        continue;

                    localPositions[i] = target.localPosition;
                    refined = true;
                }

                if (refined)
                    Source = newSource;
                return refined;
            }

            internal static CameraChainPoseSnapshot Capture(PlayerControllerB player)
            {
                var capturedTransforms = new List<Transform>(4);
                var capturedPositions = new List<Vector3>(4);
                var capturedNames = new List<string>(4);
                var missing = new List<string>(4);

                CaptureTarget(
                    "cameraContainer",
                    SafeGet(() => player.cameraContainerTransform),
                    capturedTransforms, capturedPositions, capturedNames, missing);
                CaptureTarget(
                    "gameplayCamera",
                    SafeGet(() => player.gameplayCamera != null
                        ? player.gameplayCamera.transform
                        : null),
                    capturedTransforms, capturedPositions, capturedNames, missing);
                CaptureTarget(
                    "playerModelArmsMetarig",
                    SafeGet(() => player.playerModelArmsMetarig),
                    capturedTransforms, capturedPositions, capturedNames, missing);
                CaptureTarget(
                    "localArmsTransform",
                    SafeGet(() => player.localArmsTransform),
                    capturedTransforms, capturedPositions, capturedNames, missing);

                return new CameraChainPoseSnapshot(
                    capturedTransforms.ToArray(),
                    capturedPositions.ToArray(),
                    capturedNames.ToArray(),
                    string.Join(",", missing.ToArray()));
            }

            private static Transform SafeGet(Func<Transform> getter)
            {
                try { return getter(); }
                catch { return null; }
            }

            private static void CaptureTarget(
                string name,
                Transform target,
                List<Transform> capturedTransforms,
                List<Vector3> capturedPositions,
                List<string> capturedNames,
                List<string> missing)
            {
                if (target == null)
                {
                    missing.Add(name);
                    return;
                }

                capturedTransforms.Add(target);
                capturedPositions.Add(target.localPosition);
                capturedNames.Add(name);
            }

            internal int RestorePositions()
            {
                int restored = 0;
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform target = transforms[i];
                    if (target == null)
                        continue;

                    target.localPosition = localPositions[i];
                    restored++;
                }

                return restored;
            }

            internal string DescribePositions()
            {
                var parts = new List<string>(names.Length);
                for (int i = 0; i < names.Length; i++)
                    parts.Add($"{names[i]}Local={FormatVector(localPositions[i])}");
                return string.Join(" ", parts.ToArray());
            }
        }

        private static void LogPristineThirdPersonRigPoseAvailability(PlayerControllerB player)
        {
            bool enabled =
                ReadEnabled(restorePristineThirdPersonRigControlPose, true);
            bool localPlayer = IsLocalPlayer(player);
            string playerDescription = DescribePlayer(player);
            if (!enabled)
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=False " +
                    "reason='kill_switch_disabled' action='use_equip_fallback'.");
                return;
            }

            if (player == null)
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='<null>' localPlayer=False enabled=True " +
                    "reason='player_missing' action='use_equip_fallback'.");
                return;
            }

            if (PristineThirdPersonRigPoses.ContainsKey(player))
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=True " +
                    "reason='already_captured' baseline_contaminated=False " +
                    $"source='{ThirdPersonRigAuthoredDefaultSource}' action='keep_pristine'.");
                return;
            }

            logger?.LogInfo(
                "[RestoreSeam.tprig] pristine_capture_skipped: " +
                $"frame={Time.frameCount} player='{playerDescription}' " +
                $"localPlayer={localPlayer} enabled=True baseline_contaminated=True " +
                "reason='authored_default_capture_unavailable' action='use_equip_fallback'.");
        }

        private static void TryCapturePristineThirdPersonRigPose(PlayerControllerB player)
        {
            bool enabled =
                ReadEnabled(restorePristineThirdPersonRigControlPose, true);
            bool localPlayer = IsLocalPlayer(player);
            string playerDescription = DescribePlayer(player);
            const string source = ThirdPersonRigAuthoredDefaultSource;
            if (!enabled)
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=False source='{source}' " +
                    "reason='kill_switch_disabled' action='use_equip_fallback'.");
                return;
            }

            if (player == null)
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='<null>' localPlayer=False enabled=True " +
                    $"source='{source}' reason='player_missing' action='skip'.");
                return;
            }

            if (PristineThirdPersonRigPoses.ContainsKey(player))
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=True source='{source}' " +
                    "reason='already_captured' baseline_contaminated=False action='keep_pristine'.");
                return;
            }

            try
            {
                ThirdPersonRigPoseSnapshot candidate =
                    ThirdPersonRigPoseSnapshot.Capture(player);
                if (candidate == null || candidate.TotalCount == 0)
                {
                    logger?.LogInfo(
                        "[RestoreSeam.tprig] pristine_capture_skipped: " +
                        $"frame={Time.frameCount} player='{playerDescription}' " +
                        $"localPlayer={localPlayer} enabled=True source='{source}' " +
                        "reason='third_person_rig_unavailable' baseline_contaminated=True " +
                        "action='use_equip_fallback'.");
                    return;
                }

                ThirdPersonRigPlausibility plausibility =
                    candidate.EvaluateVanillaRestPlausibility();
                string sanityAction = plausibility.Plausible
                    ? "accept_authored_default"
                    : "accept_authored_default_pending_runtime_recapture";
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_sanity: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=True source='{source}' " +
                    $"plausibleAgainstVanillaRest={plausibility.Plausible} " +
                    $"complete={candidate.IsComplete} missing='{candidate.MissingTargets}' " +
                    $"positionOutliers={plausibility.PositionOutliers} " +
                    $"rotationOutliers={plausibility.RotationOutliers} " +
                    $"scaleOutliers={plausibility.ScaleOutliers} " +
                    $"maxPositionDelta={FormatFloat(plausibility.MaxPositionDelta)} " +
                    $"maxRotationDeltaDegrees={FormatFloat(plausibility.MaxRotationDeltaDegrees)} " +
                    $"maxScaleDelta={FormatFloat(plausibility.MaxScaleDelta)} " +
                    $"positionThreshold={FormatFloat(ThirdPersonRigRestPositionThreshold)} " +
                    $"rotationThresholdDegrees={FormatFloat(ThirdPersonRigRestRotationThresholdDegrees)} " +
                    $"scaleThreshold={FormatFloat(ThirdPersonRigRestScaleThreshold)} " +
                    $"reason='{plausibility.Reason}' action='{sanityAction}'.");

                // This runs before PlayerControllerB.Awake and before movement clips can evaluate.
                // Vanilla-rest comparison is useful telemetry, but movement-aware authored defaults
                // are the baseline authority and are never rejected for differing from vanilla.
                // An implausible capture is still stored, but flagged so the first verified-clean
                // idle session entry can replace it with a runtime-settled recapture.
                candidate.SetCaptureMetadata(plausibility.Plausible, source);
                PristineThirdPersonRigPoses[player] = candidate;
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_captured: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=True baseline_contaminated=False " +
                    $"complete={candidate.IsComplete} missing='{candidate.MissingTargets}' " +
                    $"fullPoseTransforms={candidate.FullPoseCount} " +
                    $"rotationOnlyTransforms={candidate.RotationOnlyCount} " +
                    $"plausibleAgainstVanillaRest={plausibility.Plausible} " +
                    $"source='{source}'.");
            }
            catch (Exception exception)
            {
                logger?.LogInfo(
                    "[RestoreSeam.tprig] pristine_capture_skipped: " +
                    $"frame={Time.frameCount} player='{playerDescription}' " +
                    $"localPlayer={localPlayer} enabled=True source='{source}' " +
                    $"reason='capture_failed:{SanitizeLogValue(exception.Message)}' " +
                    "action='use_equip_fallback'.");
            }
        }

        private static void TryCapturePristineRig(PlayerControllerB player)
        {
            if (pristineRig != null || customAnimationHasRun || !IsLocalPlayer(player))
                return;

            try
            {
                Transform metarig = player.playerModelArmsMetarig;
                Transform rigArms = FindChildRecursive(metarig, "RigArms");
                if (metarig == null || rigArms == null)
                    return;

                pristineRig = PristineRigSnapshot.Capture(
                    player,
                    metarig,
                    rigArms,
                    ResolveTwoBoneIkConstraintType(),
                    ResolveChainIkConstraintType());
                logger?.LogInfo(
                    "[RigDiff] pristine_captured: " +
                    $"frame={Time.frameCount} transforms={pristineRig.Transforms.Length} " +
                    $"twoBoneIkConstraints={pristineRig.TwoBoneConstraints.Length} " +
                    $"chainIkConstraints={pristineRig.ChainConstraints.Length} " +
                    $"rigs={pristineRig.Rigs.Length} rigLayers={pristineRig.RigLayers.Length} " +
                    $"metarig='{metarig.name}' rigArms='{rigArms.name}'.");
            }
            catch (Exception exception)
            {
                logger?.LogWarning(
                    "[RigDiff] pristine_capture_failed: " + exception.Message);
            }
        }

        private static Type ResolveTwoBoneIkConstraintType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(
                        TwoBoneIkConstraintTypeName,
                        throwOnError: false,
                        ignoreCase: false);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            if (!twoBoneIkTypeUnavailableLogged)
            {
                twoBoneIkTypeUnavailableLogged = true;
                logger?.LogWarning(
                    "[RigDiff] two_bone_ik_type_unavailable: " +
                    $"type='{TwoBoneIkConstraintTypeName}' constraint-field capture disabled.");
            }

            return null;
        }

        private static Type ResolveChainIkConstraintType()
        {
            Assembly[] assemblies = AppDomain.CurrentDomain.GetAssemblies();
            for (int i = 0; i < assemblies.Length; i++)
            {
                try
                {
                    Type type = assemblies[i].GetType(
                        ChainIkConstraintTypeName,
                        throwOnError: false,
                        ignoreCase: false);
                    if (type != null)
                        return type;
                }
                catch { }
            }

            if (!chainIkTypeUnavailableLogged)
            {
                chainIkTypeUnavailableLogged = true;
                logger?.LogWarning(
                    "[RigDiff] chain_ik_type_unavailable: " +
                    $"type='{ChainIkConstraintTypeName}' constraint-field capture disabled.");
            }

            return null;
        }

        private static void DumpRigDiff(PlayerControllerB player, string reason)
        {
            if (pristineRig == null || player == null ||
                !ReferenceEquals(pristineRig.Player, player))
            {
                logger?.LogInfo(
                    "[RigDiff] dump_unavailable: " +
                    $"frame={Time.frameCount} reason='{reason}' pristineCaptured=False.");
                return;
            }

            int transformChanges = 0;
            int constraintChanges = 0;
            int rigChanges = 0;
            int rigLayerChanges = 0;
            logger?.LogInfo(
                "[RigDiff] dump_begin: " +
                $"frame={Time.frameCount} reason='{reason}' " +
                $"positionThreshold={RigPositionDeltaThreshold.ToString("R", CultureInfo.InvariantCulture)} " +
                $"rotationThresholdDegrees={RigRotationDeltaThresholdDegrees.ToString("R", CultureInfo.InvariantCulture)}.");

            for (int i = 0; i < pristineRig.Transforms.Length; i++)
            {
                TransformBaseline baseline = pristineRig.Transforms[i];
                Transform transform = baseline.Transform;
                if (transform == null)
                {
                    transformChanges++;
                    logger?.LogInfo(
                        "[RigDiff] transform_missing: " +
                        $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}'.");
                    continue;
                }

                Vector3 positionDelta = transform.localPosition - baseline.LocalPosition;
                float positionMagnitude = positionDelta.magnitude;
                float rotationDelta =
                    Quaternion.Angle(baseline.LocalRotation, transform.localRotation);
                if (positionMagnitude <= RigPositionDeltaThreshold &&
                    rotationDelta <= RigRotationDeltaThresholdDegrees)
                {
                    continue;
                }

                transformChanges++;
                Vector3 scaleDelta = transform.localScale - baseline.LocalScale;
                logger?.LogInfo(
                    "[RigDiff] transform_changed: " +
                    $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}' " +
                    $"localPositionDelta={FormatVector(positionDelta)} " +
                    $"positionDeltaMagnitude={positionMagnitude.ToString("R", CultureInfo.InvariantCulture)} " +
                    $"rotationDeltaDegrees={rotationDelta.ToString("R", CultureInfo.InvariantCulture)} " +
                    $"localScaleDelta={FormatVector(scaleDelta)}.");
            }

            for (int i = 0; i < pristineRig.TwoBoneConstraints.Length; i++)
            {
                TwoBoneIkBaseline baseline = pristineRig.TwoBoneConstraints[i];
                if (!baseline.TryReadCurrent(out TwoBoneIkValues current))
                {
                    constraintChanges++;
                    logger?.LogInfo(
                        "[RigDiff] constraint_missing: " +
                        $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}'.");
                    continue;
                }

                if (baseline.Values.Equals(current))
                    continue;

                constraintChanges++;
                logger?.LogInfo(
                    "[RigDiff] constraint_changed: " +
                    $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}' " +
                    $"target='{baseline.Values.TargetName}'->'{current.TargetName}' " +
                    $"hint='{baseline.Values.HintName}'->'{current.HintName}' " +
                    $"weight={FormatDelta(baseline.Values.Weight, current.Weight)} " +
                    $"targetPositionWeight={FormatDelta(baseline.Values.TargetPositionWeight, current.TargetPositionWeight)} " +
                    $"targetRotationWeight={FormatDelta(baseline.Values.TargetRotationWeight, current.TargetRotationWeight)} " +
                    $"hintWeight={FormatDelta(baseline.Values.HintWeight, current.HintWeight)} " +
                    $"maintainTargetPositionOffset={baseline.Values.MaintainTargetPositionOffset}->{current.MaintainTargetPositionOffset} " +
                    $"maintainTargetRotationOffset={baseline.Values.MaintainTargetRotationOffset}->{current.MaintainTargetRotationOffset}.");
            }

            for (int i = 0; i < pristineRig.ChainConstraints.Length; i++)
            {
                ChainIkBaseline baseline = pristineRig.ChainConstraints[i];
                if (!baseline.TryReadCurrent(out ChainIkValues current))
                {
                    constraintChanges++;
                    logger?.LogInfo(
                        "[RigDiff] chain_constraint_missing: " +
                        $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}'.");
                    continue;
                }

                if (baseline.Values.Equals(current))
                    continue;

                constraintChanges++;
                logger?.LogInfo(
                    "[RigDiff] chain_constraint_changed: " +
                    $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}' " +
                    $"root='{baseline.Values.RootName}'->'{current.RootName}' " +
                    $"tip='{baseline.Values.TipName}'->'{current.TipName}' " +
                    $"target='{baseline.Values.TargetName}'->'{current.TargetName}' " +
                    $"weight={FormatDelta(baseline.Values.Weight, current.Weight)} " +
                    $"chainRotationWeight={FormatDelta(baseline.Values.ChainRotationWeight, current.ChainRotationWeight)} " +
                    $"tipRotationWeight={FormatDelta(baseline.Values.TipRotationWeight, current.TipRotationWeight)} " +
                    $"maxIterations={baseline.Values.MaxIterations}->{current.MaxIterations} " +
                    $"tolerance={FormatDelta(baseline.Values.Tolerance, current.Tolerance)} " +
                    $"maintainTargetPositionOffset={baseline.Values.MaintainTargetPositionOffset}->{current.MaintainTargetPositionOffset} " +
                    $"maintainTargetRotationOffset={baseline.Values.MaintainTargetRotationOffset}->{current.MaintainTargetRotationOffset}.");
            }

            for (int i = 0; i < pristineRig.Rigs.Length; i++)
            {
                RigWeightBaseline baseline = pristineRig.Rigs[i];
                if (!baseline.TryReadCurrent(out float currentWeight))
                {
                    rigChanges++;
                    logger?.LogInfo(
                        "[RigDiff] rig_missing: " +
                        $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}'.");
                    continue;
                }

                if (baseline.Weight.Equals(currentWeight))
                    continue;

                rigChanges++;
                logger?.LogInfo(
                    "[RigDiff] rig_changed: " +
                    $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}' " +
                    $"weight={FormatDelta(baseline.Weight, currentWeight)}.");
            }

            for (int i = 0; i < pristineRig.RigLayers.Length; i++)
            {
                RigLayerBaseline baseline = pristineRig.RigLayers[i];
                if (!baseline.TryReadCurrent(out RigLayerValues current))
                {
                    rigLayerChanges++;
                    logger?.LogInfo(
                        "[RigDiff] rig_layer_missing: " +
                        $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}'.");
                    continue;
                }

                if (baseline.Values.Equals(current))
                    continue;

                rigLayerChanges++;
                logger?.LogInfo(
                    "[RigDiff] rig_layer_changed: " +
                    $"frame={Time.frameCount} reason='{reason}' path='{baseline.Path}' " +
                    $"rig='{baseline.Values.RigName}'->'{current.RigName}' " +
                    $"active={baseline.Values.Active}->{current.Active} " +
                    $"weight={FormatDelta(baseline.Values.Weight, current.Weight)}.");
            }

            logger?.LogInfo(
                "[RigDiff] dump_end: " +
                $"frame={Time.frameCount} reason='{reason}' " +
                $"transformChanges={transformChanges} constraintChanges={constraintChanges} " +
                $"rigChanges={rigChanges} rigLayerChanges={rigLayerChanges}.");
        }

        private static bool IsLocalPlayer(PlayerControllerB player)
        {
            if (player == null)
                return false;

            try
            {
                PlayerControllerB local = GameNetworkManager.Instance != null
                    ? GameNetworkManager.Instance.localPlayerController
                    : null;
                if (local == null && StartOfRound.Instance != null)
                    local = StartOfRound.Instance.localPlayerController;
                return ReferenceEquals(player, local);
            }
            catch
            {
                return false;
            }
        }

        private static PlayerControllerB ResolveLocalPlayer()
        {
            try
            {
                PlayerControllerB player = GameNetworkManager.Instance != null
                    ? GameNetworkManager.Instance.localPlayerController
                    : null;
                if (player == null && StartOfRound.Instance != null)
                    player = StartOfRound.Instance.localPlayerController;
                return player;
            }
            catch
            {
                return null;
            }
        }

        private static Transform FindChildRecursive(Transform root, string childName)
        {
            if (root == null)
                return null;
            if (string.Equals(root.name, childName, StringComparison.Ordinal))
                return root;

            for (int i = 0; i < root.childCount; i++)
            {
                Transform found = FindChildRecursive(root.GetChild(i), childName);
                if (found != null)
                    return found;
            }

            return null;
        }

        private static Transform FindDirectChild(Transform parent, string childName)
        {
            if (parent == null)
                return null;

            for (int i = 0; i < parent.childCount; i++)
            {
                Transform child = parent.GetChild(i);
                if (child != null &&
                    string.Equals(child.name, childName, StringComparison.Ordinal))
                {
                    return child;
                }
            }

            return null;
        }

        private static string GetRelativePath(Transform root, Transform transform)
        {
            if (transform == null)
                return "<null>";
            if (root == null || ReferenceEquals(root, transform))
                return transform.name;

            var names = new List<string>();
            Transform current = transform;
            while (current != null && !ReferenceEquals(current, root))
            {
                names.Add(current.name);
                current = current.parent;
            }

            if (ReferenceEquals(current, root))
                names.Add(root.name);
            names.Reverse();
            return string.Join("/", names);
        }

        private static bool ReadEnabled(ConfigEntry<bool> entry, bool fallback)
        {
            try
            {
                return entry != null ? entry.Value : fallback;
            }
            catch
            {
                return fallback;
            }
        }

        private static bool PristineRigCaptureEnabled()
        {
            return ReadEnabled(enablePristineRigDiffProbe, false) ||
                   ReadEnabled(restorePristineRigControlPose, true);
        }

        private static string FormatVector(Vector3 value)
        {
            return string.Concat(
                "(",
                value.x.ToString("R", CultureInfo.InvariantCulture),
                ",",
                value.y.ToString("R", CultureInfo.InvariantCulture),
                ",",
                value.z.ToString("R", CultureInfo.InvariantCulture),
                ")");
        }

        private static string FormatQuaternion(Quaternion value)
        {
            return string.Concat(
                "(",
                value.x.ToString("R", CultureInfo.InvariantCulture),
                ",",
                value.y.ToString("R", CultureInfo.InvariantCulture),
                ",",
                value.z.ToString("R", CultureInfo.InvariantCulture),
                ",",
                value.w.ToString("R", CultureInfo.InvariantCulture),
                ")");
        }

        private static string FormatFloat(float value)
        {
            return value.ToString("R", CultureInfo.InvariantCulture);
        }

        private static string DescribePlayer(PlayerControllerB player)
        {
            if (player == null)
                return "<null>";

            try
            {
                string name = string.IsNullOrWhiteSpace(player.playerUsername)
                    ? player.name
                    : player.playerUsername;
                return string.Concat(
                    SanitizeLogValue(name),
                    " (clientId=",
                    player.actualClientId.ToString(CultureInfo.InvariantCulture),
                    ")");
            }
            catch
            {
                return "<unavailable>";
            }
        }

        private static string SanitizeLogValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "<empty>"
                : value.Replace('\r', ' ').Replace('\n', ' ').Replace('\'', '"');
        }

        private static string FormatDelta(float before, float after)
        {
            return string.Concat(
                before.ToString("R", CultureInfo.InvariantCulture),
                "->",
                after.ToString("R", CultureInfo.InvariantCulture),
                " delta=",
                (after - before).ToString("R", CultureInfo.InvariantCulture));
        }

        private static object ReadMember(object instance, string propertyName, string fieldName)
        {
            if (instance == null)
                return null;

            Type type = instance.GetType();
            FieldInfo field = FindField(type, fieldName);
            if (field != null)
            {
                try { return field.GetValue(instance); }
                catch { }
            }

            if (!string.IsNullOrWhiteSpace(propertyName))
            {
                try
                {
                    PropertyInfo property = type.GetProperty(
                        propertyName,
                        BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                    if (property != null)
                        return property.GetValue(instance, null);
                }
                catch { }
            }

            return null;
        }

        private static FieldInfo FindField(Type type, string fieldName)
        {
            while (type != null)
            {
                FieldInfo field = type.GetField(
                    fieldName,
                    BindingFlags.Instance | BindingFlags.Public |
                    BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
                if (field != null)
                    return field;
                type = type.BaseType;
            }

            return null;
        }

        private static float ReadFloat(object instance, string propertyName, string fieldName)
        {
            object value = ReadMember(instance, propertyName, fieldName);
            try
            {
                return value != null
                    ? Convert.ToSingle(value, CultureInfo.InvariantCulture)
                    : float.NaN;
            }
            catch
            {
                return float.NaN;
            }
        }

        private static int ReadInt(object instance, string propertyName, string fieldName)
        {
            object value = ReadMember(instance, propertyName, fieldName);
            try
            {
                return value != null
                    ? Convert.ToInt32(value, CultureInfo.InvariantCulture)
                    : int.MinValue;
            }
            catch
            {
                return int.MinValue;
            }
        }

        private static bool ReadBool(object instance, string propertyName, string fieldName)
        {
            object value = ReadMember(instance, propertyName, fieldName);
            try { return value != null && Convert.ToBoolean(value, CultureInfo.InvariantCulture); }
            catch { return false; }
        }

        private static string ReadObjectName(object instance, string propertyName, string fieldName)
        {
            UnityEngine.Object value =
                ReadMember(instance, propertyName, fieldName) as UnityEngine.Object;
            return value != null ? value.name : "<null>";
        }

        private sealed class RestoreFrameSample
        {
            internal readonly int[] LayerStateHashes;
            internal readonly float[] LayerNormalizedTimes;
            internal bool Valid;
            internal int Frame;
            internal Vector3 GameplayCameraWorldPosition;
            internal Vector3 CameraContainerLocalPosition;
            internal Vector3 CameraContainerWorldPosition;
            internal Vector3 ArmsMetarigWorldPosition;
            internal string ControllerName;
            internal int ActualLayerCount;
            internal int CapturedLayerCount;

            internal RestoreFrameSample(int maxAnimatorLayers)
            {
                LayerStateHashes = new int[maxAnimatorLayers];
                LayerNormalizedTimes = new float[maxAnimatorLayers];
                ControllerName = "<null>";
            }
        }

        private sealed class RenderSeamFrameSample
        {
            internal bool Valid;
            internal PlayerControllerB Player;
            internal int Frame;
            internal float DeltaTime;
            internal float UnscaledDeltaTime;
            internal RenderPoseSample GameplayCamera;
            internal RenderPoseSample RightHand;
            internal RenderPoseSample LeftHand;
            internal RenderPoseSample ArmsMetarig;
            internal RenderPoseSample LocalArms;
            internal RenderPoseSample LocalVisor;
            internal RenderPoseSample LocalVisorTargetPoint;
            internal RenderVisibilitySample LocalVisorVisibility;
            internal bool VisorCameraPresent;
            internal bool VisorCameraEnabled;
            internal bool CameraParametersPresent;
            internal float CameraFieldOfView;
            internal float CameraNearClipPlane;
            internal RenderVisibilitySample HeldItem;

            internal void CopyFrom(RenderSeamFrameSample other)
            {
                if (other == null)
                {
                    Reset();
                    return;
                }

                Valid = other.Valid;
                Player = other.Player;
                Frame = other.Frame;
                DeltaTime = other.DeltaTime;
                UnscaledDeltaTime = other.UnscaledDeltaTime;
                GameplayCamera = other.GameplayCamera;
                RightHand = other.RightHand;
                LeftHand = other.LeftHand;
                ArmsMetarig = other.ArmsMetarig;
                LocalArms = other.LocalArms;
                LocalVisor = other.LocalVisor;
                LocalVisorTargetPoint = other.LocalVisorTargetPoint;
                LocalVisorVisibility = other.LocalVisorVisibility;
                VisorCameraPresent = other.VisorCameraPresent;
                VisorCameraEnabled = other.VisorCameraEnabled;
                CameraParametersPresent = other.CameraParametersPresent;
                CameraFieldOfView = other.CameraFieldOfView;
                CameraNearClipPlane = other.CameraNearClipPlane;
                HeldItem = other.HeldItem;
            }

            internal void Reset()
            {
                Valid = false;
                Player = null;
                Frame = 0;
                DeltaTime = 0f;
                UnscaledDeltaTime = 0f;
                GameplayCamera = default;
                RightHand = default;
                LeftHand = default;
                ArmsMetarig = default;
                LocalArms = default;
                LocalVisor = default;
                LocalVisorTargetPoint = default;
                LocalVisorVisibility = default;
                VisorCameraPresent = false;
                VisorCameraEnabled = false;
                CameraParametersPresent = false;
                CameraFieldOfView = 0f;
                CameraNearClipPlane = 0f;
                HeldItem = default;
            }
        }

        private sealed class RenderSeamWindow
        {
            internal readonly string Phase;
            internal bool Active;
            internal int SeamFrame;
            internal int EndFrame;
            internal int LastLoggedFrame;
            internal GameObject AnimatedProp;

            internal RenderSeamWindow(string phase)
            {
                Phase = phase;
                Reset();
            }

            internal void Activate(int seamFrame, int endFrame, GameObject animatedProp)
            {
                Active = true;
                SeamFrame = seamFrame;
                EndFrame = endFrame;
                LastLoggedFrame = -1;
                AnimatedProp = animatedProp;
            }

            internal void Reset()
            {
                Active = false;
                SeamFrame = 0;
                EndFrame = 0;
                LastLoggedFrame = -1;
                AnimatedProp = null;
            }
        }

        private struct RenderPoseSample
        {
            internal bool Present;
            internal Vector3 WorldPosition;
            internal Vector3 WorldEulerAngles;
        }

        private struct RenderVisibilitySample
        {
            internal bool Present;
            internal string Name;
            internal bool ActiveInHierarchy;
            internal int RendererCount;
            internal int EnabledRendererCount;
            internal bool AnyRendererEnabled;
            internal bool ReadFailed;
        }

        internal sealed class ThirdPersonRigPoseSnapshot
        {
            private readonly ThirdPersonRigPoseBaseline[] baselines;

            internal int FullPoseCount { get; }
            internal int RotationOnlyCount { get; }
            internal int TotalCount => baselines.Length;
            internal bool IsComplete { get; }
            internal string MissingTargets { get; }
            internal bool PlausibleAtCapture { get; private set; }
            internal string Source { get; private set; } =
                ThirdPersonRigAuthoredDefaultSource;

            /// <summary>
            /// Stamps capture-time metadata: whether the vanilla-rest sanity check judged the
            /// captured pose plausible, and which pipeline stage produced it. Set once at
            /// capture/replace time; the values are telemetry plus the recapture gate's input.
            /// </summary>
            internal void SetCaptureMetadata(bool plausibleAtCapture, string source)
            {
                PlausibleAtCapture = plausibleAtCapture;
                Source = source;
            }

            private ThirdPersonRigPoseSnapshot(
                ThirdPersonRigPoseBaseline[] baselines,
                int fullPoseCount,
                int rotationOnlyCount,
                string[] missingTargets)
            {
                this.baselines = baselines ?? Array.Empty<ThirdPersonRigPoseBaseline>();
                FullPoseCount = fullPoseCount;
                RotationOnlyCount = rotationOnlyCount;
                IsComplete = missingTargets == null || missingTargets.Length == 0;
                MissingTargets = IsComplete
                    ? "<none>"
                    : string.Join(",", missingTargets);
            }

            internal static ThirdPersonRigPoseSnapshot Capture(PlayerControllerB player)
            {
                Transform animatorRoot = null;
                try
                {
                    animatorRoot = player != null && player.playerBodyAnimator != null
                        ? player.playerBodyAnimator.transform
                        : null;
                }
                catch { }

                if (animatorRoot == null)
                    return null;

                Transform spine003 = FindChildRecursive(animatorRoot, "spine.003");
                Transform rigOne = FindChildRecursive(animatorRoot, "Rig 1");
                Transform leftLeg = FindDirectChild(rigOne, "LeftLeg");
                Transform rightLeg = FindDirectChild(rigOne, "RightLeg");
                var captured = new List<ThirdPersonRigPoseBaseline>(12);
                var capturedTransforms = new HashSet<Transform>();
                var missing = new List<string>(10);

                AddFullPose(
                    FindDirectChild(spine003, "LeftArm_target"),
                    "spine.003/LeftArm_target",
                    captured,
                    capturedTransforms,
                    missing);
                AddFullPose(
                    FindDirectChild(spine003, "RightArm_target"),
                    "spine.003/RightArm_target",
                    captured,
                    capturedTransforms,
                    missing);
                AddControlGroup(
                    leftLeg,
                    "Rig 1/LeftLeg",
                    new[] { "LeftLeg_hint", "LeftLeg_target" },
                    captured,
                    capturedTransforms,
                    missing);
                AddControlGroup(
                    rightLeg,
                    "Rig 1/RightLeg",
                    new[] { "RightLeg_hint", "RightLeg_target" },
                    captured,
                    capturedTransforms,
                    missing);
                AddRotationOnly(
                    FindChildRecursive(animatorRoot, "shin.L"),
                    "spine/thigh.L/shin.L",
                    captured,
                    capturedTransforms,
                    missing);
                AddRotationOnly(
                    FindChildRecursive(animatorRoot, "shin.R"),
                    "spine/thigh.R/shin.R",
                    captured,
                    capturedTransforms,
                    missing);

                int fullPoseCount = 0;
                int rotationOnlyCount = 0;
                for (int i = 0; i < captured.Count; i++)
                {
                    if (captured[i].RotationOnly)
                        rotationOnlyCount++;
                    else
                        fullPoseCount++;
                }

                return new ThirdPersonRigPoseSnapshot(
                    captured.ToArray(),
                    fullPoseCount,
                    rotationOnlyCount,
                    missing.ToArray());
            }

            private static void AddControlGroup(
                Transform group,
                string groupPath,
                string[] requiredChildren,
                ICollection<ThirdPersonRigPoseBaseline> captured,
                ISet<Transform> capturedTransforms,
                ICollection<string> missing)
            {
                if (group == null)
                {
                    missing.Add(groupPath);
                    for (int i = 0; i < requiredChildren.Length; i++)
                        missing.Add(groupPath + "/" + requiredChildren[i]);
                    return;
                }

                Transform[] groupTransforms =
                    group.GetComponentsInChildren<Transform>(includeInactive: true);
                for (int i = 0; i < groupTransforms.Length; i++)
                {
                    Transform transform = groupTransforms[i];
                    string relativePath = GetRelativePath(group, transform);
                    string path = ReferenceEquals(group, transform)
                        ? groupPath
                        : groupPath + relativePath.Substring(group.name.Length);
                    AddFullPose(
                        transform,
                        path,
                        captured,
                        capturedTransforms,
                        missing: null);
                }

                for (int i = 0; i < requiredChildren.Length; i++)
                {
                    string childName = requiredChildren[i];
                    if (FindDirectChild(group, childName) == null)
                        missing.Add(groupPath + "/" + childName);
                }
            }

            private static void AddFullPose(
                Transform transform,
                string path,
                ICollection<ThirdPersonRigPoseBaseline> captured,
                ISet<Transform> capturedTransforms,
                ICollection<string> missing)
            {
                if (transform == null)
                {
                    missing?.Add(path);
                    return;
                }

                if (!capturedTransforms.Add(transform))
                    return;

                captured.Add(
                    new ThirdPersonRigPoseBaseline(
                        transform,
                        path,
                        rotationOnly: false));
            }

            private static void AddRotationOnly(
                Transform transform,
                string path,
                ICollection<ThirdPersonRigPoseBaseline> captured,
                ISet<Transform> capturedTransforms,
                ICollection<string> missing)
            {
                if (transform == null)
                {
                    missing.Add(path);
                    return;
                }

                if (!capturedTransforms.Add(transform))
                    return;

                captured.Add(
                    new ThirdPersonRigPoseBaseline(
                        transform,
                        path,
                        rotationOnly: true));
            }

            internal ThirdPersonRigPlausibility EvaluateVanillaRestPlausibility()
            {
                int positionOutliers = 0;
                int rotationOutliers = 0;
                int scaleOutliers = 0;
                float maxPositionDelta = 0f;
                float maxRotationDelta = 0f;
                float maxScaleDelta = 0f;
                bool nonFinite = false;

                for (int i = 0; i < baselines.Length; i++)
                {
                    ThirdPersonRigPoseBaseline baseline = baselines[i];
                    if (!baseline.HasExpectedVanillaRest)
                        continue;

                    if (!baseline.Finite)
                    {
                        nonFinite = true;
                        continue;
                    }

                    float rotationDelta =
                        Quaternion.Angle(
                            baseline.ExpectedLocalRotation,
                            baseline.LocalRotation);
                    maxRotationDelta = Mathf.Max(maxRotationDelta, rotationDelta);
                    if (rotationDelta > ThirdPersonRigRestRotationThresholdDegrees)
                        rotationOutliers++;

                    if (baseline.RotationOnly)
                        continue;

                    float positionDelta =
                        Vector3.Distance(
                            baseline.ExpectedLocalPosition,
                            baseline.LocalPosition);
                    float scaleDelta =
                        Vector3.Distance(
                            baseline.ExpectedLocalScale,
                            baseline.LocalScale);
                    maxPositionDelta = Mathf.Max(maxPositionDelta, positionDelta);
                    maxScaleDelta = Mathf.Max(maxScaleDelta, scaleDelta);
                    if (positionDelta > ThirdPersonRigRestPositionThreshold)
                        positionOutliers++;
                    if (scaleDelta > ThirdPersonRigRestScaleThreshold)
                        scaleOutliers++;
                }

                bool plausible =
                    IsComplete &&
                    !nonFinite &&
                    positionOutliers == 0 &&
                    rotationOutliers == 0 &&
                    scaleOutliers == 0;
                string reason = !IsComplete
                    ? "required_transforms_missing"
                    : nonFinite
                        ? "non_finite_local_pose"
                        : plausible
                            ? "within_vanilla_rest_thresholds"
                            : "outside_vanilla_rest_thresholds";
                return new ThirdPersonRigPlausibility(
                    plausible,
                    reason,
                    positionOutliers,
                    rotationOutliers,
                    scaleOutliers,
                    maxPositionDelta,
                    maxRotationDelta,
                    maxScaleDelta);
            }

            internal void RestoreExcept(
                ISet<Transform> excludedTransforms,
                ISet<Transform> restoredTransforms,
                out int fullPoseRestored,
                out int rotationOnlyRestored)
            {
                fullPoseRestored = 0;
                rotationOnlyRestored = 0;
                for (int i = 0; i < baselines.Length; i++)
                {
                    ThirdPersonRigPoseBaseline baseline = baselines[i];
                    Transform transform = baseline.Transform;
                    if (transform == null ||
                        (excludedTransforms != null &&
                         excludedTransforms.Contains(transform)) ||
                        !baseline.Restore())
                    {
                        continue;
                    }

                    restoredTransforms?.Add(transform);
                    if (baseline.RotationOnly)
                        rotationOnlyRestored++;
                    else
                        fullPoseRestored++;
                }
            }
        }

        private sealed class ThirdPersonRigPoseBaseline
        {
            internal Transform Transform { get; }
            internal string Path { get; }
            internal bool RotationOnly { get; }
            internal Vector3 LocalPosition { get; }
            internal Quaternion LocalRotation { get; }
            internal Vector3 LocalScale { get; }
            internal bool HasExpectedVanillaRest { get; }
            internal Vector3 ExpectedLocalPosition { get; }
            internal Quaternion ExpectedLocalRotation { get; }
            internal Vector3 ExpectedLocalScale { get; }
            internal bool Finite =>
                IsFinite(LocalPosition) &&
                IsFinite(LocalRotation) &&
                IsFinite(LocalScale);

            internal ThirdPersonRigPoseBaseline(
                Transform transform,
                string path,
                bool rotationOnly)
            {
                Transform = transform;
                Path = path;
                RotationOnly = rotationOnly;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
                HasExpectedVanillaRest = TryGetExpectedVanillaRest(
                    path,
                    out Vector3 expectedLocalPosition,
                    out Quaternion expectedLocalRotation,
                    out Vector3 expectedLocalScale);
                ExpectedLocalPosition = expectedLocalPosition;
                ExpectedLocalRotation = expectedLocalRotation;
                ExpectedLocalScale = expectedLocalScale;
            }

            internal bool Restore()
            {
                if (Transform == null)
                    return false;

                if (!RotationOnly)
                {
                    Transform.localPosition = LocalPosition;
                    Transform.localScale = LocalScale;
                }
                Transform.localRotation = LocalRotation;
                return true;
            }

            private static bool TryGetExpectedVanillaRest(
                string path,
                out Vector3 localPosition,
                out Quaternion localRotation,
                out Vector3 localScale)
            {
                localPosition = Vector3.zero;
                localRotation = Quaternion.identity;
                localScale = Vector3.one;
                switch (path)
                {
                    case "spine.003/LeftArm_target":
                        localPosition = new Vector3(-1.045884f, -0.057756394f, -0.044099636f);
                        localRotation =
                            new Quaternion(-0.78043324f, 0.6235791f, 0.028422598f, 0.03557171f);
                        return true;
                    case "spine.003/RightArm_target":
                        localPosition = new Vector3(1.0641153f, -0.06609607f, -0.030803302f);
                        localRotation =
                            new Quaternion(-0.7733263f, -0.6323711f, -0.028823216f, 0.0352481f);
                        return true;
                    case "Rig 1/LeftLeg":
                    case "Rig 1/RightLeg":
                        localPosition = new Vector3(-9.584f, 0f, -11.081f);
                        return true;
                    case "Rig 1/LeftLeg/LeftLeg_hint":
                        localPosition = new Vector3(1.001f, 0.746f, 2.4f);
                        return true;
                    case "Rig 1/LeftLeg/LeftLeg_target":
                        localPosition = new Vector3(0.99f, 0.209f, 1.011f);
                        localRotation =
                            new Quaternion(0.89385676f, -0.000001803443f, -0.0000011043475f, 0.4483527f);
                        return true;
                    case "Rig 1/RightLeg/RightLeg_hint":
                        localPosition = new Vector3(1.486f, 0.658f, 2.823f);
                        return true;
                    case "Rig 1/RightLeg/RightLeg_target":
                        localPosition = new Vector3(1.436f, 0.27f, 1.058f);
                        localRotation =
                            new Quaternion(0.89385664f, 0.00000039951823f, -0.00000006130153f, 0.44835275f);
                        return true;
                    case "spine/thigh.L/shin.L":
                        localRotation =
                            new Quaternion(0.036354546f, -0.0022051183f, -0.008516384f, 0.99930024f);
                        return true;
                    case "spine/thigh.R/shin.R":
                        localRotation =
                            new Quaternion(0.036352716f, 0.002243982f, 0.008508066f, 0.9993003f);
                        return true;
                    default:
                        return false;
                }
            }

            private static bool IsFinite(Vector3 value)
            {
                return IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);
            }

            private static bool IsFinite(Quaternion value)
            {
                return IsFinite(value.x) && IsFinite(value.y) &&
                       IsFinite(value.z) && IsFinite(value.w);
            }

            private static bool IsFinite(float value)
            {
                return !float.IsNaN(value) && !float.IsInfinity(value);
            }
        }

        internal readonly struct ThirdPersonRigPlausibility
        {
            internal bool Plausible { get; }
            internal string Reason { get; }
            internal int PositionOutliers { get; }
            internal int RotationOutliers { get; }
            internal int ScaleOutliers { get; }
            internal float MaxPositionDelta { get; }
            internal float MaxRotationDeltaDegrees { get; }
            internal float MaxScaleDelta { get; }

            internal ThirdPersonRigPlausibility(
                bool plausible,
                string reason,
                int positionOutliers,
                int rotationOutliers,
                int scaleOutliers,
                float maxPositionDelta,
                float maxRotationDeltaDegrees,
                float maxScaleDelta)
            {
                Plausible = plausible;
                Reason = reason;
                PositionOutliers = positionOutliers;
                RotationOutliers = rotationOutliers;
                ScaleOutliers = scaleOutliers;
                MaxPositionDelta = maxPositionDelta;
                MaxRotationDeltaDegrees = maxRotationDeltaDegrees;
                MaxScaleDelta = maxScaleDelta;
            }
        }

        private sealed class RemoteRigProbeSchedule
        {
            internal PlayerControllerB Player { get; }
            internal int ProbeId { get; }
            internal int RestoreFrame { get; }
            internal float FiveSecondRealtime { get; }
            internal int LateUpdatesRemaining { get; set; }
            internal bool LateUpdateSampleLogged { get; set; }
            internal bool FiveSecondSampleLogged { get; set; }

            internal RemoteRigProbeSchedule(
                PlayerControllerB player,
                int probeId,
                int restoreFrame,
                float fiveSecondRealtime)
            {
                Player = player;
                ProbeId = probeId;
                RestoreFrame = restoreFrame;
                FiveSecondRealtime = fiveSecondRealtime;
                LateUpdatesRemaining = 2;
            }
        }

        private sealed class PristineRigSnapshot
        {
            internal readonly PlayerControllerB Player;
            internal readonly TransformBaseline[] Transforms;
            internal readonly TwoBoneIkBaseline[] TwoBoneConstraints;
            internal readonly ChainIkBaseline[] ChainConstraints;
            internal readonly RigWeightBaseline[] Rigs;
            internal readonly RigLayerBaseline[] RigLayers;

            private PristineRigSnapshot(
                PlayerControllerB player,
                TransformBaseline[] transforms,
                TwoBoneIkBaseline[] twoBoneConstraints,
                ChainIkBaseline[] chainConstraints,
                RigWeightBaseline[] rigs,
                RigLayerBaseline[] rigLayers)
            {
                Player = player;
                Transforms = transforms;
                TwoBoneConstraints = twoBoneConstraints;
                ChainConstraints = chainConstraints;
                Rigs = rigs;
                RigLayers = rigLayers;
            }

            internal static PristineRigSnapshot Capture(
                PlayerControllerB player,
                Transform metarig,
                Transform rigArms,
                Type twoBoneIkConstraintType,
                Type chainIkConstraintType)
            {
                Transform[] rigTransforms =
                    rigArms.GetComponentsInChildren<Transform>(includeInactive: true);
                var transforms = new TransformBaseline[rigTransforms.Length + 1];
                transforms[0] = new TransformBaseline(metarig, metarig.name + " (metarig root)");
                for (int i = 0; i < rigTransforms.Length; i++)
                {
                    Transform transform = rigTransforms[i];
                    transforms[i + 1] = new TransformBaseline(
                        transform,
                        GetRelativePath(metarig, transform));
                }

                Component[] components =
                    metarig.GetComponentsInChildren<Component>(includeInactive: true);
                var twoBoneConstraints = new List<TwoBoneIkBaseline>();
                var chainConstraints = new List<ChainIkBaseline>();
                var rigs = new List<RigWeightBaseline>();
                var rigLayers = new List<RigLayerBaseline>();
                for (int i = 0; i < components.Length; i++)
                {
                    Component component = components[i];
                    if (component == null)
                        continue;

                    Type componentType = component.GetType();
                    string fullName = componentType.FullName ?? componentType.Name;
                    string path = GetRelativePath(metarig, component.transform);
                    if (twoBoneIkConstraintType != null &&
                        twoBoneIkConstraintType.IsAssignableFrom(componentType))
                    {
                        twoBoneConstraints.Add(new TwoBoneIkBaseline(component, path));
                    }

                    if (chainIkConstraintType != null &&
                        chainIkConstraintType.IsAssignableFrom(componentType))
                    {
                        chainConstraints.Add(new ChainIkBaseline(component, path));
                    }

                    if (string.Equals(fullName, RigTypeName, StringComparison.Ordinal))
                        rigs.Add(new RigWeightBaseline(component, path));

                    if (!string.Equals(fullName, RigBuilderTypeName, StringComparison.Ordinal))
                        continue;

                    object layersObject = ReadMember(component, "layers", "m_RigLayers");
                    if (!(layersObject is IList layers))
                        continue;

                    for (int layerIndex = 0; layerIndex < layers.Count; layerIndex++)
                    {
                        object layer = layers[layerIndex];
                        if (layer == null)
                            continue;
                        rigLayers.Add(
                            new RigLayerBaseline(
                                layer,
                                path + "/layer[" +
                                layerIndex.ToString(CultureInfo.InvariantCulture) + "]"));
                    }
                }

                return new PristineRigSnapshot(
                    player,
                    transforms,
                    twoBoneConstraints.ToArray(),
                    chainConstraints.ToArray(),
                    rigs.ToArray(),
                    rigLayers.ToArray());
            }

            internal int RestoreRigControlPose(
                Transform rigArms,
                ISet<Transform> restoredTransforms)
            {
                int restored = 0;
                for (int i = 0; i < Transforms.Length; i++)
                {
                    TransformBaseline baseline = Transforms[i];
                    Transform transform = baseline.Transform;
                    if (transform == null ||
                        (!ReferenceEquals(transform, rigArms) && !transform.IsChildOf(rigArms)))
                    {
                        continue;
                    }

                    baseline.Restore();
                    restoredTransforms.Add(transform);
                    restored++;
                }

                return restored;
            }
        }

        private sealed class TransformBaseline
        {
            internal readonly Transform Transform;
            internal readonly string Path;
            internal readonly Vector3 LocalPosition;
            internal readonly Quaternion LocalRotation;
            internal readonly Vector3 LocalScale;

            internal TransformBaseline(Transform transform, string path)
            {
                Transform = transform;
                Path = path;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
            }

            internal void Restore()
            {
                Transform.localPosition = LocalPosition;
                Transform.localRotation = LocalRotation;
                Transform.localScale = LocalScale;
            }
        }

        private readonly struct TwoBoneIkValues : IEquatable<TwoBoneIkValues>
        {
            internal readonly string TargetName;
            internal readonly string HintName;
            internal readonly float Weight;
            internal readonly float TargetPositionWeight;
            internal readonly float TargetRotationWeight;
            internal readonly float HintWeight;
            internal readonly bool MaintainTargetPositionOffset;
            internal readonly bool MaintainTargetRotationOffset;

            internal TwoBoneIkValues(Component component)
            {
                object data = ReadMember(component, null, "m_Data");
                TargetName = ReadObjectName(data, "target", "m_Target");
                HintName = ReadObjectName(data, "hint", "m_Hint");
                Weight = ReadFloat(component, "weight", "m_Weight");
                TargetPositionWeight =
                    ReadFloat(data, "targetPositionWeight", "m_TargetPositionWeight");
                TargetRotationWeight =
                    ReadFloat(data, "targetRotationWeight", "m_TargetRotationWeight");
                HintWeight = ReadFloat(data, "hintWeight", "m_HintWeight");
                MaintainTargetPositionOffset =
                    ReadBool(data, "maintainTargetPositionOffset", "m_MaintainTargetPositionOffset");
                MaintainTargetRotationOffset =
                    ReadBool(data, "maintainTargetRotationOffset", "m_MaintainTargetRotationOffset");
            }

            public bool Equals(TwoBoneIkValues other)
            {
                return string.Equals(TargetName, other.TargetName, StringComparison.Ordinal) &&
                       string.Equals(HintName, other.HintName, StringComparison.Ordinal) &&
                       Weight.Equals(other.Weight) &&
                       TargetPositionWeight.Equals(other.TargetPositionWeight) &&
                       TargetRotationWeight.Equals(other.TargetRotationWeight) &&
                       HintWeight.Equals(other.HintWeight) &&
                       MaintainTargetPositionOffset == other.MaintainTargetPositionOffset &&
                       MaintainTargetRotationOffset == other.MaintainTargetRotationOffset;
            }
        }

        private sealed class TwoBoneIkBaseline
        {
            private readonly Component component;
            internal readonly string Path;
            internal readonly TwoBoneIkValues Values;

            internal TwoBoneIkBaseline(Component component, string path)
            {
                this.component = component;
                Path = path;
                Values = new TwoBoneIkValues(component);
            }

            internal bool TryReadCurrent(out TwoBoneIkValues current)
            {
                if (component == null)
                {
                    current = default;
                    return false;
                }

                current = new TwoBoneIkValues(component);
                return true;
            }
        }

        private readonly struct ChainIkValues : IEquatable<ChainIkValues>
        {
            internal readonly string RootName;
            internal readonly string TipName;
            internal readonly string TargetName;
            internal readonly float Weight;
            internal readonly float ChainRotationWeight;
            internal readonly float TipRotationWeight;
            internal readonly int MaxIterations;
            internal readonly float Tolerance;
            internal readonly bool MaintainTargetPositionOffset;
            internal readonly bool MaintainTargetRotationOffset;

            internal ChainIkValues(Component component)
            {
                object data = ReadMember(component, null, "m_Data");
                RootName = ReadObjectName(data, "root", "m_Root");
                TipName = ReadObjectName(data, "tip", "m_Tip");
                TargetName = ReadObjectName(data, "target", "m_Target");
                Weight = ReadFloat(component, "weight", "m_Weight");
                ChainRotationWeight =
                    ReadFloat(data, "chainRotationWeight", "m_ChainRotationWeight");
                TipRotationWeight =
                    ReadFloat(data, "tipRotationWeight", "m_TipRotationWeight");
                MaxIterations = ReadInt(data, "maxIterations", "m_MaxIterations");
                Tolerance = ReadFloat(data, "tolerance", "m_Tolerance");
                MaintainTargetPositionOffset =
                    ReadBool(data, "maintainTargetPositionOffset", "m_MaintainTargetPositionOffset");
                MaintainTargetRotationOffset =
                    ReadBool(data, "maintainTargetRotationOffset", "m_MaintainTargetRotationOffset");
            }

            public bool Equals(ChainIkValues other)
            {
                return string.Equals(RootName, other.RootName, StringComparison.Ordinal) &&
                       string.Equals(TipName, other.TipName, StringComparison.Ordinal) &&
                       string.Equals(TargetName, other.TargetName, StringComparison.Ordinal) &&
                       Weight.Equals(other.Weight) &&
                       ChainRotationWeight.Equals(other.ChainRotationWeight) &&
                       TipRotationWeight.Equals(other.TipRotationWeight) &&
                       MaxIterations == other.MaxIterations &&
                       Tolerance.Equals(other.Tolerance) &&
                       MaintainTargetPositionOffset == other.MaintainTargetPositionOffset &&
                       MaintainTargetRotationOffset == other.MaintainTargetRotationOffset;
            }
        }

        private sealed class ChainIkBaseline
        {
            private readonly Component component;
            internal readonly string Path;
            internal readonly ChainIkValues Values;

            internal ChainIkBaseline(Component component, string path)
            {
                this.component = component;
                Path = path;
                Values = new ChainIkValues(component);
            }

            internal bool TryReadCurrent(out ChainIkValues current)
            {
                if (component == null)
                {
                    current = default;
                    return false;
                }

                current = new ChainIkValues(component);
                return true;
            }
        }

        private sealed class RigWeightBaseline
        {
            private readonly Component component;
            internal readonly string Path;
            internal readonly float Weight;

            internal RigWeightBaseline(Component component, string path)
            {
                this.component = component;
                Path = path;
                Weight = ReadFloat(component, "weight", "m_Weight");
            }

            internal bool TryReadCurrent(out float currentWeight)
            {
                if (component == null)
                {
                    currentWeight = float.NaN;
                    return false;
                }

                currentWeight = ReadFloat(component, "weight", "m_Weight");
                return true;
            }
        }

        private readonly struct RigLayerValues : IEquatable<RigLayerValues>
        {
            internal readonly string RigName;
            internal readonly bool Active;
            internal readonly float Weight;

            internal RigLayerValues(object layer)
            {
                Component rig = ReadMember(layer, "rig", "m_Rig") as Component;
                RigName = rig != null ? rig.name : "<null>";
                Active = ReadBool(layer, "active", "m_Active");
                Weight = ReadFloat(rig, "weight", "m_Weight");
            }

            public bool Equals(RigLayerValues other)
            {
                return string.Equals(RigName, other.RigName, StringComparison.Ordinal) &&
                       Active == other.Active &&
                       Weight.Equals(other.Weight);
            }
        }

        private sealed class RigLayerBaseline
        {
            private readonly object layer;
            internal readonly string Path;
            internal readonly RigLayerValues Values;

            internal RigLayerBaseline(object layer, string path)
            {
                this.layer = layer;
                Path = path;
                Values = new RigLayerValues(layer);
            }

            internal bool TryReadCurrent(out RigLayerValues current)
            {
                if (layer == null)
                {
                    current = default;
                    return false;
                }

                current = new RigLayerValues(layer);
                return true;
            }
        }
    }

    [DefaultExecutionOrder(32000)]
    internal sealed class InteractionAnimationRestoreDiagnosticsRunner : MonoBehaviour
    {
        private void LateUpdate()
        {
            InteractionAnimationApiRestoreDiagnostics.LateUpdate();
        }
    }

    [HarmonyPatch(typeof(PlayerControllerB), "Awake")]
    internal static class InteractionAnimationAuthoredThirdPersonRigCapturePatch
    {
        [HarmonyPrefix]
        [HarmonyPriority(Priority.First)]
        private static void CaptureBeforePlayerScriptsRun(PlayerControllerB __instance)
        {
            InteractionAnimationApiRestoreDiagnostics
                .CapturePristineThirdPersonRigPoseAtPlayerAwake(__instance);
            InteractionAnimationApiRestoreDiagnostics
                .CapturePristineCameraChainPoseAtPlayerAwake(__instance);
            InteractionAnimationApiRestoreDiagnostics
                .LogIkBakeProbeAtPlayerAwake(__instance);
        }
    }
}
