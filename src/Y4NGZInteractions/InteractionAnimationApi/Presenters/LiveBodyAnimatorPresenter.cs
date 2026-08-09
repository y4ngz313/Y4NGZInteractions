using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using Stopwatch = System.Diagnostics.Stopwatch;
using BepInEx;
using BepInEx.Logging;
using UnityEngine;
using UnityEngine.Rendering;
using Y4NGZInteractions.InteractionAnimationApi.Authoring;

namespace Y4NGZInteractions.InteractionAnimationApi.Presenters
{
    /// <summary>
    /// LC-native first-person presenter: plays an authored LC-metarig controller directly on
    /// <c>PlayerControllerB.playerBodyAnimator</c>, the same animator vanilla uses for held-item
    /// and emote animations. The visible arms stay LC's real <c>thisPlayerModelArms</c>, kept
    /// camera-matched by vanilla code. No duplicate rig, no per-frame transform copying.
    /// </summary>
    internal sealed class LiveBodyAnimatorPresenter : IInteractionPresenter
    {
        private const float CameraDisplacementGuardThreshold = 1.25f;
        private const float CameraRotationResidueThresholdDegrees = 0.02f;
        private const float CameraDriftHealThresholdMeters = 0.02f;
        // Vanilla crouched viewpoint: player-local camera Y ~1.17 versus the standing rest
        // height carried in VanillaCameraPlayerLocalRestExpectation.y (2.35).
        private const float VanillaCameraCrouchedPlayerLocalRestHeight = 1.17f;
        private const float StanceViewpointHeightToleranceMeters = 0.15f;
        // Vanilla glides the camera between the stand and crouch heights over roughly a
        // quarter second after isCrouching flips, so the mismatch must persist well past that
        // glide before it is treated as a desynced viewpoint rather than a transition in
        // flight. Counted in guard evaluations (one per Tick).
        private const int StanceViewpointMismatchTicksRequired = 30;
        private const string VanillaStartCrouchingTrigger = "startCrouching";
        private const string VanillaCrouchingBool = "crouching";
        private const string CameraDisplacementGuardBaselineSource =
            "try_start_pre_controller";
        private static readonly Vector3 VanillaCameraPlayerLocalRestExpectation =
            new Vector3(0f, 2.35f, 0.01f);
        private static readonly Vector3 VanillaCameraContainerLocalRestEuler =
            new Vector3(90f, 359.8182f, 0f);

        // Keep animation bundles resident after their first use. Re-loading and decompressing
        // the gun controller + clip pack synchronously in EquipItem measured 378-398 ms in the
        // game log. AssetBundle.GetAllLoadedAssetBundles lets later sessions reuse these; this
        // set gives plugin shutdown one explicit owner for unloading them.
        private static readonly HashSet<AssetBundle> RetainedBundles = new HashSet<AssetBundle>();
        private static bool visorHierarchyLogged;
        private readonly List<RigBuilderState> suppressedRigBuilders = new List<RigBuilderState>();
        private readonly List<ExternalCameraRendererProbe> externalCameraRendererProbes =
            new List<ExternalCameraRendererProbe>();

        private InteractionAnimationContext context;
        private Animator bodyAnimator;
        private AssetBundle bundle;
        private bool ownsBundle;
        private AssetBundle clipPackBundle;
        private bool ownsClipPackBundle;
        private RuntimeAnimatorController appliedController;
        private AnimatorStateSnapshot snapshot;
        private TransformPoseSnapshot rigControlPoseSnapshot;
        private Transform rigControlRoot;
        private InteractionAnimationApiRestoreDiagnostics.ThirdPersonRigPoseSnapshot
            thirdPersonRigControlPoseSnapshot;
        private TransformPoseSnapshot scopedFirstPersonPoseSnapshot;
        private bool rigEvaluateMethodMissingLogged;
        private int fullBodyLayerIndex = -1;
        private int firstPersonLayerIndex = -1;
        private float elapsedSeconds;
        private float nextDiagnosticsAtSeconds;
        private float nextTransformChainDiagnosticsAtSeconds;
        private int lastTransformChainDiagnosticsFrame = -1;
        private bool transformChainDiagnosticsSubscribed;
        private bool externalCameraPresentationDiagnosticsSubscribed;
        private int lastExternalCameraPresentationFrame = -1;
        private int lastExternalCameraPresentationSignature;
        private bool hasExternalCameraPresentationSignature;
        private bool externalCameraPresentationSampleFailureLogged;
        private bool visorHardGlueSubscribed;
        private Transform visorHardGlueVisor;
        private Transform visorHardGlueTarget;
        private bool visorHardGlueAppliedLogged;
        private bool visorHardGlueParkedLogged;
        private Transform rightArmIkTarget;
        private Transform leftArmIkTarget;
        private Transform rightHandBone;
        private Transform rightShoulderBone;
        private GameObject propInstance;
        private bool propReleased;
        private bool exitRequested;
        private float exitElapsedSeconds;
        private float exitDurationSeconds;
        private float exitStartFullBodyWeight;
        private float exitStartFirstPersonWeight;
        private int lastMovementValue = -1;
        private Vector3 cameraPlayerLocalPositionAtStart;
        private bool hasCameraPlayerLocalBaseline;
        private float gameplayCameraLocalYawAtStart;
        private float gameplayCameraLocalRollAtStart;
        private bool hasGameplayCameraLocalRotationBaseline;
        private float cameraBaselineDisplacementFromVanillaRest;
        private bool cameraGuardPreExistingDisplacementDetected;
        private bool cameraGuardBaselineContaminated;
        private bool consumerOwnedCameraLogged;
        private bool cameraGuardPreExistingSuppressionLogged;
        private bool cameraGuardVanillaRestEnvelopeSuppressionLogged;
        private bool cameraGuardEvaluationUnavailableLogged;
        private int stanceViewpointMismatchTicks;
        private bool stanceViewpointLastCrouchState;
        private bool hasStanceViewpointLastCrouchState;
        private bool stanceViewpointGuardExemptLogged;
        private bool hasLastSyncedCrouchState;
        private bool lastSyncedCrouchState;
        private int lastLocomotionSyncSignature = -1;
        private LocalCameraPositionStabilizer cameraPositionStabilizer;
        private LocalCameraRotationStabilizer cameraRotationStabilizer;
        private bool specialAnimationAutoStopExemptLogged;
        private InteractionAnimationStopReason? requestedStopReason;
        private bool active;

        public InteractionAnimationStopReason? RequestedStopReason => requestedStopReason;

        public bool HasResourceOwnership => active && bodyAnimator != null &&
            appliedController != null &&
            bodyAnimator.runtimeAnimatorController == appliedController;

        private ManualLogSource RestoreLogger =>
            context?.Logger ?? InteractionAnimationApiRestoreDiagnostics.StaticLogger;

        internal static bool TryPreloadBundles(
            InteractionAnimationManifest manifest,
            string assetRootPath,
            ManualLogSource logger,
            out string reason)
        {
            reason = string.Empty;
            InteractionAnimationManifest.BodyManifest body = manifest?.body;
            if (body == null || !body.enabled)
                return true;

            if (!TryPreloadBundle(manifest.bundleInternalName, body.bundleFileName, assetRootPath,
                    "controller", out AssetBundle controllerBundle, out reason))
                return false;

            RuntimeAnimatorController controller = null;
            if (!string.IsNullOrWhiteSpace(body.controllerAssetName))
                controller = controllerBundle.LoadAsset<RuntimeAnimatorController>(body.controllerAssetName);
            if (controller == null)
            {
                reason = "live_body.preload_controller_missing:" + body.controllerAssetName;
                return false;
            }

            InteractionAnimationManifest.ClipPackManifest pack = body.clipPack;
            AssetBundle propBundle = controllerBundle;
            if (pack != null && pack.enabled)
            {
                if (!TryPreloadBundle(pack.bundleInternalName, pack.bundleFileName, assetRootPath,
                        "clip_pack", out AssetBundle clipBundle, out reason))
                    return false;
                propBundle = clipBundle;

                if (pack.overrides != null)
                {
                    for (int i = 0; i < pack.overrides.Length; i++)
                    {
                        string clipName = pack.overrides[i]?.clip;
                        if (string.IsNullOrWhiteSpace(clipName))
                            continue;
                        if (clipBundle.LoadAsset<AnimationClip>(clipName) == null)
                        {
                            reason = "live_body.preload_clip_missing:" + clipName;
                            return false;
                        }
                    }
                }

            }

            if (body.prop != null && body.prop.enabled &&
                !string.IsNullOrWhiteSpace(body.prop.prefabAssetName) &&
                propBundle.LoadAsset<GameObject>(body.prop.prefabAssetName) == null)
            {
                reason = "live_body.preload_prop_missing:" + body.prop.prefabAssetName;
                return false;
            }

            logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.preloaded: " +
                $"interaction='{manifest.interactionId}' controller='{controller.name}'.");
            return true;
        }

        private static bool TryPreloadBundle(
            string internalName,
            string fileName,
            string assetRootPath,
            string role,
            out AssetBundle loaded,
            out string reason)
        {
            loaded = null;
            reason = string.Empty;

            // Validate consumer-root confinement before considering a same-name loaded bundle.
            // Cache reuse must never let an escaping manifest path bypass the root contract.
            if (!TryResolveBundlePath(fileName, assetRootPath, out string path, out reason))
            {
                reason = "live_body.preload_" + role + "_bundle_rejected:" + reason;
                return false;
            }

            if (!string.IsNullOrWhiteSpace(internalName))
            {
                foreach (AssetBundle candidate in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (candidate != null &&
                        string.Equals(candidate.name, internalName, StringComparison.OrdinalIgnoreCase))
                    {
                        loaded = candidate;
                        return true;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                reason = "live_body.preload_" + role + "_bundle_missing:" + fileName;
                return false;
            }

            loaded = AssetBundle.LoadFromFile(path);
            if (loaded == null)
            {
                reason = "live_body.preload_" + role + "_bundle_load_failed:" + path;
                return false;
            }

            RetainedBundles.Add(loaded);
            return true;
        }

        public bool TryPreflight(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;
            InteractionAnimationManifest.BodyManifest body = context?.Manifest?.body;
            if (body == null || !body.enabled)
            {
                reason = "missing_body_manifest";
                return false;
            }
            if (context.Request?.Player == null ||
                context.Request.Player.playerBodyAnimator == null)
            {
                reason = "missing_body_animator";
                return false;
            }
            if (!TryPreloadBundles(
                    context.Manifest,
                    context.AssetRootPath,
                    context.Logger,
                    out reason))
            {
                return false;
            }
            if (body.prop != null && body.prop.enabled)
            {
                Transform root = context.Request.Player.playerModelArmsMetarig != null
                    ? context.Request.Player.playerModelArmsMetarig
                    : context.Request.Player.playerBodyAnimator.transform;
                if (ResolvePropAttachBone(root, body.prop) == null)
                {
                    reason = "live_body.prop_attach_bone_missing:" +
                        body.prop.attachBonePath;
                    return false;
                }
            }
            return true;
        }
        public bool TryStart(InteractionAnimationContext context, out string reason)
        {
            reason = string.Empty;

            if (context == null)
            {
                reason = "missing_context";
                return false;
            }

            InteractionAnimationManifest manifest = context.Manifest;
            InteractionAnimationManifest.BodyManifest body = manifest?.body;
            if (body == null || !body.enabled)
            {
                reason = "missing_body_manifest";
                return false;
            }

            if (context.Request?.Player == null || context.Request.Player.playerBodyAnimator == null)
            {
                reason = "missing_body_animator";
                return false;
            }

            this.context = context;
            bodyAnimator = context.Request.Player.playerBodyAnimator;
            // This must be the first session-state capture: the guard reference represents the
            // camera before diagnostics, bundle work, or the authored controller can touch it.
            CaptureLocalCameraBaseline();
            SeamPhaseStopwatch seamTiming =
                InteractionAnimationApiRestoreDiagnostics.RestoreSeamFrameLoggerEnabled
                    ? new SeamPhaseStopwatch()
                    : null;
            InteractionAnimationApiRestoreDiagnostics.PrepareForLiveBodyStart(
                context.Request.Player);
            CaptureRigControlPose();
            CaptureThirdPersonRigControlPose();
            CaptureScopedFirstPersonPose(body);
            StartLocalCameraPositionStabilizer(body);
            StartLocalCameraRotationStabilizer();
            exitRequested = false;
            exitElapsedSeconds = 0f;
            exitDurationSeconds = 0f;
            exitStartFullBodyWeight = 0f;
            exitStartFirstPersonWeight = 0f;
            double setupMs = LapMilliseconds(seamTiming);

            if (!TryLoadBundle(manifest, body, out reason))
            {
                CleanupFailedStart();
                return false;
            }
            double bundleLoadMs = LapMilliseconds(seamTiming);

            InteractionAnimationApiRestoreDiagnostics.NotifyRemoteRigProbeSessionBegin(
                context.Request.Player);
            CameraRotationSnapshot startCameraRotation = CaptureSeamCameraRotation("start");
            double cameraCaptureMs = LapMilliseconds(seamTiming);
            VisorPoseSnapshot startVisorPose = CaptureSeamVisorPose("start");
            double visorCaptureMs = LapMilliseconds(seamTiming);
            if (!TryApplyController(body, out reason))
            {
                CleanupFailedStart();
                return false;
            }
            double controllerSwapMs = LapMilliseconds(seamTiming);

            InteractionAnimationApiRestoreDiagnostics.NotifyLiveBodyAnimationRan(
                context.Request.Player);
            if (!body.rebuildRigBuilders)
                SuppressLiveRigBuilders();
            else
                RebuildRigBuilders("start");
            double rigBuildMs = LapMilliseconds(seamTiming);

            ReapplySeamCameraRotation(startCameraRotation, "start", animatorRestored: true);
            double cameraReapplyMs = LapMilliseconds(seamTiming);
            ReapplySeamVisorPose(startVisorPose, "start", animatorRestored: true);
            double visorReapplyMs = LapMilliseconds(seamTiming);

            // Controller evaluation above happens before a live RigBuilder rebuild. Evaluate
            // once more after the graph exists so no rendered frame observes uninitialized IK.
            try { bodyAnimator.Update(0f); } catch { }
            double animatorUpdateMs = LapMilliseconds(seamTiming);
            if (body.rebuildRigBuilders)
                EvaluateRigBuilders("start");
            double rigEvaluateMs = LapMilliseconds(seamTiming);
            ApplyLocalCameraPositionStabilizerNow();

            if (InteractionAnimationApiRestoreDiagnostics.RestoreSeamFrameLoggerEnabled)
                ResolveDiagnosticTransforms();
            double diagnosticsMs = LapMilliseconds(seamTiming);
            AttachPropIfConfigured();
            double propInstantiateMs = LapMilliseconds(seamTiming);
            InteractionAnimationApiRestoreDiagnostics.NotifyLiveBodyStarted(
                context.Request.Player,
                propInstance);
            elapsedSeconds = 0f;
            nextDiagnosticsAtSeconds = 0f;
            exitRequested = false;
            lastMovementValue = -1;
            requestedStopReason = null;
            active = true;
            StartTransformChainDiagnostics();
            StartExternalCameraPresentationDiagnostics();
            StartLocalVisorHardGlue();
            double finalizeMs = LapMilliseconds(seamTiming);
            if (seamTiming != null)
            {
                context.Logger?.LogInfo(
                    "[RestoreSeam.timing] " +
                    $"phase='start' frame={Time.frameCount} handle={context.Handle} " +
                    $"totalMs={seamTiming.TotalMilliseconds:0.###} setupMs={setupMs:0.###} " +
                    $"bundleLoadMs={bundleLoadMs:0.###} cameraCaptureMs={cameraCaptureMs:0.###} " +
                    $"visorCaptureMs={visorCaptureMs:0.###} " +
                    $"controllerSwapMs={controllerSwapMs:0.###} rigBuildMs={rigBuildMs:0.###} " +
                    $"cameraReapplyMs={cameraReapplyMs:0.###} visorReapplyMs={visorReapplyMs:0.###} " +
                    $"animatorUpdateMs={animatorUpdateMs:0.###} " +
                    $"rigEvaluateMs={rigEvaluateMs:0.###} diagnosticsMs={diagnosticsMs:0.###} " +
                    $"propInstantiateMs={propInstantiateMs:0.###} finalizeMs={finalizeMs:0.###}.");
            }
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.started: " +
                $"handle={context.Handle} interaction='{manifest.interactionId}' " +
                $"controller='{appliedController.name}' animator='{bodyAnimator.name}' " +
                $"fullBodyLayer={fullBodyLayerIndex} firstPersonArmsLayer={firstPersonLayerIndex} " +
                $"rigBuildersSuppressed={suppressedRigBuilders.Count}.");
            return true;
        }

        public void Tick(float deltaTime)
        {
            if (!active || bodyAnimator == null)
                return;

            // Another animation system took ownership after this session started. Yield on the
            // same frame and let Stop's expected-controller guard avoid restoring over it.
            if (appliedController != null && bodyAnimator.runtimeAnimatorController != appliedController)
            {
                requestedStopReason = InteractionAnimationStopReason.PresenterFailure;
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.ownership_lost: " +
                    $"handle={context.Handle} currentController='" +
                    $"{bodyAnimator.runtimeAnimatorController?.name ?? "<none>"}'.");
                return;
            }

            DetectUnsafeCameraDisplacement();
            if (requestedStopReason.HasValue)
                return;

            elapsedSeconds += deltaTime;
            ReleasePropIfDue();
            if (exitRequested)
                exitElapsedSeconds += Mathf.Max(0f, deltaTime);
            ApplyLayerWeights();
            // Vanilla writes "Walking"/"crouching"/"Jumping" only on state transitions, and it
            // enters crouch via the "startCrouching" trigger — which the snapshot machinery
            // never captures. Re-assert them every tick from live player state so a crouch or
            // stand during the session cannot leave the session animator (and the viewpoint it
            // drives) in the stale stance.
            if (IsLocalPlayer(context?.Request?.Player))
                SyncVanillaLocomotionParameters("tick");
            DriveMovementParameter();
            DetectAutoStopConditions();
            LogFrameDiagnostics();
        }

        // Live-body sessions yield to death, ladders, and foreign special animations. Configured
        // interactions may legitimately own the special-animation flag for the whole session.
        private void DetectAutoStopConditions()
        {
            if (requestedStopReason.HasValue)
                return;

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null)
                return;

            try
            {
                if (player.inSpecialInteractAnimation || player.isPlayerDead || player.isClimbingLadder)
                {
                    string interactionId = context?.Manifest?.interactionId;
                    if (player.inSpecialInteractAnimation &&
                        !player.isPlayerDead &&
                        !player.isClimbingLadder &&
                        context?.Manifest?.body?.stopOnVanillaSpecialAnimation == false)
                    {
                        if (!specialAnimationAutoStopExemptLogged)
                        {
                            specialAnimationAutoStopExemptLogged = true;
                            context?.Logger?.LogInfo(
                                "[LCInteractionAnimationAPI] live_body.auto_stop_exempt: " +
                                $"handle={context.Handle} interaction='{interactionId}' " +
                                $"specialAnim={player.inSpecialInteractAnimation} " +
                                "action='continue_exempt'.");
                        }
                        return;
                    }

                    requestedStopReason = player.isPlayerDead
                        ? InteractionAnimationStopReason.PlayerDied
                        : InteractionAnimationStopReason.Interrupted;
                    context?.Logger?.LogInfo(
                        "[LCInteractionAnimationAPI] live_body.auto_stop_requested: " +
                        $"handle={context.Handle} specialAnim={player.inSpecialInteractAnimation} " +
                        $"dead={player.isPlayerDead} ladder={player.isClimbingLadder}.");
                }
            }
            catch { }
        }

        /// <summary>
        /// True when the manifest declares that something outside this session owns the local
        /// camera — the session is itself a third-person presentation. Every camera-ownership
        /// behavior in this presenter exists to keep a first-person camera at rest, so all of
        /// them must stand down rather than fight the external owner for the same transform.
        /// </summary>
        private bool ConsumerOwnsCameraPresentation =>
            context?.Manifest?.body?.preserveGameplayCamera == false;

        private void CaptureLocalCameraBaseline()
        {
            ResetCameraDisplacementGuardState();
            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.baseline_unavailable: " +
                    $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                    "reason='local_camera_owned_externally' " +
                    "action='guard_disabled_for_session'.");
                return;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            bool localPlayer = IsLocalPlayer(player);
            if (player == null || player.transform == null || !localPlayer ||
                player.gameplayCamera == null)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.baseline_unavailable: " +
                    $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                    $"playerPresent={player != null} playerTransformPresent={player?.transform != null} " +
                    $"localPlayer={localPlayer} gameplayCameraPresent={player?.gameplayCamera != null} " +
                    "rotation_residue_available=False pre_existing_rotation_residue=False " +
                    "action='guard_disabled_for_session'.");
                return;
            }

            TryHealCameraDriftAtSessionEntry(player);

            Transform gameplayCameraTransform = player.gameplayCamera.transform;
            cameraPlayerLocalPositionAtStart =
                player.transform.InverseTransformPoint(gameplayCameraTransform.position);
            hasCameraPlayerLocalBaseline = true;
            Vector3 gameplayCameraLocalEuler = gameplayCameraTransform.localEulerAngles;
            gameplayCameraLocalYawAtStart =
                Mathf.DeltaAngle(0f, gameplayCameraLocalEuler.y);
            gameplayCameraLocalRollAtStart =
                Mathf.DeltaAngle(0f, gameplayCameraLocalEuler.z);
            hasGameplayCameraLocalRotationBaseline = true;
            Transform cameraContainer = player.cameraContainerTransform;
            bool cameraContainerAvailable = cameraContainer != null;
            Vector3 cameraContainerLocalEuler = cameraContainerAvailable
                ? cameraContainer.localEulerAngles
                : Vector3.zero;
            Vector3 cameraContainerRestDeviation = cameraContainerAvailable
                ? DescribeEulerDeviation(
                    cameraContainerLocalEuler,
                    VanillaCameraContainerLocalRestEuler)
                : Vector3.zero;
            bool preExistingRotationResidue =
                Mathf.Abs(gameplayCameraLocalYawAtStart) >
                    CameraRotationResidueThresholdDegrees ||
                Mathf.Abs(gameplayCameraLocalRollAtStart) >
                    CameraRotationResidueThresholdDegrees ||
                (cameraContainerAvailable &&
                 (Mathf.Abs(cameraContainerRestDeviation.x) >
                      CameraRotationResidueThresholdDegrees ||
                  Mathf.Abs(cameraContainerRestDeviation.y) >
                      CameraRotationResidueThresholdDegrees ||
                  Mathf.Abs(cameraContainerRestDeviation.z) >
                      CameraRotationResidueThresholdDegrees));
            cameraBaselineDisplacementFromVanillaRest = Vector3.Distance(
                cameraPlayerLocalPositionAtStart,
                VanillaCameraPlayerLocalRestExpectation);
            cameraGuardPreExistingDisplacementDetected =
                cameraBaselineDisplacementFromVanillaRest > CameraDisplacementGuardThreshold;
            cameraGuardBaselineContaminated = cameraGuardPreExistingDisplacementDetected;
            bool baselineCrouching = false;
            try { baselineCrouching = player.isCrouching; } catch { }

            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.baseline_captured: " +
                $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                $"baseline={DescribeVector(cameraPlayerLocalPositionAtStart)} " +
                $"vanilla_rest_expectation={DescribeVector(VanillaCameraPlayerLocalRestExpectation)} " +
                $"displacement_from_vanilla_rest={cameraBaselineDisplacementFromVanillaRest:0.###} " +
                $"threshold={CameraDisplacementGuardThreshold:0.###} " +
                $"baseline_source='{CameraDisplacementGuardBaselineSource}' " +
                $"pre_existing_displacement={cameraGuardPreExistingDisplacementDetected} " +
                $"baseline_contaminated={cameraGuardBaselineContaminated} " +
                "rotation_residue_available=True " +
                $"gameplay_camera_local_yaw={gameplayCameraLocalYawAtStart:0.######} " +
                $"gameplay_camera_local_roll={gameplayCameraLocalRollAtStart:0.######} " +
                $"gameplay_camera_yaw_deviation_from_rest={gameplayCameraLocalYawAtStart:0.######} " +
                $"gameplay_camera_roll_deviation_from_rest={gameplayCameraLocalRollAtStart:0.######} " +
                $"camera_container_local_euler={(cameraContainerAvailable ? DescribeEuler(cameraContainerLocalEuler) : "<unavailable>")} " +
                $"camera_container_rest_euler={DescribeEuler(VanillaCameraContainerLocalRestEuler)} " +
                $"camera_container_deviation_from_rest={(cameraContainerAvailable ? DescribeEuler(cameraContainerRestDeviation) : "<unavailable>")} " +
                $"rotation_residue_threshold_degrees={CameraRotationResidueThresholdDegrees:0.###} " +
                $"pre_existing_rotation_residue={preExistingRotationResidue} " +
                $"baseline_crouching={baselineCrouching} " +
                $"consumerOwnsCamera={ConsumerOwnsCameraPresentation}.");

            if (cameraGuardPreExistingDisplacementDetected)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.pre_existing_displacement: " +
                    $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                    "phase='try_start' " +
                    $"displacement_from_vanilla_rest={cameraBaselineDisplacementFromVanillaRest:0.###} " +
                    $"threshold={CameraDisplacementGuardThreshold:0.###} " +
                    $"baseline={DescribeVector(cameraPlayerLocalPositionAtStart)} " +
                    $"vanilla_rest_expectation={DescribeVector(VanillaCameraPlayerLocalRestExpectation)} " +
                    $"baseline_source='{CameraDisplacementGuardBaselineSource}' " +
                    "pre_existing_displacement=True baseline_contaminated=True " +
                    "action='continue_new_displacement_only'.");
            }
        }

        /// <summary>
        /// Session-start drift heal: earlier live-body sessions (or other mods) can leave the
        /// camera chain with small positional residue that vanilla never rewrites — it derives
        /// container/arms positions from each other every frame, so an injected offset persists
        /// for the rest of the game and every subsequent restore adopts it as the "correct"
        /// baseline. Before capturing this session's baseline, snap the chain back to the
        /// authored defaults when the camera deviates from vanilla rest by more than the heal
        /// threshold. Displacements beyond the guard threshold are left to the displacement
        /// guard's own machinery, and legitimately-displaced states (crouch, special interact
        /// animations) are never healed.
        /// </summary>
        private void TryHealCameraDriftAtSessionEntry(
            GameNetcodeStuff.PlayerControllerB player)
        {
            if (!InteractionAnimationApiRestoreDiagnostics.HealCameraDriftAtSessionStartEnabled)
                return;

            try
            {
                Vector3 cameraPlayerLocal = player.transform.InverseTransformPoint(
                    player.gameplayCamera.transform.position);
                float displacement = Vector3.Distance(
                    cameraPlayerLocal,
                    VanillaCameraPlayerLocalRestExpectation);
                if (displacement <= CameraDriftHealThresholdMeters)
                {
                    // Verified-clean entry: upgrade the rest baseline from the authored prefab
                    // pose to the runtime-settled pose so later heals and stop snaps target
                    // vanilla's true rest instead of breathing a few millimeters against it.
                    TryRefineCameraChainRestBaselineAtCleanEntry(player, displacement);
                    // Same verified-clean idle gate: replace an Awake-time third-person rig
                    // baseline that was flagged implausible with a runtime-settled recapture.
                    if (TryGetVerifiedCleanIdleState(
                            player,
                            out _,
                            out _,
                            out _))
                    {
                        InteractionAnimationApiRestoreDiagnostics
                            .TryRecapturePristineThirdPersonRigPoseIfImplausible(
                                player,
                                out _);
                    }
                    return;
                }

                if (displacement > CameraDisplacementGuardThreshold)
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.camerachain] heal_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"displacement={displacement:0.###} " +
                        $"threshold={CameraDriftHealThresholdMeters:0.###} " +
                        $"guardThreshold={CameraDisplacementGuardThreshold:0.###} " +
                        "reason='beyond_guard_threshold' action='defer_to_displacement_guard'.");
                    return;
                }

                bool crouching = false;
                bool specialAnimation = false;
                try
                {
                    crouching = player.isCrouching;
                    specialAnimation = player.inSpecialInteractAnimation;
                }
                catch { }
                if (crouching || specialAnimation)
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.camerachain] heal_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"displacement={displacement:0.###} crouching={crouching} " +
                        $"specialAnimation={specialAnimation} " +
                        "reason='legitimately_displaced_state' action='leave_camera_chain'.");
                    return;
                }

                if (!InteractionAnimationApiRestoreDiagnostics
                        .TryRestorePristineCameraChainPositions(
                            player,
                            out int restored,
                            out string reason,
                            out string baselineSource))
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.camerachain] heal_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"displacement={displacement:0.###} " +
                        $"reason='{reason}' action='leave_camera_chain'.");
                    return;
                }

                Vector3 healedPlayerLocal = player.transform.InverseTransformPoint(
                    player.gameplayCamera.transform.position);
                float healedDisplacement = Vector3.Distance(
                    healedPlayerLocal,
                    VanillaCameraPlayerLocalRestExpectation);
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerachain] heal_applied: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    $"interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                    $"restoredTransforms={restored} " +
                    $"beforePlayerLocal={DescribeVector(cameraPlayerLocal)} " +
                    $"afterPlayerLocal={DescribeVector(healedPlayerLocal)} " +
                    $"displacementBefore={displacement:0.###} " +
                    $"displacementAfter={healedDisplacement:0.###} " +
                    $"source='{baselineSource}'.");
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.camerachain] heal_failed: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        /// <summary>
        /// One-time rest-baseline upgrade, attempted only at a verified-clean session entry.
        /// Gated to an idle, standing, non-special-animation state so head-bob phase, crouch
        /// height, or scripted poses can never be baked into the rest target. Movement is
        /// checked horizontally: vanilla's grounding logic keeps a small vertical component in
        /// CharacterController.velocity even when the player stands still.
        /// </summary>
        private void TryRefineCameraChainRestBaselineAtCleanEntry(
            GameNetcodeStuff.PlayerControllerB player,
            float displacement)
        {
            if (!TryGetVerifiedCleanIdleState(
                    player,
                    out _,
                    out _,
                    out float horizontalSpeed))
            {
                return;
            }

            if (!InteractionAnimationApiRestoreDiagnostics.TryRefineCameraChainRestBaseline(
                    player,
                    out string reason))
            {
                if (!string.Equals(reason, "already_refined", StringComparison.Ordinal))
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.camerachain] rest_baseline_refine_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"displacement={displacement:0.###} reason='{reason}' " +
                        "action='keep_authored_default'.");
                }
                return;
            }

            context?.Logger?.LogInfo(
                "[RestoreSeam.camerachain] rest_baseline_refine_accepted: " +
                $"frame={Time.frameCount} handle={context.Handle} " +
                $"interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                $"displacement={displacement:0.###} horizontalSpeed={horizontalSpeed:0.###} " +
                "action='snap_targets_now_runtime_settled'.");
        }

        /// <summary>
        /// Verified-clean idle gate shared by the session-entry rest-baseline upgrades: true
        /// only when the player is standing, not in a special animation, and effectively
        /// stationary (horizontal speed at or below 0.1 m/s). Movement is checked horizontally
        /// because vanilla's grounding logic keeps a small vertical component in
        /// CharacterController.velocity even when the player stands still.
        /// </summary>
        private static bool TryGetVerifiedCleanIdleState(
            GameNetcodeStuff.PlayerControllerB player,
            out bool crouching,
            out bool specialAnimation,
            out float horizontalSpeed)
        {
            crouching = false;
            specialAnimation = false;
            horizontalSpeed = -1f;
            try
            {
                crouching = player.isCrouching;
                specialAnimation = player.inSpecialInteractAnimation;
            }
            catch { }
            try
            {
                if (player.thisController != null)
                {
                    Vector3 velocity = player.thisController.velocity;
                    horizontalSpeed = new Vector3(velocity.x, 0f, velocity.z).magnitude;
                }
            }
            catch { }

            return !crouching && !specialAnimation &&
                horizontalSpeed >= 0f && horizontalSpeed <= 0.1f;
        }

        private void ResetCameraDisplacementGuardState()
        {
            hasCameraPlayerLocalBaseline = false;
            cameraPlayerLocalPositionAtStart = Vector3.zero;
            gameplayCameraLocalYawAtStart = 0f;
            gameplayCameraLocalRollAtStart = 0f;
            hasGameplayCameraLocalRotationBaseline = false;
            cameraBaselineDisplacementFromVanillaRest = 0f;
            cameraGuardPreExistingDisplacementDetected = false;
            cameraGuardBaselineContaminated = false;
            consumerOwnedCameraLogged = false;
            cameraGuardPreExistingSuppressionLogged = false;
            cameraGuardVanillaRestEnvelopeSuppressionLogged = false;
            cameraGuardEvaluationUnavailableLogged = false;
            stanceViewpointMismatchTicks = 0;
            stanceViewpointLastCrouchState = false;
            hasStanceViewpointLastCrouchState = false;
            stanceViewpointGuardExemptLogged = false;
        }

        private void StartLocalCameraPositionStabilizer(
            InteractionAnimationManifest.BodyManifest body)
        {
            cameraPositionStabilizer = null;
            if (body == null || !body.preserveGameplayCamera ||
                !hasCameraPlayerLocalBaseline ||
                ConsumerOwnsCameraPresentation)
            {
                return;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || player.transform == null || player.gameplayCamera == null)
                return;

            try
            {
                cameraPositionStabilizer =
                    player.gameplayCamera.gameObject.AddComponent<LocalCameraPositionStabilizer>();
                cameraPositionStabilizer.Initialize(
                    player.transform,
                    player.gameplayCamera.transform,
                    cameraPlayerLocalPositionAtStart);
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.local_camera_stabilizer_started: " +
                    $"handle={context.Handle} playerLocalPosition={cameraPlayerLocalPositionAtStart}.");
            }
            catch (Exception exception)
            {
                cameraPositionStabilizer = null;
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.local_camera_stabilizer_failed: " +
                    $"handle={context.Handle} error='{exception.Message}'.");
            }
        }

        private void ApplyLocalCameraPositionStabilizerNow()
        {
            try { cameraPositionStabilizer?.ApplyNow(); } catch { }
        }

        private void StartLocalCameraRotationStabilizer()
        {
            cameraRotationStabilizer = null;
            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_skipped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "enabled=True reason='local_camera_owned_externally' " +
                    "action='leave_rotation_unpinned'.");
                return;
            }

            if (!InteractionAnimationApiRestoreDiagnostics
                    .StabilizeCameraRotationDuringSessionEnabled)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_skipped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "enabled=False reason='kill_switch_disabled' action='leave_rotation_unpinned'.");
                return;
            }

            if (!hasGameplayCameraLocalRotationBaseline)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_skipped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "enabled=True reason='session_entry_rotation_unavailable' " +
                    "action='leave_rotation_unpinned'.");
                return;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            Transform gameplayCameraTransform =
                player != null && player.gameplayCamera != null
                    ? player.gameplayCamera.transform
                    : null;
            if (gameplayCameraTransform == null)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_skipped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "enabled=True reason='gameplay_camera_missing' " +
                    "action='leave_rotation_unpinned'.");
                return;
            }

            try
            {
                cameraRotationStabilizer =
                    gameplayCameraTransform.GetComponent<LocalCameraRotationStabilizer>();
                if (cameraRotationStabilizer == null)
                {
                    cameraRotationStabilizer =
                        gameplayCameraTransform.gameObject
                            .AddComponent<LocalCameraRotationStabilizer>();
                }

                cameraRotationStabilizer.Initialize(
                    gameplayCameraTransform,
                    gameplayCameraLocalYawAtStart,
                    gameplayCameraLocalRollAtStart);
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_started: " +
                    $"handle={context.Handle} enabled=True " +
                    $"sessionEntryLocalYaw={gameplayCameraLocalYawAtStart:0.######} " +
                    $"sessionEntryLocalRoll={gameplayCameraLocalRollAtStart:0.######} " +
                    "pitchOwner='vanilla_live_x' yZSource='session_entry_absolute'.");
            }
            catch (Exception exception)
            {
                cameraRotationStabilizer = null;
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_skipped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"enabled=True reason='start_failed:{exception.Message}' " +
                    "action='leave_rotation_unpinned'.");
            }
        }

        private void StopLocalCameraRotationStabilizer(bool restoreSessionEntryRotation)
        {
            LocalCameraRotationStabilizer stabilizer = cameraRotationStabilizer;
            cameraRotationStabilizer = null;
            if (stabilizer == null)
                return;

            try
            {
                if (restoreSessionEntryRotation)
                    stabilizer.ApplyNow();
                stabilizer.enabled = false;
                UnityEngine.Object.Destroy(stabilizer);
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_stopped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"restoredSessionEntryRotation={restoreSessionEntryRotation}.");
            }
            catch (Exception exception)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.camera_rotation_stabilizer_stop_failed: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        private void StartRestoreScopedCameraPositionStabilizer()
        {
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreScopedCameraPinEnabled)
                return;

            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerapin] pin_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='local_camera_owned_externally' action='leave_camera_unpinned'.");
                return;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || player.transform == null || player.gameplayCamera == null)
                return;

            try
            {
                GameNetcodeStuff.PlayerControllerB localPlayer =
                    GameNetworkManager.Instance != null
                        ? GameNetworkManager.Instance.localPlayerController
                        : null;
                if (localPlayer == null && StartOfRound.Instance != null)
                    localPlayer = StartOfRound.Instance.localPlayerController;
                if (!ReferenceEquals(player, localPlayer))
                    return;

                Vector3 stopEntryPlayerLocalPosition =
                    player.transform.InverseTransformPoint(
                        player.gameplayCamera.transform.position);
                if (cameraPositionStabilizer == null)
                {
                    cameraPositionStabilizer =
                        player.gameplayCamera.gameObject.AddComponent<LocalCameraPositionStabilizer>();
                }

                cameraPositionStabilizer.Initialize(
                    player.transform,
                    player.gameplayCamera.transform,
                    stopEntryPlayerLocalPosition);
                context?.Logger?.LogInfo(
                    "[RestoreSeam.pin] pin_started: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    $"playerLocalPosition={stopEntryPlayerLocalPosition}.");
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.pin] pin_failed: " +
                    $"frame={Time.frameCount} handle=" +
                    $"{(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        private void StopLocalCameraPositionStabilizer(bool restorePosition, bool deferRelease)
        {
            LocalCameraPositionStabilizer stabilizer = cameraPositionStabilizer;
            cameraPositionStabilizer = null;
            if (stabilizer == null)
                return;

            try
            {
                if (restorePosition)
                    stabilizer.ApplyNow();
                if (deferRelease)
                    stabilizer.ReleaseAfterLateUpdates(2);
                else
                {
                    stabilizer.enabled = false;
                    UnityEngine.Object.Destroy(stabilizer);
                }
            }
            catch { }
        }

        private CameraRotationSnapshot CaptureSeamCameraRotation(string phase)
        {
            bool stopSnapToRest =
                string.Equals(phase, "stop", StringComparison.Ordinal) &&
                InteractionAnimationApiRestoreDiagnostics.RestoreCameraRotationSnapToRestEnabled;
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreCameraRotationEnabled &&
                !stopSnapToRest)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='disabled'.");
                return default;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='missing_player'.");
                return default;
            }

            if (!IsLocalPlayer(player))
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='not_local_player'.");
                return default;
            }

            Transform gameplayCameraTransform = null;
            Transform cameraContainerTransform = null;
            Quaternion gameplayCameraLocalRotation = Quaternion.identity;
            Quaternion cameraContainerLocalRotation = Quaternion.identity;
            bool gameplayCameraCaptured = false;
            bool cameraContainerCaptured = false;

            try
            {
                gameplayCameraTransform = player.gameplayCamera != null
                    ? player.gameplayCamera.transform
                    : null;
                if (gameplayCameraTransform != null)
                {
                    gameplayCameraLocalRotation = gameplayCameraTransform.localRotation;
                    gameplayCameraCaptured = true;
                }
                else
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.camerarotation] capture_target_missing: " +
                        $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                        "target='gameplayCamera'.");
                }
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.camerarotation] capture_target_failed: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='gameplayCamera' error='{exception.Message}'.");
            }

            try
            {
                cameraContainerTransform = player.cameraContainerTransform;
                if (cameraContainerTransform != null)
                {
                    cameraContainerLocalRotation = cameraContainerTransform.localRotation;
                    cameraContainerCaptured = true;
                }
                else
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.camerarotation] capture_target_missing: " +
                        $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                        "target='cameraContainerTransform'.");
                }
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.camerarotation] capture_target_failed: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='cameraContainerTransform' error='{exception.Message}'.");
            }

            if (!gameplayCameraCaptured && !cameraContainerCaptured)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    "reason='no_rotation_targets'.");
                return default;
            }

            context?.Logger?.LogInfo(
                "[RestoreSeam.camerarotation] captured: " +
                $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                $"gameplayCameraLocalEuler={DescribeCapturedEuler(gameplayCameraCaptured, gameplayCameraLocalRotation)} " +
                $"cameraContainerLocalEuler={DescribeCapturedEuler(cameraContainerCaptured, cameraContainerLocalRotation)}.");
            return new CameraRotationSnapshot(
                gameplayCameraTransform,
                gameplayCameraLocalRotation,
                gameplayCameraCaptured,
                cameraContainerTransform,
                cameraContainerLocalRotation,
                cameraContainerCaptured);
        }

        private void ReapplySeamCameraRotation(
            CameraRotationSnapshot captured,
            string phase,
            bool animatorRestored)
        {
            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='local_camera_owned_externally'.");
                return;
            }

            bool stopPhase = string.Equals(phase, "stop", StringComparison.Ordinal);
            bool stopSnapToRest =
                stopPhase &&
                InteractionAnimationApiRestoreDiagnostics.RestoreCameraRotationSnapToRestEnabled;
            if (!animatorRestored)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='animator_not_restored'.");
                return;
            }

            if (!InteractionAnimationApiRestoreDiagnostics.RestoreCameraRotationEnabled &&
                !stopSnapToRest)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='disabled'.");
                return;
            }

            if (!captured.HasAnyRotation)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='capture_unavailable'.");
                return;
            }

            bool gameplayCameraApplied = false;
            bool cameraContainerApplied = false;
            string gameplayCameraEuler = captured.GameplayCameraCaptured ? "<target_missing>" : "<not_captured>";
            string cameraContainerEuler = captured.CameraContainerCaptured ? "<target_missing>" : "<not_captured>";
            Vector3 capturedGameplayCameraEuler = captured.GameplayCameraCaptured
                ? captured.GameplayCameraLocalRotation.eulerAngles
                : Vector3.zero;
            float discardedGameplayCameraYaw = captured.GameplayCameraCaptured
                ? Mathf.DeltaAngle(0f, capturedGameplayCameraEuler.y)
                : 0f;
            float discardedGameplayCameraRoll = captured.GameplayCameraCaptured
                ? Mathf.DeltaAngle(0f, capturedGameplayCameraEuler.z)
                : 0f;
            Vector3 capturedCameraContainerEuler = captured.CameraContainerCaptured
                ? captured.CameraContainerLocalRotation.eulerAngles
                : Vector3.zero;
            Vector3 discardedCameraContainerDeviation = captured.CameraContainerCaptured
                ? DescribeEulerDeviation(
                    capturedCameraContainerEuler,
                    VanillaCameraContainerLocalRestEuler)
                : Vector3.zero;

            if (stopPhase)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerarotation] stop_restore_gate: " +
                    $"phase='stop' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"restoreCameraRotation={InteractionAnimationApiRestoreDiagnostics.RestoreCameraRotationEnabled} " +
                    $"snapToRest={stopSnapToRest} " +
                    $"gameplayCameraCaptured={captured.GameplayCameraCaptured} " +
                    $"discardedGameplayCameraLocalYaw={discardedGameplayCameraYaw:0.######} " +
                    $"discardedGameplayCameraLocalRoll={discardedGameplayCameraRoll:0.######} " +
                    $"cameraContainerCaptured={captured.CameraContainerCaptured} " +
                    $"discardedCameraContainerDeviationFromRest=" +
                    $"{(captured.CameraContainerCaptured ? DescribeEuler(discardedCameraContainerDeviation) : "<not_captured>")} " +
                    $"cameraContainerRestEuler={DescribeEuler(VanillaCameraContainerLocalRestEuler)} " +
                    $"action='{(stopSnapToRest ? "preserve_pitch_snap_yaw_roll_and_container_to_rest" : "reapply_stop_entry_rotation")}'.");
            }

            if (captured.GameplayCameraCaptured)
            {
                if (captured.GameplayCameraTransform == null)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.camerarotation] reapply_target_missing: " +
                        $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                        "target='gameplayCamera'.");
                }
                else
                {
                    try
                    {
                        captured.GameplayCameraTransform.localRotation = stopSnapToRest
                            ? Quaternion.Euler(capturedGameplayCameraEuler.x, 0f, 0f)
                            : captured.GameplayCameraLocalRotation;
                        gameplayCameraEuler =
                            DescribeEuler(captured.GameplayCameraTransform.localEulerAngles);
                        gameplayCameraApplied = true;
                    }
                    catch (Exception exception)
                    {
                        context?.Logger?.LogWarning(
                            "[RestoreSeam.camerarotation] reapply_target_failed: " +
                            $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                            $"target='gameplayCamera' error='{exception.Message}'.");
                    }
                }
            }

            if (captured.CameraContainerCaptured)
            {
                if (captured.CameraContainerTransform == null)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.camerarotation] reapply_target_missing: " +
                        $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                        "target='cameraContainerTransform'.");
                }
                else
                {
                    try
                    {
                        if (stopSnapToRest)
                        {
                            captured.CameraContainerTransform.localEulerAngles =
                                VanillaCameraContainerLocalRestEuler;
                        }
                        else
                        {
                            captured.CameraContainerTransform.localRotation =
                                captured.CameraContainerLocalRotation;
                        }
                        cameraContainerEuler =
                            DescribeEuler(captured.CameraContainerTransform.localEulerAngles);
                        cameraContainerApplied = true;
                    }
                    catch (Exception exception)
                    {
                        context?.Logger?.LogWarning(
                            "[RestoreSeam.camerarotation] reapply_target_failed: " +
                            $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                            $"target='cameraContainerTransform' error='{exception.Message}'.");
                    }
                }
            }

            if (!gameplayCameraApplied && !cameraContainerApplied)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.camerarotation] reapply_failed: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    "reason='no_rotation_targets_applied'.");
                return;
            }

            context?.Logger?.LogInfo(
                "[RestoreSeam.camerarotation] reapplied: " +
                $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                $"snapToRest={stopSnapToRest} " +
                $"gameplayCameraApplied={gameplayCameraApplied} gameplayCameraLocalEuler={gameplayCameraEuler} " +
                $"cameraContainerApplied={cameraContainerApplied} cameraContainerLocalEuler={cameraContainerEuler}.");
        }

        private VisorPoseSnapshot CaptureSeamVisorPose(string phase)
        {
            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='local_camera_owned_externally' action='leave_visor_to_external_owner'.");
                return default;
            }

            if (!InteractionAnimationApiRestoreDiagnostics.RestoreVisorPoseEnabled)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='disabled'.");
                return default;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='missing_player'.");
                return default;
            }

            if (!IsLocalPlayer(player))
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='not_local_player'.");
                return default;
            }

            Transform animatorRoot = bodyAnimator != null
                ? bodyAnimator.transform
                : player.playerBodyAnimator != null
                    ? player.playerBodyAnimator.transform
                    : null;
            Transform localVisor = null;
            Transform localVisorTargetPoint = null;
            bool localVisorReadFailed = false;
            bool targetPointReadFailed = false;

            try { localVisor = player.localVisor; }
            catch (Exception exception)
            {
                localVisorReadFailed = true;
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='localVisor' reason='field_read_failed' error='{exception.Message}'.");
            }

            try { localVisorTargetPoint = player.localVisorTargetPoint; }
            catch (Exception exception)
            {
                targetPointReadFailed = true;
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='localVisorTargetPoint' reason='field_read_failed' error='{exception.Message}'.");
            }

            LogVisorHierarchyOnce(
                animatorRoot,
                localVisor,
                localVisorTargetPoint,
                phase);

            SeamTransformPose localVisorPose = default;
            SeamTransformPose targetPointPose = default;
            if (!localVisorReadFailed)
            {
                TryCaptureSeamVisorTarget(
                    localVisor,
                    animatorRoot,
                    "localVisor",
                    phase,
                    out localVisorPose);
            }
            if (!targetPointReadFailed)
            {
                TryCaptureSeamVisorTarget(
                    localVisorTargetPoint,
                    animatorRoot,
                    "localVisorTargetPoint",
                    phase,
                    out targetPointPose);
            }

            var captured = new VisorPoseSnapshot(localVisorPose, targetPointPose);
            if (!captured.HasAnyPose)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    "reason='no_pose_targets'.");
                return default;
            }

            if (!captured.HasAnyRestoreEligiblePose)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    "reason='no_targets_under_animator_hierarchy'.");
                return default;
            }

            context?.Logger?.LogInfo(
                "[RestoreSeam.visor] captured: " +
                $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                $"localVisor={DescribeCapturedSeamPose(captured.LocalVisor)} " +
                $"localVisorTargetPoint={DescribeCapturedSeamPose(captured.LocalVisorTargetPoint)}.");
            return captured;
        }

        private bool TryCaptureSeamVisorTarget(
            Transform target,
            Transform animatorRoot,
            string targetName,
            string phase,
            out SeamTransformPose captured)
        {
            captured = default;
            if (target == null)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='{targetName}' reason='missing_transform'.");
                return false;
            }

            try
            {
                bool underAnimatorHierarchy =
                    animatorRoot != null &&
                    (ReferenceEquals(target, animatorRoot) || target.IsChildOf(animatorRoot));
                captured = new SeamTransformPose(target, underAnimatorHierarchy);
                return true;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] capture_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='{targetName}' reason='capture_failed' error='{exception.Message}'.");
                return false;
            }
        }

        private void ReapplySeamVisorPose(
            VisorPoseSnapshot captured,
            string phase,
            bool animatorRestored)
        {
            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='local_camera_owned_externally' action='leave_visor_to_external_owner'.");
                return;
            }

            if (!animatorRestored)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='animator_not_restored'.");
                return;
            }

            if (!InteractionAnimationApiRestoreDiagnostics.RestoreVisorPoseEnabled)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='disabled'.");
                return;
            }

            if (!captured.HasAnyPose)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='capture_unavailable'.");
                return;
            }

            bool targetPointApplied = TryReapplySeamVisorTarget(
                captured.LocalVisorTargetPoint,
                "localVisorTargetPoint",
                phase,
                restoreWorldPose: false);
            bool localVisorApplied = TryReapplySeamVisorTarget(
                captured.LocalVisor,
                "localVisor",
                phase,
                restoreWorldPose: true);
            bool vanillaGlueApplied = false;

            if (captured.LocalVisor.Captured && captured.LocalVisorTargetPoint.Captured &&
                captured.LocalVisor.Transform != null &&
                captured.LocalVisorTargetPoint.Transform != null)
            {
                try
                {
                    // Vanilla snaps visor position every LateUpdate, but rotation deliberately
                    // trails the target through a Lerp. Preserve that pre-seam trailing rotation
                    // exactly instead of advancing it an extra step or snapping to the target.
                    captured.LocalVisor.Transform.position =
                        captured.LocalVisorTargetPoint.Transform.position;
                    captured.LocalVisor.Transform.rotation =
                        captured.LocalVisor.WorldRotation;
                    vanillaGlueApplied = true;
                }
                catch (Exception exception)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.visor] reapply_skipped: " +
                        $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                        $"target='vanillaVisorGlue' reason='apply_failed' error='{exception.Message}'.");
                }
            }
            else
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    "target='vanillaVisorGlue' reason='missing_captured_transform'.");
            }

            if (!targetPointApplied && !localVisorApplied && !vanillaGlueApplied)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    "reason='no_targets_applied'.");
                return;
            }

            context?.Logger?.LogInfo(
                "[RestoreSeam.visor] reapplied: " +
                $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                $"localVisorApplied={localVisorApplied} " +
                $"localVisor={DescribeCurrentSeamPose(captured.LocalVisor.Transform)} " +
                $"localVisorTargetPointApplied={targetPointApplied} " +
                $"localVisorTargetPoint={DescribeCurrentSeamPose(captured.LocalVisorTargetPoint.Transform)} " +
                $"vanillaGlueApplied={vanillaGlueApplied} " +
                $"preservedVisorWorldEuler={DescribeEuler(captured.LocalVisor.WorldRotation.eulerAngles)}.");
        }

        private bool TryReapplySeamVisorTarget(
            SeamTransformPose captured,
            string targetName,
            string phase,
            bool restoreWorldPose)
        {
            if (!captured.Captured)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='{targetName}' reason='capture_unavailable'.");
                return false;
            }

            if (!captured.UnderAnimatorHierarchy)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='{targetName}' reason='not_under_animator_hierarchy'.");
                return false;
            }

            if (captured.Transform == null)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='{targetName}' reason='target_missing'.");
                return false;
            }

            try
            {
                captured.Transform.localPosition = captured.LocalPosition;
                captured.Transform.localRotation = captured.LocalRotation;
                captured.Transform.localScale = captured.LocalScale;
                if (restoreWorldPose)
                {
                    captured.Transform.SetPositionAndRotation(
                        captured.WorldPosition,
                        captured.WorldRotation);
                }
                return true;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] reapply_skipped: " +
                    $"phase='{phase}' frame={Time.frameCount} handle={context.Handle} " +
                    $"target='{targetName}' reason='apply_failed' error='{exception.Message}'.");
                return false;
            }
        }

        private void LogVisorHierarchyOnce(
            Transform animatorRoot,
            Transform localVisor,
            Transform localVisorTargetPoint,
            string phase)
        {
            if (visorHierarchyLogged)
                return;

            visorHierarchyLogged = true;
            try
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.visor] hierarchy: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"animatorRootPath='{DescribeHierarchyPath(animatorRoot)}' " +
                    $"localVisorPath='{DescribeHierarchyPath(localVisor)}' " +
                    $"localVisorUnderAnimatorHierarchy={IsUnderHierarchy(localVisor, animatorRoot)} " +
                    $"localVisorTargetPointPath='{DescribeHierarchyPath(localVisorTargetPoint)}' " +
                    $"localVisorTargetPointUnderAnimatorHierarchy={IsUnderHierarchy(localVisorTargetPoint, animatorRoot)}.");
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.visor] hierarchy_failed: " +
                    $"phase='{phase}' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        private static bool IsUnderHierarchy(Transform target, Transform root)
        {
            try
            {
                return target != null && root != null &&
                    (ReferenceEquals(target, root) || target.IsChildOf(root));
            }
            catch
            {
                return false;
            }
        }

        private static string DescribeHierarchyPath(Transform transform)
        {
            if (transform == null)
                return "<missing>";

            try
            {
                var names = new List<string>();
                Transform current = transform;
                while (current != null)
                {
                    names.Add(current.name ?? "<unnamed>");
                    current = current.parent;
                }
                names.Reverse();
                return string.Join("/", names.ToArray())
                    .Replace('\r', ' ')
                    .Replace('\n', ' ')
                    .Replace('\'', '"');
            }
            catch (Exception exception)
            {
                return "<read_failed:" + exception.GetType().Name + ">";
            }
        }

        private static string DescribeCapturedSeamPose(SeamTransformPose captured)
        {
            if (!captured.Captured)
                return "[captured=False]";

            return
                $"[captured=True underAnimatorHierarchy={captured.UnderAnimatorHierarchy} " +
                $"localPos={DescribeVector(captured.LocalPosition)} " +
                $"localEuler={DescribeEuler(captured.LocalRotation.eulerAngles)} " +
                $"localScale={DescribeVector(captured.LocalScale)} " +
                $"worldPos={DescribeVector(captured.WorldPosition)} " +
                $"worldEuler={DescribeEuler(captured.WorldRotation.eulerAngles)} " +
                $"worldScale={DescribeVector(captured.WorldScale)}]";
        }

        private static string DescribeCurrentSeamPose(Transform transform)
        {
            if (transform == null)
                return "<target_missing>";

            try
            {
                return
                    $"[localPos={DescribeVector(transform.localPosition)} " +
                    $"localEuler={DescribeEuler(transform.localEulerAngles)} " +
                    $"localScale={DescribeVector(transform.localScale)} " +
                    $"worldPos={DescribeVector(transform.position)} " +
                    $"worldEuler={DescribeEuler(transform.rotation.eulerAngles)} " +
                    $"worldScale={DescribeVector(transform.lossyScale)}]";
            }
            catch (Exception exception)
            {
                return "<read_failed:" + exception.GetType().Name + ">";
            }
        }

        private static string DescribeCapturedEuler(bool captured, Quaternion rotation)
        {
            return captured ? DescribeEuler(rotation.eulerAngles) : "<not_captured>";
        }

        private static string DescribeEuler(Vector3 euler)
        {
            return $"({euler.x:0.######},{euler.y:0.######},{euler.z:0.######})";
        }

        private static Vector3 DescribeEulerDeviation(Vector3 current, Vector3 rest)
        {
            return new Vector3(
                Mathf.DeltaAngle(rest.x, current.x),
                Mathf.DeltaAngle(rest.y, current.y),
                Mathf.DeltaAngle(rest.z, current.z));
        }

        private static string DescribeVector(Vector3 value)
        {
            return $"({value.x:0.######},{value.y:0.######},{value.z:0.######})";
        }

        private static double LapMilliseconds(SeamPhaseStopwatch stopwatch)
        {
            return stopwatch != null ? stopwatch.LapMilliseconds() : 0d;
        }

        private void DetectUnsafeCameraDisplacement()
        {
            if (!hasCameraPlayerLocalBaseline || requestedStopReason.HasValue)
                return;

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || player.transform == null || player.gameplayCamera == null)
            {
                if (!cameraGuardEvaluationUnavailableLogged)
                {
                    cameraGuardEvaluationUnavailableLogged = true;
                    context?.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.evaluation_unavailable: " +
                        $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                        $"playerPresent={player != null} playerTransformPresent={player?.transform != null} " +
                        $"gameplayCameraPresent={player?.gameplayCamera != null} " +
                        "action='continue_without_guard_evaluation'.");
                }
                return;
            }

            Vector3 current = player.transform.InverseTransformPoint(player.gameplayCamera.transform.position);

            // Stance-aware viewpoint invariant: a crouch-pose sink (~1.18 m) sits inside both
            // the displacement threshold and the crouch<->stand envelope whitelist below, so
            // the magnitude checks alone let a viewpoint stuck at crouch height while the
            // player stands (or vice versa) run indefinitely. Evaluate it before the early
            // return so the whitelist can no longer mask a stance-mismatched height.
            if (EvaluateStanceViewpointInvariant(player, current))
                return;

            float displacement = Vector3.Distance(current, cameraPlayerLocalPositionAtStart);
            if (displacement <= CameraDisplacementGuardThreshold)
                return;

            float currentDisplacementFromVanillaRest = Vector3.Distance(
                current,
                VanillaCameraPlayerLocalRestExpectation);
            // Built lazily: once the baseline threshold trips every tick reaches this point,
            // but the suppression branches below log at most once per session, so eagerly
            // formatting ~10 fields per tick would be a hot-path allocation for nothing.
            string BuildMeasurements() =>
                $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                $"displacement={displacement:0.###} threshold={CameraDisplacementGuardThreshold:0.###} " +
                $"baseline={DescribeVector(cameraPlayerLocalPositionAtStart)} " +
                $"current={DescribeVector(current)} " +
                $"vanilla_rest_expectation={DescribeVector(VanillaCameraPlayerLocalRestExpectation)} " +
                $"baseline_displacement_from_vanilla_rest={cameraBaselineDisplacementFromVanillaRest:0.###} " +
                $"current_displacement_from_vanilla_rest={currentDisplacementFromVanillaRest:0.###} " +
                $"baseline_source='{CameraDisplacementGuardBaselineSource}' " +
                $"pre_existing_displacement={cameraGuardPreExistingDisplacementDetected} " +
                $"baseline_contaminated={cameraGuardBaselineContaminated}";

            if (ConsumerOwnsCameraPresentation)
            {
                if (!consumerOwnedCameraLogged)
                {
                    consumerOwnedCameraLogged = true;
                    context?.Logger?.LogInfo(
                        "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.guard_exempt: " +
                        BuildMeasurements() + " action='continue_exempt'.");
                }
                return;
            }

            // A contaminated reference is never treated as authored damage merely because a
            // later frame is far from it. Movement back toward vanilla rest is evidence that
            // the pre-existing displacement is unwinding, not that this controller worsened it.
            if (cameraGuardPreExistingDisplacementDetected &&
                currentDisplacementFromVanillaRest <= cameraBaselineDisplacementFromVanillaRest)
            {
                if (!cameraGuardPreExistingSuppressionLogged)
                {
                    cameraGuardPreExistingSuppressionLogged = true;
                    context?.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.pre_existing_displacement: " +
                        BuildMeasurements() + " phase='tick' action='continue_pre_existing_not_worsened'.");
                }
                return;
            }

            // Crouch<->stand transitions move the camera well beyond the try_start baseline
            // (crouched baseline ~1.17 vs standing rest 2.35) while staying inside the
            // vanilla-rest envelope. Genuine displacement — teleport, turret grab — exceeds
            // the threshold from both the baseline AND vanilla rest, so a camera still within
            // the envelope is never authored damage worth killing the session over.
            if (currentDisplacementFromVanillaRest <= CameraDisplacementGuardThreshold)
            {
                if (!cameraGuardVanillaRestEnvelopeSuppressionLogged)
                {
                    cameraGuardVanillaRestEnvelopeSuppressionLogged = true;
                    context?.Logger?.LogInfo(
                        "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.continue: " +
                        BuildMeasurements() + " action='continue_within_vanilla_rest_envelope'.");
                }
                return;
            }

            requestedStopReason = InteractionAnimationStopReason.PresenterFailure;
            context?.Logger?.LogError(
                "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.stop: " +
                BuildMeasurements() + " action='stop_genuinely_new_displacement'.");
        }

        /// <summary>
        /// Stance-aware half of the camera displacement guard: compares the camera's
        /// player-local height against the expected height for the player's CURRENT stance
        /// (standing rest 2.35 vs crouched rest ~1.17). A mismatch sustained past the vanilla
        /// crouch-glide window trips the same auto-stop recovery path as the magnitude guard.
        /// Returns true when it requested the stop (caller should bail out).
        /// </summary>
        private bool EvaluateStanceViewpointInvariant(
            GameNetcodeStuff.PlayerControllerB player,
            Vector3 cameraPlayerLocal)
        {
            bool crouching;
            bool specialAnimation;
            try
            {
                crouching = player.isCrouching;
                specialAnimation = player.inSpecialInteractAnimation;
            }
            catch
            {
                return false;
            }

            // Special interact animations legitimately move the viewpoint outside both stance
            // heights; vanilla owns the camera there and the auto-stop machinery already
            // yields the session.
            if (specialAnimation)
            {
                stanceViewpointMismatchTicks = 0;
                return false;
            }

            // A stance flip restarts the debounce: vanilla glides the camera between the two
            // heights after isCrouching changes, so the frames right after a flip always
            // mismatch legitimately.
            if (!hasStanceViewpointLastCrouchState ||
                stanceViewpointLastCrouchState != crouching)
            {
                hasStanceViewpointLastCrouchState = true;
                stanceViewpointLastCrouchState = crouching;
                stanceViewpointMismatchTicks = 0;
                return false;
            }

            float expectedHeight = crouching
                ? VanillaCameraCrouchedPlayerLocalRestHeight
                : VanillaCameraPlayerLocalRestExpectation.y;
            float heightDeviation = Mathf.Abs(cameraPlayerLocal.y - expectedHeight);
            if (heightDeviation <= StanceViewpointHeightToleranceMeters)
            {
                stanceViewpointMismatchTicks = 0;
                return false;
            }

            stanceViewpointMismatchTicks++;
            if (stanceViewpointMismatchTicks < StanceViewpointMismatchTicksRequired)
                return false;

            string measurements =
                $"handle={context.Handle} interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                $"crouching={crouching} camera_player_local_y={cameraPlayerLocal.y:0.###} " +
                $"expected_height={expectedHeight:0.###} height_deviation={heightDeviation:0.###} " +
                $"tolerance={StanceViewpointHeightToleranceMeters:0.###} " +
                $"sustained_ticks={stanceViewpointMismatchTicks} " +
                $"required_ticks={StanceViewpointMismatchTicksRequired}";

            if (ConsumerOwnsCameraPresentation)
            {
                if (!stanceViewpointGuardExemptLogged)
                {
                    stanceViewpointGuardExemptLogged = true;
                    context?.Logger?.LogInfo(
                        "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.stance_mismatch_exempt: " +
                        measurements + " action='continue_exempt'.");
                }
                return false;
            }

            requestedStopReason = InteractionAnimationStopReason.PresenterFailure;
            context?.Logger?.LogError(
                "[LCInteractionAnimationAPI] live_body.camera_displacement_guard.stop: " +
                measurements + " action='stop_stance_mismatched_viewpoint_height'.");
            return true;
        }

        private void CaptureScopedFirstPersonPose(InteractionAnimationManifest.BodyManifest body)
        {
            scopedFirstPersonPoseSnapshot = null;
            if (body == null)
                return;

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || player != GameNetworkManager.Instance?.localPlayerController ||
                player.playerModelArmsMetarig == null)
            {
                return;
            }

            // CaptureSubtree, not CaptureDescendants: authored clips can write the metarig
            // ROOT's local position, and vanilla never rewrites it — excluding the root left
            // that residue permanent (accumulating viewpoint drift across sessions).
            scopedFirstPersonPoseSnapshot = TransformPoseSnapshot.CaptureSubtree(
                player.playerModelArmsMetarig);
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.scoped_fp_pose_captured: " +
                $"handle={context.Handle} transforms={scopedFirstPersonPoseSnapshot.Count} " +
                "includesMetarigRoot=True.");
        }

        private void CaptureRigControlPose()
        {
            rigControlPoseSnapshot = null;
            rigControlRoot = null;
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreRigControlPoseEnabled)
                return;

            try
            {
                GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
                Transform armsMetarig = player != null
                    ? player.playerModelArmsMetarig
                    : null;
                rigControlRoot = FindChildRecursive(armsMetarig, "RigArms");
                if (rigControlRoot == null)
                    return;

                rigControlPoseSnapshot = TransformPoseSnapshot.CaptureSubtree(rigControlRoot);
                context?.Logger?.LogInfo(
                    "[RestoreSeam.rigpose] captured: " +
                    $"handle={context.Handle} transforms={rigControlPoseSnapshot.Count}.");
            }
            catch (Exception exception)
            {
                rigControlPoseSnapshot = null;
                rigControlRoot = null;
                context?.Logger?.LogWarning(
                    "[RestoreSeam.rigpose] capture_failed: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        private void RestoreRigControlPose()
        {
            if (rigControlRoot == null)
                return;

            var pristineRestoredTransforms = new HashSet<Transform>();
            int pristineRestored = 0;
            bool pristineBaselineUsed = false;
            try
            {
                GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
                pristineBaselineUsed =
                    InteractionAnimationApiRestoreDiagnostics.TryRestorePristineRigControlPose(
                        player,
                        rigControlRoot,
                        pristineRestoredTransforms,
                        out pristineRestored);
            }
            catch (Exception exception)
            {
                pristineRestoredTransforms.Clear();
                pristineRestored = 0;
                context?.Logger?.LogWarning(
                    "[RestoreSeam.rigpose] pristine_restore_dispatch_failed: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }

            int equipFallbackRestored = rigControlPoseSnapshot != null
                ? rigControlPoseSnapshot.RestoreExcept(pristineRestoredTransforms)
                : 0;
            context?.Logger?.LogInfo(
                "[RestoreSeam.rigpose] restored: " +
                $"handle={context.Handle} pristineBaselineUsed={pristineBaselineUsed} " +
                $"pristineRestored={pristineRestored} " +
                $"equipFallbackRestored={equipFallbackRestored} " +
                $"equipCaptured={(rigControlPoseSnapshot != null ? rigControlPoseSnapshot.Count : 0)}.");
        }

        private void CaptureThirdPersonRigControlPose()
        {
            thirdPersonRigControlPoseSnapshot = null;
            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            bool localPlayer = IsLocalPlayer(player);
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreThirdPersonRigControlPoseEnabled)
            {
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] capture_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"localPlayer={localPlayer} enabled=False " +
                    "reason='kill_switch_disabled' action='leave_third_person_rig_unchanged'.");
                return;
            }

            if (player == null || player.playerBodyAnimator == null)
            {
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] capture_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"localPlayer={localPlayer} enabled=True " +
                    $"playerPresent={player != null} animatorPresent={player?.playerBodyAnimator != null} " +
                    "reason='player_or_animator_missing' action='restore_unavailable'.");
                return;
            }

            try
            {
                thirdPersonRigControlPoseSnapshot =
                    InteractionAnimationApiRestoreDiagnostics
                        .CaptureThirdPersonRigControlPose(player);
                if (thirdPersonRigControlPoseSnapshot == null ||
                    thirdPersonRigControlPoseSnapshot.TotalCount == 0)
                {
                    thirdPersonRigControlPoseSnapshot = null;
                    RestoreLogger?.LogInfo(
                        "[RestoreSeam.tprig] capture_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"localPlayer={localPlayer} enabled=True " +
                        "reason='third_person_rig_unavailable' action='restore_unavailable'.");
                    return;
                }

                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] captured: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    $"localPlayer={localPlayer} enabled=True " +
                    $"complete={thirdPersonRigControlPoseSnapshot.IsComplete} " +
                    $"missing='{thirdPersonRigControlPoseSnapshot.MissingTargets}' " +
                    $"fullPoseTransforms={thirdPersonRigControlPoseSnapshot.FullPoseCount} " +
                    $"rotationOnlyTransforms={thirdPersonRigControlPoseSnapshot.RotationOnlyCount} " +
                    "source='session_entry_equip_fallback'.");
            }
            catch (Exception exception)
            {
                thirdPersonRigControlPoseSnapshot = null;
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] capture_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"localPlayer={localPlayer} enabled=True " +
                    $"reason='capture_failed:{exception.Message}' action='restore_unavailable'.");
            }
        }

        private void RestoreThirdPersonRigControlPose(bool animatorRestored)
        {
            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            bool localPlayer = IsLocalPlayer(player);
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreThirdPersonRigControlPoseEnabled)
            {
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] restore_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"localPlayer={localPlayer} enabled=False " +
                    "reason='kill_switch_disabled' action='leave_third_person_rig_unchanged'.");
                return;
            }

            if (!animatorRestored)
            {
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] restore_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"localPlayer={localPlayer} enabled=True " +
                    "reason='animator_ownership_restore_rejected' action='respect_external_owner'.");
                return;
            }

            if (thirdPersonRigControlPoseSnapshot == null)
            {
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] restore_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"localPlayer={localPlayer} enabled=True " +
                    "reason='session_entry_snapshot_unavailable' action='leave_third_person_rig_unchanged'.");
                return;
            }

            var pristineRestoredTransforms = new HashSet<Transform>();
            int pristineFullPoseRestored = 0;
            int pristineRotationOnlyRestored = 0;
            bool pristineBaselineUsed = false;
            string pristineGateReason;
            try
            {
                pristineBaselineUsed =
                    InteractionAnimationApiRestoreDiagnostics
                        .TryRestorePristineThirdPersonRigControlPose(
                            player,
                            pristineRestoredTransforms,
                            out pristineFullPoseRestored,
                            out pristineRotationOnlyRestored,
                            out pristineGateReason);
            }
            catch (Exception exception)
            {
                pristineRestoredTransforms.Clear();
                pristineFullPoseRestored = 0;
                pristineRotationOnlyRestored = 0;
                pristineGateReason = "dispatch_failed:" + exception.Message;
            }

            RestoreLogger?.LogInfo(
                "[RestoreSeam.tprig] pristine_restore_gate: " +
                $"frame={Time.frameCount} handle={context.Handle} " +
                $"localPlayer={localPlayer} " +
                $"enabled={InteractionAnimationApiRestoreDiagnostics.RestorePristineThirdPersonRigControlPoseEnabled} " +
                $"baselineUsed={pristineBaselineUsed} reason='{pristineGateReason}' " +
                $"fullPoseRestored={pristineFullPoseRestored} " +
                $"rotationOnlyRestored={pristineRotationOnlyRestored}.");

            int equipFallbackFullPoseRestored;
            int equipFallbackRotationOnlyRestored;
            try
            {
                thirdPersonRigControlPoseSnapshot.RestoreExcept(
                    pristineRestoredTransforms,
                    null,
                    out equipFallbackFullPoseRestored,
                    out equipFallbackRotationOnlyRestored);
            }
            catch (Exception exception)
            {
                RestoreLogger?.LogInfo(
                    "[RestoreSeam.tprig] restore_failed: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    $"localPlayer={localPlayer} enabled=True " +
                    $"reason='equip_fallback_failed:{exception.Message}' action='continue_teardown'.");
                return;
            }
            RestoreLogger?.LogInfo(
                "[RestoreSeam.tprig] restored: " +
                $"frame={Time.frameCount} handle={context.Handle} " +
                $"localPlayer={localPlayer} enabled=True " +
                $"pristineBaselineUsed={pristineBaselineUsed} " +
                $"pristineFullPoseRestored={pristineFullPoseRestored} " +
                $"pristineRotationOnlyRestored={pristineRotationOnlyRestored} " +
                $"equipFallbackFullPoseRestored={equipFallbackFullPoseRestored} " +
                $"equipFallbackRotationOnlyRestored={equipFallbackRotationOnlyRestored} " +
                $"equipCaptured={thirdPersonRigControlPoseSnapshot.TotalCount}.");
        }

        /// <summary>
        /// Stop-seam twin of the camera-rotation snap-to-rest: restores the camera chain local
        /// positions (CameraContainer, gameplay camera, arms metarig root, local-arms transform)
        /// to their authored defaults. Authored clips playing on playerBodyAnimator can write
        /// positions on these transforms; vanilla only ever derives them from each other, so
        /// residue left here at Stop otherwise persists for the rest of the game and stacks
        /// across sessions. Positions only — rotation is owned by ReapplySeamCameraRotation.
        /// </summary>
        private void ApplyCameraChainPositionSnapToRest()
        {
            if (!InteractionAnimationApiRestoreDiagnostics
                    .RestoreCameraChainPositionSnapToRestEnabled)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerachain] snap_skipped: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    "reason='kill_switch_disabled' action='leave_camera_chain'.");
                return;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || !IsLocalPlayer(player))
                return;

            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerachain] snap_skipped: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    "reason='local_camera_owned_externally' action='leave_camera_chain'.");
                return;
            }

            try
            {
                bool crouching = false;
                bool specialAnimation = false;
                try
                {
                    crouching = player.isCrouching;
                    specialAnimation = player.inSpecialInteractAnimation;
                }
                catch { }
                if (crouching || specialAnimation)
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.camerachain] snap_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"crouching={crouching} specialAnimation={specialAnimation} " +
                        "reason='legitimately_displaced_state' action='leave_camera_chain'.");
                    return;
                }

                Vector3 beforePlayerLocal = player.gameplayCamera != null
                    ? player.transform.InverseTransformPoint(
                        player.gameplayCamera.transform.position)
                    : Vector3.zero;
                if (!InteractionAnimationApiRestoreDiagnostics
                        .TryRestorePristineCameraChainPositions(
                            player,
                            out int restored,
                            out string reason,
                            out string baselineSource))
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.camerachain] snap_skipped: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"reason='{reason}' action='leave_camera_chain'.");
                    return;
                }

                Vector3 afterPlayerLocal = player.gameplayCamera != null
                    ? player.transform.InverseTransformPoint(
                        player.gameplayCamera.transform.position)
                    : Vector3.zero;
                // The restore-scoped stabilizer memorized the stop-entry (pre-snap) camera
                // position; without retargeting, its final restore and two deferred LateUpdates
                // would undo this snap and re-accumulate camera-chain residue across sessions.
                bool stabilizerRetargeted = false;
                if (cameraPositionStabilizer != null)
                {
                    cameraPositionStabilizer.RetargetToCurrentPosition();
                    stabilizerRetargeted = true;
                }
                context?.Logger?.LogInfo(
                    "[RestoreSeam.camerachain] snap_applied: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    $"restoredTransforms={restored} " +
                    $"beforePlayerLocal={DescribeVector(beforePlayerLocal)} " +
                    $"afterPlayerLocal={DescribeVector(afterPlayerLocal)} " +
                    $"source='{baselineSource}' " +
                    $"stabilizerRetargeted={stabilizerRetargeted}.");
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.camerachain] snap_failed: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        private void ApplyVanillaLocalArmsGlueBeforeRigEvaluation()
        {
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreVanillaArmsGlueEnabled)
                return;

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (!IsLocalPlayer(player))
                return;

            try
            {
                bool inSpecialInteractAnimation = player.inSpecialInteractAnimation;
                bool localArmsMatchCamera = player.localArmsMatchCamera;
                string branch = inSpecialInteractAnimation
                    ? "specialanim"
                    : localArmsMatchCamera ? "lateupdate" : "update";
                context?.Logger?.LogInfo(
                    "[RestoreSeam.armsglue] gates: " +
                    $"frame={Time.frameCount} handle={context.Handle} " +
                    $"inSpecialInteractAnimation={inSpecialInteractAnimation} " +
                    $"localArmsMatchCamera={localArmsMatchCamera} branch={branch}.");

                Transform armsMetarig = player.playerModelArmsMetarig;
                if (armsMetarig == null)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.armsglue] required_transform_missing: " +
                        $"frame={Time.frameCount} handle={context.Handle} branch={branch} " +
                        "missing='playerModelArmsMetarig'.");
                    return;
                }

                if (inSpecialInteractAnimation)
                {
                    // Match PlayerControllerB.Update's special-interaction else branch. Vanilla's
                    // head-bob-off camera Y pin is also gated off during special interactions.
                    armsMetarig.localEulerAngles = new Vector3(-90f, 0f, 0f);
                    return;
                }

                bool headBobbingEnabled = true;
                try
                {
                    headBobbingEnabled = IngamePlayerSettings.Instance.settings.headBobbing;
                }
                catch (Exception exception)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.armsglue] head_bob_read_failed: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"branch={branch} error='{exception.Message}'.");
                }

                Transform cameraContainer = player.cameraContainerTransform;
                if (!headBobbingEnabled && cameraContainer != null && armsMetarig != null)
                {
                    cameraContainer.position = new Vector3(
                        cameraContainer.position.x,
                        armsMetarig.position.y,
                        cameraContainer.position.z);
                }
                else if (!headBobbingEnabled)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.armsglue] camera_y_pin_transform_missing: " +
                        $"frame={Time.frameCount} handle={context.Handle} branch={branch} " +
                        $"cameraContainerPresent={cameraContainer != null}.");
                }

                Transform localArms = player.localArmsTransform;
                Transform armsRotationTarget = player.localArmsRotationTarget;
                if (localArms == null || armsRotationTarget == null)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.armsglue] required_transform_missing: " +
                        $"frame={Time.frameCount} handle={context.Handle} branch={branch} " +
                        $"localArmsPresent={localArms != null} " +
                        $"rotationTargetPresent={armsRotationTarget != null}.");
                    return;
                }

                if (localArmsMatchCamera)
                {
                    Camera gameplayCamera = player.gameplayCamera;
                    if (cameraContainer == null || gameplayCamera == null)
                    {
                        context?.Logger?.LogWarning(
                            "[RestoreSeam.armsglue] required_transform_missing: " +
                            $"frame={Time.frameCount} handle={context.Handle} branch={branch} " +
                            $"cameraContainerPresent={cameraContainer != null} " +
                            $"gameplayCameraPresent={gameplayCamera != null}.");
                        return;
                    }

                    // Match PlayerControllerB.LateUpdate: camera Y first, then local-arms
                    // position, then arms-metarig rotation.
                    localArms.position =
                        cameraContainer.position + gameplayCamera.transform.up * -0.5f;
                }
                else
                {
                    // Match PlayerControllerB.Update's exact ordering: compute position from the
                    // metarig's current pre-assignment orientation, then rotate the metarig.
                    localArms.position =
                        armsMetarig.position + armsMetarig.forward * -0.445f;

                    // Vanilla lerps this rotation by 15f * Time.deltaTime. Restore instead snaps
                    // to the converged steady-state target so one stale lerp step cannot survive
                    // into the seam frame evaluated below.
                }

                armsMetarig.rotation = armsRotationTarget.rotation;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.armsglue] apply_failed: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"error='{exception.Message}'.");
            }
        }

        private void RestoreScopedFirstPersonPose()
        {
            if (scopedFirstPersonPoseSnapshot == null)
                return;

            int restored = scopedFirstPersonPoseSnapshot.Restore();
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.scoped_fp_pose_restored: " +
                $"handle={context.Handle} restored={restored} " +
                $"captured={scopedFirstPersonPoseSnapshot.Count}.");
        }

        public bool TrySetAnimatorParameter(string parameterName, AnimatorControllerParameterType parameterType, float value)
        {
            if (!active || bodyAnimator == null || string.IsNullOrWhiteSpace(parameterName) ||
                !HasParameter(parameterName, parameterType))
            {
                return false;
            }

            try
            {
                switch (parameterType)
                {
                    case AnimatorControllerParameterType.Bool:
                        bodyAnimator.SetBool(parameterName, value != 0f);
                        return true;
                    case AnimatorControllerParameterType.Int:
                        bodyAnimator.SetInteger(parameterName, (int)value);
                        return true;
                    case AnimatorControllerParameterType.Float:
                        bodyAnimator.SetFloat(parameterName, value);
                        return true;
                    case AnimatorControllerParameterType.Trigger:
                        bodyAnimator.ResetTrigger(parameterName);
                        bodyAnimator.SetTrigger(parameterName);
                        return true;
                    default:
                        return false;
                }
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// Fires the controller's exit trigger so the put-away (Hide) state plays, and reports
        /// how long the caller should wait before Stop restores the vanilla controller.
        /// </summary>
        public float BeginExit()
        {
            InteractionAnimationManifest.BodyManifest body = context?.Manifest?.body;
            if (!active || bodyAnimator == null || body == null || exitRequested)
                return 0f;

            exitRequested = true;
            SetBoolIfExists(body.activeBool, false);
            FireTriggerIfExists(body.exitTrigger);
            float exitSeconds = Mathf.Max(0f, body.exitSeconds);
            exitElapsedSeconds = 0f;
            exitDurationSeconds = exitSeconds;
            exitStartFullBodyWeight = GetLayerWeightOrZero(fullBodyLayerIndex);
            exitStartFirstPersonWeight = GetLayerWeightOrZero(firstPersonLayerIndex);
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.exit_begun: " +
                $"handle={context.Handle} exitSeconds={exitSeconds:0.###}.");
            return exitSeconds;
        }

        // 0 = idle, 1 = walking, 2 = sprinting — matched by the shell controller's movement
        // transitions so the walk/run first-person loops play while moving with the item out.
        private static readonly System.Reflection.FieldInfo VanillaIsWalkingField =
            typeof(GameNetcodeStuff.PlayerControllerB).GetField(
                "isWalking",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

        private static readonly System.Reflection.FieldInfo VanillaIsJumpingField =
            typeof(GameNetcodeStuff.PlayerControllerB).GetField(
                "isJumping",
                System.Reflection.BindingFlags.Instance |
                System.Reflection.BindingFlags.NonPublic |
                System.Reflection.BindingFlags.Public);

        /// <summary>
        /// Rewrites the transition-gated vanilla locomotion parameters from the player's CURRENT
        /// state. Vanilla re-asserts "Sprinting"/"Sideways"/"animationSpeed" every frame while
        /// its cached isWalking field is true, but writes "Walking", "crouching", and "Jumping"
        /// only on state transitions (decompiled PlayerControllerB.Update) — so a stale animator
        /// value survives indefinitely while the player keeps moving.
        /// </summary>
        private void SyncVanillaLocomotionParameters(string phase)
        {
            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || bodyAnimator == null)
                return;

            try
            {
                bool sprinting = false;
                bool crouching = false;
                try { sprinting = player.isSprinting; } catch { }
                try { crouching = player.isCrouching; } catch { }

                bool walking;
                string walkingSource;
                object walkingValue = null;
                try { walkingValue = VanillaIsWalkingField?.GetValue(player); } catch { }
                if (walkingValue is bool vanillaWalking)
                {
                    walking = vanillaWalking;
                    walkingSource = "vanilla_isWalking_field";
                }
                else
                {
                    float horizontalSpeed = 0f;
                    try
                    {
                        if (player.thisController != null)
                        {
                            Vector3 velocity = player.thisController.velocity;
                            velocity.y = 0f;
                            horizontalSpeed = velocity.magnitude;
                        }
                    }
                    catch { }
                    walking = horizontalSpeed > 0.2f;
                    walkingSource = "horizontal_velocity_fallback";
                }

                bool jumping = false;
                try
                {
                    object jumpingValue = VanillaIsJumpingField?.GetValue(player);
                    if (jumpingValue is bool vanillaJumping)
                        jumping = vanillaJumping;
                }
                catch { }

                SetBoolIfExists("Walking", walking);
                SetBoolIfExists("Sprinting", walking && sprinting);
                SetBoolIfExists(VanillaCrouchingBool, crouching);
                SetBoolIfExists("Jumping", jumping);
                if (!walking)
                    SetBoolIfExists("Sideways", false);

                // Vanilla enters crouch through the "startCrouching" trigger PLUS the
                // "crouching" bool (decompiled PlayerControllerB.Crouch_performed). The bool
                // alone never fires trigger-gated crouch transitions, so mirror the trigger on
                // the crouch edge. Reset on the stand edge so a latched trigger cannot replay
                // a crouch entry later.
                if (hasLastSyncedCrouchState && crouching != lastSyncedCrouchState)
                {
                    if (crouching)
                        FireTriggerIfExists(VanillaStartCrouchingTrigger);
                    else
                        ResetTriggerIfExists(VanillaStartCrouchingTrigger);
                }
                hasLastSyncedCrouchState = true;
                lastSyncedCrouchState = crouching;

                // Per-tick syncs would otherwise emit this line every frame; log only when a
                // value changed or the caller is a one-shot seam phase.
                int syncSignature =
                    (walking ? 1 : 0) |
                    (sprinting ? 2 : 0) |
                    (crouching ? 4 : 0) |
                    (jumping ? 8 : 0);
                bool perFramePhase = string.Equals(phase, "tick", StringComparison.Ordinal);
                if (!perFramePhase || syncSignature != lastLocomotionSyncSignature)
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.locomotion] parameters_synced: " +
                        $"frame={Time.frameCount} handle={context.Handle} phase='{phase}' " +
                        $"walking={walking} walkingSource='{walkingSource}' " +
                        $"sprinting={sprinting} crouching={crouching} jumping={jumping}.");
                }
                lastLocomotionSyncSignature = syncSignature;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[RestoreSeam.locomotion] parameters_sync_failed: " +
                    $"frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"phase='{phase}' error='{exception.Message}'.");
            }
        }

        private void DriveMovementParameter()
        {
            InteractionAnimationManifest.BodyManifest body = context?.Manifest?.body;
            if (body == null || string.IsNullOrWhiteSpace(body.movementParameter) || exitRequested)
                return;

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null)
                return;

            int movement = 0;
            try
            {
                bool sprinting = player.isSprinting;
                float horizontalSpeed = 0f;
                if (player.thisController != null)
                {
                    UnityEngine.Vector3 velocity = player.thisController.velocity;
                    velocity.y = 0f;
                    horizontalSpeed = velocity.magnitude;
                }

                if (sprinting && horizontalSpeed > 0.2f)
                    movement = 2;
                else if (horizontalSpeed > 0.2f)
                    movement = 1;
            }
            catch
            {
                movement = 0;
            }

            if (movement == lastMovementValue)
                return;

            lastMovementValue = movement;
            try { bodyAnimator.SetInteger(body.movementParameter, movement); } catch { }
        }

        public void Stop(InteractionAnimationStopReason stopReason)
        {
            if (!active && bodyAnimator == null && bundle == null)
                return;

            SeamPhaseStopwatch seamTiming =
                InteractionAnimationApiRestoreDiagnostics.RestoreSeamFrameLoggerEnabled
                    ? new SeamPhaseStopwatch()
                    : null;
            StopTransformChainDiagnostics();
            StopExternalCameraPresentationDiagnostics();
            StopLocalVisorHardGlue();
            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            InteractionAnimationApiRestoreDiagnostics.NotifyStop(player, propInstance);
            StartRestoreScopedCameraPositionStabilizer();
            double setupMs = LapMilliseconds(seamTiming);
            CameraRotationSnapshot stopCameraRotation = CaptureSeamCameraRotation("stop");
            StopLocalCameraRotationStabilizer(restoreSessionEntryRotation: true);
            double cameraCaptureMs = LapMilliseconds(seamTiming);
            VisorPoseSnapshot stopVisorPose = CaptureSeamVisorPose("stop");
            double visorCaptureMs = LapMilliseconds(seamTiming);
            DestroyProp();
            double propDestroyMs = LapMilliseconds(seamTiming);
            string restoreResult = RestoreAnimator(out AnimatorStateRestoreMode restoreStateMode);
            double animatorRestoreMs = LapMilliseconds(seamTiming);
            bool animatorRestored =
                string.Equals(restoreResult, "restored", StringComparison.Ordinal);
            if (animatorRestored)
            {
                // The snapshot just rewrote every parameter with its equip-time value. The
                // player's movement state may have changed during the session, and vanilla only
                // corrects "Walking"/"crouching"/"Jumping" on transitions — a stale value here
                // kills locomotion (sprint animation and camera bob) until the player stops and
                // starts again. Re-sync those from the player's live state.
                SyncVanillaLocomotionParameters("stop");
                RestoreScopedFirstPersonPose();
                RestoreRigControlPose();
            }
            RestoreThirdPersonRigControlPose(animatorRestored);
            double poseRestoreMs = LapMilliseconds(seamTiming);
            RestoreLiveRigBuilders();
            InteractionAnimationApiRestoreDiagnostics.LogRigAnimatorStates(
                bodyAnimator,
                "before_build");
            RebuildRigBuilders("restore");
            InteractionAnimationApiRestoreDiagnostics.LogRigAnimatorStates(
                bodyAnimator,
                "after_build");
            double rigBuildMs = LapMilliseconds(seamTiming);
            // Vanilla normally applies this glue later in PlayerControllerB.LateUpdate. Stop can
            // run after that callback, so reproduce it synchronously before the final animator
            // and rig evaluations to keep the rendered restore frame continuous.
            ReapplySeamCameraRotation(stopCameraRotation, "stop", animatorRestored);
            ReapplySeamVisorPose(stopVisorPose, "stop", animatorRestored);
            if (animatorRestored)
            {
                // Position snap must run before the arms glue: the glue derives camera-container
                // and local-arms positions from the arms metarig, so it has to read the healed
                // chain, not the last authored-clip pose.
                ApplyCameraChainPositionSnapToRest();
                ApplyVanillaLocalArmsGlueBeforeRigEvaluation();
            }
            double seamGlueMs = LapMilliseconds(seamTiming);
            // Snapshot restoration evaluates before the RigBuilder graph is rebuilt. A second
            // zero-delta evaluation closes the same one-frame hole on teardown.
            try { bodyAnimator?.Update(0f); } catch { }
            double animatorUpdateMs = LapMilliseconds(seamTiming);
            EvaluateRigBuilders("restore");
            double rigEvaluateMs = LapMilliseconds(seamTiming);
            InteractionAnimationApiRestoreDiagnostics.LogRigAnimatorStates(
                bodyAnimator,
                "after_final_update");
            // Keep the camera pinned through two final LateUpdates. Destroying the guard on the
            // controller-restore frame exposed one small viewpoint snap while the vanilla camera
            // parent settled on the following frame.
            StopLocalCameraPositionStabilizer(restorePosition: true, deferRelease: true);
            InteractionAnimationApiRestoreDiagnostics.NotifyRestoreCompleted(player);
            ReleaseBundle(retainOwnedBundles: stopReason != InteractionAnimationStopReason.Shutdown);
            double cleanupMs = LapMilliseconds(seamTiming);
            if (seamTiming != null)
            {
                context?.Logger?.LogInfo(
                    "[RestoreSeam.timing] " +
                    $"phase='stop' frame={Time.frameCount} " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"totalMs={seamTiming.TotalMilliseconds:0.###} setupMs={setupMs:0.###} " +
                    $"cameraCaptureMs={cameraCaptureMs:0.###} visorCaptureMs={visorCaptureMs:0.###} " +
                    $"propDestroyMs={propDestroyMs:0.###} " +
                    $"animatorRestoreMs={animatorRestoreMs:0.###} poseRestoreMs={poseRestoreMs:0.###} " +
                    $"rigBuildMs={rigBuildMs:0.###} seamGlueMs={seamGlueMs:0.###} " +
                    $"animatorUpdateMs={animatorUpdateMs:0.###} rigEvaluateMs={rigEvaluateMs:0.###} " +
                    $"cleanupMs={cleanupMs:0.###}.");
            }
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.restored: " +
                $"handle={(context != null ? context.Handle.ToString() : "<none>")} reason='{stopReason}' " +
                $"controllerRestore='{restoreResult}' restoreStateMode='{FormatRestoreStateMode(restoreStateMode)}'.");
            bodyAnimator = null;
            appliedController = null;
            snapshot = null;
            rigControlPoseSnapshot = null;
            rigControlRoot = null;
            thirdPersonRigControlPoseSnapshot = null;
            scopedFirstPersonPoseSnapshot = null;
            suppressedRigBuilders.Clear();
            rightArmIkTarget = null;
            leftArmIkTarget = null;
            rightHandBone = null;
            rightShoulderBone = null;
            propReleased = false;
            exitRequested = false;
            exitElapsedSeconds = 0f;
            exitDurationSeconds = 0f;
            exitStartFullBodyWeight = 0f;
            exitStartFirstPersonWeight = 0f;
            lastMovementValue = -1;
            hasLastSyncedCrouchState = false;
            lastSyncedCrouchState = false;
            lastLocomotionSyncSignature = -1;
            ResetCameraDisplacementGuardState();
            cameraPositionStabilizer = null;
            cameraRotationStabilizer = null;
            requestedStopReason = null;
            active = false;
            context = null;
        }

        /// <summary>
        /// Instantiates the manifest-configured prop (shipped in the consumer's clip-pack bundle)
        /// and parents it rigidly to the configured hand bone. The attach pose comes from the
        /// baker (anatomical basis-corrected source pose), so the prop follows every clip exactly
        /// like the source prop followed the source hand.
        /// </summary>
        private void AttachPropIfConfigured()
        {
            InteractionAnimationManifest.PropManifest prop = context?.Manifest?.body?.prop;
            if (prop == null || !prop.enabled || string.IsNullOrWhiteSpace(prop.prefabAssetName))
                return;

            AssetBundle propBundle = clipPackBundle != null ? clipPackBundle : bundle;
            if (propBundle == null)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.prop_no_asset_bundle: " +
                    $"handle={context.Handle} prefab='{prop.prefabAssetName}'.");
                return;
            }

            GameObject prefab = propBundle.LoadAsset<GameObject>(prop.prefabAssetName);
            if (prefab == null)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.prop_prefab_missing: " +
                    $"handle={context.Handle} prefab='{prop.prefabAssetName}'.");
                return;
            }

            Transform armsMetarig = null;
            try
            {
                GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
                bool isLocalPlayer = player != null &&
                    player == GameNetworkManager.Instance?.localPlayerController;
                armsMetarig = isLocalPlayer ? player.playerModelArmsMetarig : null;
            }
            catch { }

            Transform searchRoot = armsMetarig != null
                ? armsMetarig
                : (bodyAnimator != null ? bodyAnimator.transform : null);
            Transform attachBone = ResolvePropAttachBone(searchRoot, prop);
            if (attachBone == null)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.prop_attach_bone_missing: " +
                    $"handle={context.Handle} bone='{prop.attachBonePath}'.");
                return;
            }

            propInstance = UnityEngine.Object.Instantiate(prefab, attachBone, false);
            propInstance.name = "Y4NGZ_" + prop.prefabAssetName + "_Instance";
            propInstance.transform.localPosition = prop.localPosition.ToUnityVector3();
            propInstance.transform.localEulerAngles = prop.localEulerAngles.ToUnityVector3();
            propInstance.transform.localScale = Vector3.one * (prop.localScale > 0f ? prop.localScale : 1f);
            SetLayerRecursive(propInstance, attachBone.gameObject.layer);
            propReleased = false;

            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.prop_attached: " +
                $"handle={context.Handle} prefab='{prop.prefabAssetName}' bone='{prop.attachBonePath}' " +
                $"localPos=({prop.localPosition.x:0.###},{prop.localPosition.y:0.###},{prop.localPosition.z:0.###}) " +
                $"scale={prop.localScale:0.####}.");
        }

        private void ReleasePropIfDue()
        {
            if (propReleased || propInstance == null)
                return;

            InteractionAnimationManifest.PropManifest prop = context?.Manifest?.body?.prop;
            if (prop == null || prop.releaseSeconds <= 0f || elapsedSeconds < prop.releaseSeconds)
                return;

            propReleased = true;
            DestroyProp();
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.prop_released: " +
                $"handle={context.Handle} elapsed={elapsedSeconds:0.###} releaseSeconds={prop.releaseSeconds:0.###}.");
        }

        private void DestroyProp()
        {
            if (propInstance == null)
                return;

            try { UnityEngine.Object.Destroy(propInstance); } catch { }
            propInstance = null;
        }

        private static void SetLayerRecursive(GameObject root, int layer)
        {
            if (root == null)
                return;

            root.layer = layer;
            for (int i = 0; i < root.transform.childCount; i++)
                SetLayerRecursive(root.transform.GetChild(i).gameObject, layer);
        }

        private bool TryLoadBundle(
            InteractionAnimationManifest manifest,
            InteractionAnimationManifest.BodyManifest body,
            out string reason)
        {
            reason = string.Empty;

            string bundleInternalName = manifest.bundleInternalName ?? string.Empty;
            // Resolve first so a loaded bundle cannot bypass AssetRootPath confinement.
            if (!TryResolveBundlePath(
                    body.bundleFileName,
                    context.AssetRootPath,
                    out string bundlePath,
                    out reason))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(bundleInternalName))
            {
                foreach (AssetBundle loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (loadedBundle != null &&
                        string.Equals(loadedBundle.name, bundleInternalName, StringComparison.OrdinalIgnoreCase))
                    {
                        bundle = loadedBundle;
                        ownsBundle = false;
                        return true;
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
            {
                reason = "live_body.bundle_missing:" + body.bundleFileName;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.bundle_missing: " +
                    $"handle={context.Handle} file='{body.bundleFileName}' resolvedPath='{bundlePath}'.");
                return false;
            }

            bundle = AssetBundle.LoadFromFile(bundlePath);
            if (bundle == null)
            {
                reason = "live_body.bundle_load_failed:" + bundlePath;
                return false;
            }

            ownsBundle = true;
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.bundle_loaded: " +
                $"handle={context.Handle} path='{bundlePath}' internalName='{bundle.name}'.");
            return true;
        }

        private bool TryApplyController(
            InteractionAnimationManifest.BodyManifest body,
            out string reason)
        {
            reason = string.Empty;

            RuntimeAnimatorController controller = null;
            if (!string.IsNullOrWhiteSpace(body.controllerAssetName))
                controller = bundle.LoadAsset<RuntimeAnimatorController>(body.controllerAssetName);
            if (controller == null)
            {
                reason = "live_body.controller_missing:" + body.controllerAssetName;
                context.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.controller_missing: " +
                    $"handle={context.Handle} assetName='{body.controllerAssetName}'.");
                return false;
            }

            snapshot = AnimatorStateSnapshot.Capture(bodyAnimator);
            if (snapshot == null)
            {
                reason = "live_body.snapshot_failed";
                return false;
            }

            // Record the equip-time stance so Stop can refuse replaying a stale base-layer
            // state when the player crouched or stood up during the session, and seed the
            // crouch-edge tracker that fires vanilla's "startCrouching" trigger.
            bool captureCrouching = false;
            bool captureCrouchingKnown = false;
            try
            {
                GameNetcodeStuff.PlayerControllerB capturePlayer = context?.Request?.Player;
                if (capturePlayer != null)
                {
                    captureCrouching = capturePlayer.isCrouching;
                    captureCrouchingKnown = true;
                }
            }
            catch { }
            snapshot.CapturedCrouching = captureCrouchingKnown ? captureCrouching : (bool?)null;
            hasLastSyncedCrouchState = captureCrouchingKnown;
            lastSyncedCrouchState = captureCrouching;

            RuntimeAnimatorController controllerToApply = controller;
            if (!TryApplyClipPackOverride(body, controller, ref controllerToApply, out reason))
            {
                snapshot = null;
                return false;
            }

            try
            {
                bodyAnimator.runtimeAnimatorController = controllerToApply;
            }
            catch (Exception exception)
            {
                reason = "live_body.controllerAssetName_apply_exception:" + exception.Message;
                snapshot = null;
                return false;
            }

            appliedController = controllerToApply;
            fullBodyLayerIndex = FindLayerIndex(bodyAnimator, body.fullBodyLayer);
            firstPersonLayerIndex = FindLayerIndex(bodyAnimator, body.firstPersonArmsLayer);

            // The controller swap reset every parameter to its default. Vanilla only rewrites
            // "Walking"/"crouching"/"Jumping" on state transitions, so carry the equip-time
            // values (captured a moment ago, still accurate) onto the shell controller — else
            // equipping mid-walk kills locomotion (sprint animation and camera bob) until the
            // player happens to stop and start moving again.
            int reappliedParameters = snapshot.ReapplyParameters(bodyAnimator);
            context.Logger?.LogInfo(
                "[RestoreSeam.locomotion] parameters_reapplied: " +
                $"frame={Time.frameCount} handle={context.Handle} phase='start' " +
                $"count={reappliedParameters}.");

            // The controller swap reset the base layer to its default (standing) state, and
            // vanilla enters the crouch state only through the "startCrouching" trigger — an
            // input edge that already passed when the session starts while the player is
            // crouched. The crouch-edge tracker above is seeded to the current stance, so it
            // will never fire that entry either. Actively place the fresh base layer in the
            // crouch state now; otherwise the body stands while the capsule/camera sit at
            // crouch height for the entire session.
            if (captureCrouching)
            {
                FireTriggerIfExists(VanillaStartCrouchingTrigger);
                context.Logger?.LogInfo(
                    "[RestoreSeam.locomotion] crouch_entry_asserted: " +
                    $"frame={Time.frameCount} handle={context.Handle} phase='start' " +
                    "reason='session_started_while_crouched' " +
                    "action='fire_startCrouching_on_fresh_base_layer'.");
            }

            SetBoolIfExists(body.activeBool, true);
            FireTriggerIfExists(body.enterTrigger);
            elapsedSeconds = 0f;
            ApplyLayerWeights();
            bodyAnimator.Update(0f);

            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.controllerAssetName_applied: " +
                $"handle={context.Handle} controller='{controller.name}' " +
                $"previousController='{snapshot.RuntimeAnimatorController?.name ?? "<null>"}' " +
                $"activeBool='{body.activeBool}' enterTrigger='{body.enterTrigger}' " +
                $"startLayerWeight={body.startLayerWeight:0.###} rampSeconds={body.layerWeightRampSeconds:0.###}.");
            return true;
        }

        // Generic playback path: load a clip-pack bundle (baked IK-target clips) and override
        // the shell controller's slot clips by name. New animations are new clip packs; the
        // shell controller never changes. Fails closed when the pack is enabled but invalid.
        private bool TryApplyClipPackOverride(
            InteractionAnimationManifest.BodyManifest body,
            RuntimeAnimatorController shellController,
            ref RuntimeAnimatorController controllerToApply,
            out string reason)
        {
            reason = string.Empty;

            InteractionAnimationManifest.ClipPackManifest pack = body.clipPack;
            if (pack == null || !pack.enabled)
                return true;

            if (string.IsNullOrWhiteSpace(pack.bundleFileName) ||
                pack.overrides == null || pack.overrides.Length == 0)
            {
                reason = "live_body.clip_pack_invalid_manifest";
                return false;
            }

            // Resolve first so a loaded clip pack cannot bypass AssetRootPath confinement.
            if (!TryResolveBundlePath(
                    pack.bundleFileName,
                    context.AssetRootPath,
                    out string bundlePath,
                    out reason))
            {
                return false;
            }

            if (!string.IsNullOrWhiteSpace(pack.bundleInternalName))
            {
                foreach (AssetBundle loadedBundle in AssetBundle.GetAllLoadedAssetBundles())
                {
                    if (loadedBundle != null &&
                        string.Equals(loadedBundle.name, pack.bundleInternalName, StringComparison.OrdinalIgnoreCase))
                    {
                        clipPackBundle = loadedBundle;
                        ownsClipPackBundle = false;
                        break;
                    }
                }
            }

            if (clipPackBundle == null)
            {
                if (string.IsNullOrWhiteSpace(bundlePath) || !File.Exists(bundlePath))
                {
                    reason = "live_body.clip_pack_bundle_missing:" + pack.bundleFileName;
                    context.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] live_body.clip_pack_bundle_missing: " +
                        $"handle={context.Handle} file='{pack.bundleFileName}' resolvedPath='{bundlePath}'.");
                    return false;
                }

                clipPackBundle = AssetBundle.LoadFromFile(bundlePath);
                if (clipPackBundle == null)
                {
                    reason = "live_body.clip_pack_bundle_load_failed:" + bundlePath;
                    return false;
                }

                ownsClipPackBundle = true;
            }

            var overrideController = new AnimatorOverrideController(shellController);
            int applied = 0;
            for (int i = 0; i < pack.overrides.Length; i++)
            {
                InteractionAnimationManifest.ClipOverrideManifest entry = pack.overrides[i];
                if (entry == null || string.IsNullOrWhiteSpace(entry.slot) || string.IsNullOrWhiteSpace(entry.clip))
                    continue;

                AnimationClip packClip = clipPackBundle.LoadAsset<AnimationClip>(entry.clip);
                if (packClip == null)
                {
                    reason = "live_body.clip_pack_clip_missing:" + entry.clip;
                    context.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] live_body.clip_pack_clip_missing: " +
                        $"handle={context.Handle} clip='{entry.clip}' bundle='{pack.bundleFileName}'.");
                    return false;
                }

                overrideController[entry.slot] = packClip;
                applied++;
                context.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.clip_pack_slot_overridden: " +
                    $"handle={context.Handle} slot='{entry.slot}' clip='{packClip.name}' clipLength={packClip.length:0.###}.");
            }

            if (applied == 0)
            {
                reason = "live_body.clip_pack_no_overrides_applied";
                return false;
            }

            controllerToApply = overrideController;
            context.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.clip_pack_applied: " +
                $"handle={context.Handle} bundle='{pack.bundleFileName}' overriddenSlots={applied}.");
            return true;
        }
        private void ApplyLayerWeights()
        {
            InteractionAnimationManifest.BodyManifest body = context?.Manifest?.body;
            if (body == null || bodyAnimator == null)
                return;

            float startWeight = Mathf.Clamp01(body.startLayerWeight <= 0f ? 1f : body.startLayerWeight);
            float weight = body.layerWeightRampSeconds > 0f
                ? Mathf.Lerp(startWeight, 1f, Mathf.Clamp01(elapsedSeconds / body.layerWeightRampSeconds))
                : 1f;

            float fullBodyWeight = body.fullBodyLayerWeight >= 0f
                ? Mathf.Clamp01(body.fullBodyLayerWeight)
                : weight;
            float firstPersonWeight = weight;

            if (body.enterLayerFadeSeconds > 0f)
            {
                float enterT = Mathf.Clamp01(elapsedSeconds / body.enterLayerFadeSeconds);
                fullBodyWeight *= enterT;
                firstPersonWeight *= enterT;
            }

            if (!exitRequested && body.naturalEndLayerFadeSeconds > 0f &&
                context.Manifest.durationSeconds > 0f)
            {
                float remaining = context.Manifest.durationSeconds - elapsedSeconds;
                float endT = Mathf.Clamp01(remaining / body.naturalEndLayerFadeSeconds);
                fullBodyWeight *= endT;
                firstPersonWeight *= endT;
            }

            // Fade custom layers to zero before the vanilla controller is restored. Previously
            // the exit transition reached the curve-empty Neutral state at weight 1, producing
            // one frame of extreme/invalid IK immediately before teardown.
            if (exitRequested)
            {
                float exitT = exitDurationSeconds > 1e-5f
                    ? Mathf.Clamp01(exitElapsedSeconds / exitDurationSeconds)
                    : 1f;
                fullBodyWeight = Mathf.Lerp(exitStartFullBodyWeight, 0f, exitT);
                firstPersonWeight = Mathf.Lerp(exitStartFirstPersonWeight, 0f, exitT);
            }

            SetLayerWeightIfValid(fullBodyLayerIndex, fullBodyWeight);
            SetLayerWeightIfValid(firstPersonLayerIndex, firstPersonWeight);
        }

        private string RestoreAnimator(out AnimatorStateRestoreMode restoreStateMode)
        {
            restoreStateMode = InteractionAnimationApiRestoreDiagnostics.ReadRestoreStateMode();
            if (bodyAnimator == null || snapshot == null)
                return "no_snapshot";

            try
            {
                InteractionAnimationManifest.BodyManifest body = context?.Manifest?.body;
                if (body != null)
                {
                    SetBoolIfExists(body.activeBool, false);
                    FireTriggerIfExists(body.exitTrigger);
                }

                bool scopedRestore = body != null;

                // Equip-while-crouched then stand (or the reverse) makes the captured base-layer
                // state a stale stance pose. Skip its forced replay and let the locomotion
                // parameters — synced to CURRENT player state before the replay evaluates —
                // drive layer 0 instead. Other layers restore as usual.
                bool currentCrouching = false;
                bool currentCrouchingKnown = false;
                try
                {
                    GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
                    if (player != null)
                    {
                        currentCrouching = player.isCrouching;
                        currentCrouchingKnown = true;
                    }
                }
                catch { }

                bool stanceMismatch =
                    snapshot.CapturedCrouching.HasValue &&
                    currentCrouchingKnown &&
                    currentCrouching != snapshot.CapturedCrouching.Value;
                if (stanceMismatch)
                {
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.locomotion] stance_changed_during_session: " +
                        $"frame={Time.frameCount} handle={context.Handle} " +
                        $"capturedCrouching={snapshot.CapturedCrouching} " +
                        "action='skip_base_layer_state_replay'.");
                }

                bool restored = snapshot.Restore(
                    bodyAnimator,
                    appliedController,
                    rebindAnimator: !scopedRestore,
                    restoreMode: restoreStateMode,
                    syncParametersBeforeStateReplay:
                        () => SyncVanillaLocomotionParameters("stop_pre_state_replay"),
                    restoreBaseLayerState: !stanceMismatch);

                // When the captured base-layer state was not replayed (Fresh restore mode, or
                // a stance mismatch skipped the replay), the restored vanilla controller sits
                // in its default (standing) base state. Vanilla only fires "startCrouching" on
                // the crouch input edge — already consumed — so a player still crouched at stop
                // would keep a standing base pose indefinitely, and the next session would then
                // snapshot that broken pose as its baseline. Re-assert the crouch entry so the
                // restored base layer matches the live stance.
                if (restored && currentCrouchingKnown && currentCrouching &&
                    (stanceMismatch || restoreStateMode == AnimatorStateRestoreMode.Fresh))
                {
                    FireTriggerIfExists(VanillaStartCrouchingTrigger);
                    try { bodyAnimator.Update(0f); } catch { }
                    context?.Logger?.LogInfo(
                        "[RestoreSeam.locomotion] crouch_entry_asserted: " +
                        $"frame={Time.frameCount} handle={context.Handle} phase='stop' " +
                        $"stanceMismatch={stanceMismatch} " +
                        $"restoreStateMode='{FormatRestoreStateMode(restoreStateMode)}' " +
                        "action='fire_startCrouching_on_restored_base_layer'.");
                }

                return restored ? "restored" : "controller_changed_externally";
            }
            catch (Exception exception)
            {
                return "restore_exception:" + exception.Message;
            }
        }

        private static string FormatRestoreStateMode(AnimatorStateRestoreMode restoreStateMode)
        {
            switch (restoreStateMode)
            {
                case AnimatorStateRestoreMode.Crossfade:
                    return "crossfade";
                case AnimatorStateRestoreMode.Replay:
                    return "replay";
                default:
                    return "fresh";
            }
        }

        private void SuppressLiveRigBuilders()
        {
            Transform playerRoot = context?.Request?.Player != null
                ? context.Request.Player.transform
                : null;
            if (playerRoot == null)
                return;

            Behaviour[] behaviours = playerRoot.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.enabled || !IsRigBuilderComponent(behaviour))
                    continue;

                suppressedRigBuilders.Add(new RigBuilderState(behaviour));
                behaviour.enabled = false;
            }

            if (suppressedRigBuilders.Count > 0)
            {
                context.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.rig_suppressed: " +
                    $"handle={context.Handle} rigBuilders={suppressedRigBuilders.Count}.");
            }
        }

        private void RestoreLiveRigBuilders()
        {
            for (int i = suppressedRigBuilders.Count - 1; i >= 0; i--)
                suppressedRigBuilders[i].Restore();

            suppressedRigBuilders.Clear();
        }

        // Swapping runtimeAnimatorController destroys the Animation Rigging playable graph,
        // and the authored clips drive IK targets that only move arms through that graph.
        // RigBuilder.Build() must run after every controller change (apply and restore).
        private void RebuildRigBuilders(string phase)
        {
            Transform playerRoot = context?.Request?.Player != null
                ? context.Request.Player.transform
                : null;
            if (playerRoot == null)
                return;

            int rebuilt = 0;
            Behaviour[] behaviours = playerRoot.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || !IsRigBuilderComponent(behaviour))
                    continue;

                try
                {
                    System.Reflection.MethodInfo buildMethod =
                        behaviour.GetType().GetMethod("Build", Type.EmptyTypes);
                    if (buildMethod != null)
                    {
                        buildMethod.Invoke(behaviour, null);
                        rebuilt++;
                    }
                }
                catch (Exception exception)
                {
                    context?.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] live_body.rig_rebuild_failed: " +
                        $"phase='{phase}' rigBuilder='{behaviour.name}' reason='{exception.Message}'.");
                }
            }

            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.rig_rebuilt: " +
                $"handle={(context != null ? context.Handle.ToString() : "<none>")} phase='{phase}' rigBuilders={rebuilt}.");
        }

        private void EvaluateRigBuilders(string phase)
        {
            Transform playerRoot = context?.Request?.Player != null
                ? context.Request.Player.transform
                : null;
            if (playerRoot == null)
                return;

            int evaluated = 0;
            Behaviour[] behaviours = playerRoot.GetComponentsInChildren<Behaviour>(true);
            for (int i = 0; i < behaviours.Length; i++)
            {
                Behaviour behaviour = behaviours[i];
                if (behaviour == null || !behaviour.isActiveAndEnabled ||
                    !IsRigBuilderComponent(behaviour))
                {
                    continue;
                }

                try
                {
                    System.Reflection.MethodInfo evaluateMethod =
                        behaviour.GetType().GetMethod(
                            "Evaluate",
                            System.Reflection.BindingFlags.Instance |
                            System.Reflection.BindingFlags.Public,
                            null,
                            new[] { typeof(float) },
                            null);
                    if (evaluateMethod == null)
                    {
                        if (!rigEvaluateMethodMissingLogged)
                        {
                            rigEvaluateMethodMissingLogged = true;
                            context?.Logger?.LogWarning(
                                "[RestoreSeam.rigeval] evaluate_method_missing: " +
                                $"phase='{phase}' rigBuilder='{behaviour.name}'.");
                        }
                        continue;
                    }

                    evaluateMethod.Invoke(behaviour, new object[] { 0f });
                    evaluated++;
                }
                catch (Exception exception)
                {
                    context?.Logger?.LogWarning(
                        "[RestoreSeam.rigeval] evaluate_failed: " +
                        $"phase='{phase}' rigBuilder='{behaviour.name}' reason='{exception.Message}'.");
                }
            }

            context?.Logger?.LogInfo(
                "[RestoreSeam.rigeval] evaluated: " +
                $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                $"phase='{phase}' rigBuilders={evaluated}.");
        }

        private void ResolveDiagnosticTransforms()
        {
            Transform armsMetarig = null;
            try
            {
                armsMetarig = context?.Request?.Player != null
                    ? context.Request.Player.playerModelArmsMetarig
                    : null;
            }
            catch { }

            Transform searchRoot = armsMetarig != null
                ? armsMetarig
                : (bodyAnimator != null ? bodyAnimator.transform : null);
            if (searchRoot == null)
                return;

            rightArmIkTarget = FindChildRecursive(searchRoot, "ArmsRightArm_target");
            leftArmIkTarget = FindChildRecursive(searchRoot, "ArmsLeftArm_target");
            rightHandBone = FindChildRecursive(searchRoot, "hand.R");
            rightShoulderBone = FindChildRecursive(searchRoot, "shoulder.R");

            bool armsUnderAnimator = bodyAnimator != null && armsMetarig != null &&
                armsMetarig.IsChildOf(bodyAnimator.transform);
            string armsRelativePath = GetRelativePath(
                bodyAnimator != null ? bodyAnimator.transform : null,
                armsMetarig);
            context?.Logger?.LogInfo(
                "[LCInteractionAnimationAPI] live_body.diagnostic_targets: " +
                $"handle={context.Handle} armsMetarig='{(armsMetarig != null ? armsMetarig.name : "<null>")}' " +
                $"armsMetarigUnderAnimator={armsUnderAnimator} armsRelativePath='{armsRelativePath}' " +
                $"rightArmIkTarget={(rightArmIkTarget != null)} leftArmIkTarget={(leftArmIkTarget != null)} " +
                $"rightHandBone={(rightHandBone != null)} rightShoulderBone={(rightShoulderBone != null)}.");
        }

        private static string GetRelativePath(Transform root, Transform target)
        {
            if (root == null || target == null || !target.IsChildOf(root))
                return "<unresolved>";

            var segments = new List<string>();
            Transform current = target;
            while (current != null && current != root && segments.Count < 32)
            {
                segments.Add(current.name);
                current = current.parent;
            }

            segments.Reverse();
            return string.Join("/", segments.ToArray());
        }

        private void LogFrameDiagnostics()
        {
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreSeamFrameLoggerEnabled ||
                context?.Logger == null ||
                elapsedSeconds < nextDiagnosticsAtSeconds)
                return;

            nextDiagnosticsAtSeconds = elapsedSeconds + 0.25f;
            string fullBodyState = DescribeLayerState(fullBodyLayerIndex);
            string firstPersonState = DescribeLayerState(firstPersonLayerIndex);

            Camera camera = null;
            SkinnedMeshRenderer armsRenderer = null;
            try
            {
                camera = context.Request?.Player != null ? context.Request.Player.gameplayCamera : null;
                armsRenderer = context.Request?.Player != null ? context.Request.Player.thisPlayerModelArms : null;
            }
            catch { }

            string handViewport = "<null>";
            if (rightHandBone != null && camera != null)
            {
                Vector3 viewport = camera.WorldToViewportPoint(rightHandBone.position);
                handViewport = $"({viewport.x:0.##},{viewport.y:0.##},{viewport.z:0.##})";
            }

            context.Logger.LogInfo(
                "[LCInteractionAnimationAPI] live_body.frame: " +
                $"handle={context.Handle} elapsed={elapsedSeconds:0.###} " +
                $"fullBodyLayer={fullBodyLayerIndex} fullBodyWeight={GetLayerWeight(fullBodyLayerIndex):0.###} fullBodyState={fullBodyState} " +
                $"firstPersonArmsLayer={firstPersonLayerIndex} firstPersonArmsWeight={GetLayerWeight(firstPersonLayerIndex):0.###} firstPersonArmsState={firstPersonState} " +
                $"rightTargetLocalPos={DescribeLocalPosition(rightArmIkTarget)} " +
                $"leftTargetLocalPos={DescribeLocalPosition(leftArmIkTarget)} " +
                $"rightShoulderLocalEuler={DescribeLocalEuler(rightShoulderBone)} " +
                $"rightHandLocalEuler={DescribeLocalEuler(rightHandBone)} " +
                $"rightHandViewport={handViewport} " +
                $"armsRendererEnabled={(armsRenderer != null && armsRenderer.enabled)} " +
                $"armsRendererVisible={(armsRenderer != null && armsRenderer.isVisible)}.");

            LogCalibrationSample(camera);
        }

        // Emits paired camera-space / parent-local-space samples for the IK targets.
        // The authoring baker uses these to derive the rigid transform between the
        // gameplay camera and the target parent spaces, so retargeted wrist
        // trajectories can be baked into IK-target curves offline.
        private void LogCalibrationSample(Camera camera)
        {
            if (camera == null || (rightArmIkTarget == null && leftArmIkTarget == null))
                return;

            context.Logger.LogInfo(
                "[LCInteractionAnimationAPI] live_body.calibration: " +
                $"handle={context.Handle} elapsed={elapsedSeconds:0.###} " +
                $"rightTarget={DescribeCalibration(camera, rightArmIkTarget)} " +
                $"leftTarget={DescribeCalibration(camera, leftArmIkTarget)} " +
                $"rightParent={DescribeParentInCameraSpace(camera, rightArmIkTarget)} " +
                $"leftParent={DescribeParentInCameraSpace(camera, leftArmIkTarget)}.");
        }

        private static string DescribeCalibration(Camera camera, Transform target)
        {
            if (target == null)
                return "<null>";

            Vector3 cameraPos = camera.transform.InverseTransformPoint(target.position);
            Vector3 cameraEuler = (Quaternion.Inverse(camera.transform.rotation) * target.rotation).eulerAngles;
            Vector3 localPos = target.localPosition;
            Vector3 localEuler = target.localEulerAngles;
            return $"[camPos=({cameraPos.x:0.####},{cameraPos.y:0.####},{cameraPos.z:0.####}) " +
                   $"camEuler=({cameraEuler.x:0.##},{cameraEuler.y:0.##},{cameraEuler.z:0.##}) " +
                   $"localPos=({localPos.x:0.####},{localPos.y:0.####},{localPos.z:0.####}) " +
                   $"localEuler=({localEuler.x:0.##},{localEuler.y:0.##},{localEuler.z:0.##})]";
        }

        private static string DescribeParentInCameraSpace(Camera camera, Transform target)
        {
            Transform parent = target != null ? target.parent : null;
            if (parent == null)
                return "<null>";

            Vector3 cameraPos = camera.transform.InverseTransformPoint(parent.position);
            Vector3 cameraEuler = (Quaternion.Inverse(camera.transform.rotation) * parent.rotation).eulerAngles;
            Vector3 lossyScale = parent.lossyScale;
            return $"[name='{parent.name}' camPos=({cameraPos.x:0.####},{cameraPos.y:0.####},{cameraPos.z:0.####}) " +
                   $"camEuler=({cameraEuler.x:0.##},{cameraEuler.y:0.##},{cameraEuler.z:0.##}) " +
                   $"lossyScale=({lossyScale.x:0.####},{lossyScale.y:0.####},{lossyScale.z:0.####})]";
        }

        // Coordinator Tick runs in the plugin's LateUpdate. Sampling again from
        // Application.onBeforeRender captures the transform chain after every LateUpdate and
        // any consumer postfixes, which is the pose the player actually sees.
        private void StartTransformChainDiagnostics()
        {
            StopTransformChainDiagnostics();
            if (!InteractionAnimationApiRestoreDiagnostics.RestoreSeamFrameLoggerEnabled)
                return;

            nextTransformChainDiagnosticsAtSeconds = 0f;
            lastTransformChainDiagnosticsFrame = -1;
            try
            {
                Application.onBeforeRender += LogFinalFrameTransformChainDiagnostic;
                transformChainDiagnosticsSubscribed = true;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.transform_chain_subscribe_failed: " +
                    exception.Message);
            }
        }

        private void StopTransformChainDiagnostics()
        {
            if (!transformChainDiagnosticsSubscribed)
                return;

            try { Application.onBeforeRender -= LogFinalFrameTransformChainDiagnostic; }
            catch { }
            transformChainDiagnosticsSubscribed = false;
        }

        // A consumer-owned camera presentation also owns the rendered local
        // player. Sample after every LateUpdate writer, but emit only the first state and
        // eligibility changes so an intermittent arms/visor leak is diagnosable without the
        // full restore-seam frame logger or per-frame log volume.
        private void StartExternalCameraPresentationDiagnostics()
        {
            StopExternalCameraPresentationDiagnostics();
            if (!ConsumerOwnsCameraPresentation ||
                !InteractionAnimationApiRestoreDiagnostics
                    .ExternalCameraPresentationLoggerEnabled)
            {
                return;
            }

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || !IsLocalPlayer(player))
                return;

            CacheExternalCameraRendererProbes(player);
            lastExternalCameraPresentationFrame = -1;
            lastExternalCameraPresentationSignature = 0;
            hasExternalCameraPresentationSignature = false;
            externalCameraPresentationSampleFailureLogged = false;
            try
            {
                Application.onBeforeRender += LogExternalCameraPresentationState;
                externalCameraPresentationDiagnosticsSubscribed = true;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] " +
                    "live_body.external_camera_presentation_subscribe_failed: " +
                    $"handle={context.Handle} error='{exception.Message}'.");
                StopExternalCameraPresentationDiagnostics();
            }
        }

        private void StopExternalCameraPresentationDiagnostics()
        {
            if (externalCameraPresentationDiagnosticsSubscribed)
            {
                try
                {
                    Application.onBeforeRender -= LogExternalCameraPresentationState;
                }
                catch { }
            }

            externalCameraPresentationDiagnosticsSubscribed = false;
            externalCameraRendererProbes.Clear();
            lastExternalCameraPresentationFrame = -1;
            lastExternalCameraPresentationSignature = 0;
            hasExternalCameraPresentationSignature = false;
            externalCameraPresentationSampleFailureLogged = false;
        }

        private void CacheExternalCameraRendererProbes(
            GameNetcodeStuff.PlayerControllerB player)
        {
            externalCameraRendererProbes.Clear();
            if (player == null || player.transform == null)
                return;

            var seen = new HashSet<Renderer>();
            Transform playerRoot = player.transform;
            Transform armsRoot = FindChildRecursive(
                playerRoot,
                "ScavengerModelArmsOnly");
            AddExternalCameraRendererSubtree(
                player,
                armsRoot,
                "first_person_arms",
                seen);
            try
            {
                AddExternalCameraRendererProbe(
                    player,
                    player.thisPlayerModelArms,
                    "first_person_arms",
                    seen);
            }
            catch { }

            Transform localVisor = null;
            try { localVisor = player.localVisor; } catch { }
            AddExternalCameraRendererSubtree(
                player,
                localVisor,
                "local_visor",
                seen);

            try
            {
                AddExternalCameraRendererProbe(
                    player,
                    player.thisPlayerModel,
                    "world_body",
                    seen);
                AddExternalCameraRendererProbe(
                    player,
                    player.thisPlayerModelLOD1,
                    "world_body",
                    seen);
                AddExternalCameraRendererProbe(
                    player,
                    player.thisPlayerModelLOD2,
                    "world_body",
                    seen);
            }
            catch { }

            Transform bodyRoot = FindChildRecursive(playerRoot, "ScavengerModel");
            AddExternalCameraRendererSubtree(
                player,
                bodyRoot,
                "world_body",
                seen);
            externalCameraRendererProbes.Sort(CompareExternalCameraRendererProbes);
        }

        private void AddExternalCameraRendererSubtree(
            GameNetcodeStuff.PlayerControllerB player,
            Transform root,
            string role,
            HashSet<Renderer> seen)
        {
            if (root == null)
                return;

            try
            {
                Renderer[] renderers =
                    root.GetComponentsInChildren<Renderer>(includeInactive: true);
                for (int i = 0; i < renderers.Length; i++)
                {
                    AddExternalCameraRendererProbe(
                        player,
                        renderers[i],
                        role,
                        seen);
                }
            }
            catch { }
        }

        private void AddExternalCameraRendererProbe(
            GameNetcodeStuff.PlayerControllerB player,
            Renderer renderer,
            string role,
            HashSet<Renderer> seen)
        {
            if (renderer == null || seen == null || !seen.Add(renderer))
                return;

            string path = GetRelativePath(
                player != null ? player.transform : null,
                renderer.transform);
            externalCameraRendererProbes.Add(
                new ExternalCameraRendererProbe(renderer, role, path));
        }

        private static int CompareExternalCameraRendererProbes(
            ExternalCameraRendererProbe left,
            ExternalCameraRendererProbe right)
        {
            int roleComparison = string.CompareOrdinal(left?.Role, right?.Role);
            return roleComparison != 0
                ? roleComparison
                : string.CompareOrdinal(left?.Path, right?.Path);
        }

        private void LogExternalCameraPresentationState()
        {
            if (!externalCameraPresentationDiagnosticsSubscribed)
                return;

            try
            {
                if (!active || !ConsumerOwnsCameraPresentation ||
                    !InteractionAnimationApiRestoreDiagnostics
                        .ExternalCameraPresentationLoggerEnabled)
                {
                    StopExternalCameraPresentationDiagnostics();
                    return;
                }

                if (Time.frameCount == lastExternalCameraPresentationFrame)
                    return;
                lastExternalCameraPresentationFrame = Time.frameCount;

                GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
                if (player == null)
                    return;

                Camera camera = null;
                try { camera = player.gameplayCamera; } catch { }
                int signature = ComputeExternalCameraPresentationSignature(
                    camera,
                    out int bodyRenderEligible,
                    out int armsRenderEligible,
                    out int visorRenderEligible,
                    out int bodyVisible,
                    out int armsVisible,
                    out int visorVisible,
                    out int rendererReadFailures);
                if (hasExternalCameraPresentationSignature &&
                    signature == lastExternalCameraPresentationSignature)
                {
                    return;
                }

                bool initialSample = !hasExternalCameraPresentationSignature;
                lastExternalCameraPresentationSignature = signature;
                hasExternalCameraPresentationSignature = true;

                int scavengerModelCount = CountNamedTransforms(
                    player.transform,
                    "ScavengerModel");
                int activeScenePlayerCount = CountActiveScenePlayerControllers();
                bool animatorMatchesPlayer = false;
                try
                {
                    animatorMatchesPlayer =
                        ReferenceEquals(player.playerBodyAnimator, bodyAnimator);
                }
                catch { }

                bool invariantPassed =
                    camera != null &&
                    scavengerModelCount == 1 &&
                    animatorMatchesPlayer &&
                    bodyRenderEligible > 0 &&
                    armsRenderEligible == 0 &&
                    visorRenderEligible == 0 &&
                    rendererReadFailures == 0;
                string eventName = invariantPassed
                    ? "live_body.external_camera_presentation_state"
                    : "live_body.external_camera_presentation_invariant_failed";
                string message =
                    "[LCInteractionAnimationAPI] " + eventName + ": " +
                    $"handle={context.Handle} " +
                    $"interaction='{context.Manifest?.interactionId ?? "<none>"}' " +
                    $"frame={Time.frameCount} " +
                    $"sample='{(initialSample ? "initial" : "state_changed")}' " +
                    $"invariantPassed={invariantPassed} " +
                    $"activeScenePlayerCount={activeScenePlayerCount} " +
                    $"scavengerModelCountUnderPlayer={scavengerModelCount} " +
                    $"animatorMatchesPlayer={animatorMatchesPlayer} " +
                    $"camera='{(camera != null ? camera.name : "<null>")}' " +
                    $"cameraCullingMask={(camera != null ? "0x" + camera.cullingMask.ToString("X8") : "<unavailable>")} " +
                    $"bodyRenderEligible={bodyRenderEligible} bodyVisible={bodyVisible} " +
                    $"armsRenderEligible={armsRenderEligible} armsVisible={armsVisible} " +
                    $"visorRenderEligible={visorRenderEligible} visorVisible={visorVisible} " +
                    $"rendererReadFailures={rendererReadFailures} " +
                    $"renderers={DescribeExternalCameraRenderers(player, camera)}.";
                if (invariantPassed)
                {
                    context.Logger?.LogInfo(message);
                }
                else
                {
                    context.Logger?.LogWarning(message);
                }
            }
            catch (Exception exception)
            {
                if (!externalCameraPresentationSampleFailureLogged)
                {
                    externalCameraPresentationSampleFailureLogged = true;
                    context?.Logger?.LogWarning(
                        "[LCInteractionAnimationAPI] " +
                        "live_body.external_camera_presentation_sample_failed: " +
                        $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                        $"error='{exception.Message}'.");
                }
            }
        }

        private int ComputeExternalCameraPresentationSignature(
            Camera camera,
            out int bodyRenderEligible,
            out int armsRenderEligible,
            out int visorRenderEligible,
            out int bodyVisible,
            out int armsVisible,
            out int visorVisible,
            out int rendererReadFailures)
        {
            bodyRenderEligible = 0;
            armsRenderEligible = 0;
            visorRenderEligible = 0;
            bodyVisible = 0;
            armsVisible = 0;
            visorVisible = 0;
            rendererReadFailures = 0;

            unchecked
            {
                int signature = 17;
                signature = signature * 31 + (camera != null ? camera.cullingMask : 0);
                signature = signature * 31 + externalCameraRendererProbes.Count;
                for (int i = 0; i < externalCameraRendererProbes.Count; i++)
                {
                    ExternalCameraRendererProbe probe = externalCameraRendererProbes[i];
                    ExternalCameraRendererState state =
                        CaptureExternalCameraRendererState(probe?.Renderer, camera);
                    signature = signature * 31 + (probe?.InstanceId ?? 0);
                    signature = signature * 31 + state.Signature;
                    if (state.ReadFailed)
                        rendererReadFailures++;

                    if (string.Equals(
                        probe?.Role,
                        "first_person_arms",
                        StringComparison.Ordinal))
                    {
                        if (state.RenderEligible) armsRenderEligible++;
                        if (state.IsVisible) armsVisible++;
                    }
                    else if (string.Equals(
                        probe?.Role,
                        "local_visor",
                        StringComparison.Ordinal))
                    {
                        if (state.RenderEligible) visorRenderEligible++;
                        if (state.IsVisible) visorVisible++;
                    }
                    else
                    {
                        if (state.RenderEligible) bodyRenderEligible++;
                        if (state.IsVisible) bodyVisible++;
                    }
                }

                return signature;
            }
        }

        private static ExternalCameraRendererState
            CaptureExternalCameraRendererState(Renderer renderer, Camera camera)
        {
            var state = new ExternalCameraRendererState();
            if (renderer == null)
                return state;

            try
            {
                state.Present = true;
                state.ActiveInHierarchy = renderer.gameObject.activeInHierarchy;
                state.Enabled = renderer.enabled;
                state.ForceRenderingOff = renderer.forceRenderingOff;
                state.IsVisible = renderer.isVisible;
                state.Layer = renderer.gameObject.layer;
                state.ShadowCastingMode = renderer.shadowCastingMode;
                state.CameraDrawsLayer =
                    camera == null ||
                    (camera.cullingMask & (1 << state.Layer)) != 0;
                state.RenderEligible =
                    state.ActiveInHierarchy &&
                    state.Enabled &&
                    !state.ForceRenderingOff &&
                    state.CameraDrawsLayer &&
                    state.ShadowCastingMode != ShadowCastingMode.ShadowsOnly;
            }
            catch
            {
                state.ReadFailed = true;
            }

            return state;
        }

        private string DescribeExternalCameraRenderers(
            GameNetcodeStuff.PlayerControllerB player,
            Camera camera)
        {
            if (externalCameraRendererProbes.Count == 0)
                return "<none>";

            var description = new StringBuilder(512);
            for (int i = 0; i < externalCameraRendererProbes.Count; i++)
            {
                ExternalCameraRendererProbe probe = externalCameraRendererProbes[i];
                Renderer renderer = probe?.Renderer;
                ExternalCameraRendererState state =
                    CaptureExternalCameraRendererState(renderer, camera);
                if (i > 0)
                    description.Append(" | ");

                description.Append("[role='")
                    .Append(probe?.Role ?? "<null>")
                    .Append("' id=")
                    .Append(probe?.InstanceId ?? 0)
                    .Append(" path='")
                    .Append(SanitizeExternalCameraLogValue(probe?.Path))
                    .Append("' present=")
                    .Append(state.Present)
                    .Append(" active=")
                    .Append(state.ActiveInHierarchy)
                    .Append(" enabled=")
                    .Append(state.Enabled)
                    .Append(" forceOff=")
                    .Append(state.ForceRenderingOff)
                    .Append(" shadow=")
                    .Append(state.ShadowCastingMode)
                    .Append(" isVisible=")
                    .Append(state.IsVisible)
                    .Append(" layer=")
                    .Append(state.Layer)
                    .Append(" cameraDrawsLayer=")
                    .Append(state.CameraDrawsLayer)
                    .Append(" renderEligible=")
                    .Append(state.RenderEligible)
                    .Append(" readFailed=")
                    .Append(state.ReadFailed);

                if (renderer != null)
                {
                    try
                    {
                        Bounds bounds = renderer.bounds;
                        description.Append(" boundsCenter=")
                            .Append(DescribeExternalCameraVector(bounds.center))
                            .Append(" boundsSize=")
                            .Append(DescribeExternalCameraVector(bounds.size));
                        if (renderer is SkinnedMeshRenderer skinned)
                        {
                            description.Append(" rootBone='")
                                .Append(SanitizeExternalCameraLogValue(
                                    GetRelativePath(
                                        player != null ? player.transform : null,
                                        skinned.rootBone)))
                                .Append("'");
                        }
                    }
                    catch { }
                }

                description.Append(']');
            }

            return description.ToString();
        }

        private static int CountNamedTransforms(Transform root, string name)
        {
            if (root == null)
                return 0;

            int count = string.Equals(root.name, name, StringComparison.Ordinal) ? 1 : 0;
            for (int i = 0; i < root.childCount; i++)
                count += CountNamedTransforms(root.GetChild(i), name);
            return count;
        }

        private static int CountActiveScenePlayerControllers()
        {
            int count = 0;
            try
            {
                GameNetcodeStuff.PlayerControllerB[] players =
                    Resources.FindObjectsOfTypeAll<GameNetcodeStuff.PlayerControllerB>();
                for (int i = 0; i < players.Length; i++)
                {
                    GameNetcodeStuff.PlayerControllerB player = players[i];
                    if (player != null && player.gameObject.scene.IsValid())
                        count++;
                }
            }
            catch { }
            return count;
        }

        private static string SanitizeExternalCameraLogValue(string value)
        {
            return string.IsNullOrEmpty(value)
                ? "<empty>"
                : value.Replace('\r', ' ').Replace('\n', ' ').Replace('\'', '"');
        }

        private static string DescribeExternalCameraVector(Vector3 value)
        {
            return $"({value.x:0.###},{value.y:0.###},{value.z:0.###})";
        }

        private sealed class ExternalCameraRendererProbe
        {
            internal ExternalCameraRendererProbe(
                Renderer renderer,
                string role,
                string path)
            {
                Renderer = renderer;
                Role = role;
                Path = path;
                InstanceId = renderer != null ? renderer.GetInstanceID() : 0;
            }

            internal Renderer Renderer { get; }
            internal string Role { get; }
            internal string Path { get; }
            internal int InstanceId { get; }
        }

        private struct ExternalCameraRendererState
        {
            internal bool Present;
            internal bool ActiveInHierarchy;
            internal bool Enabled;
            internal bool ForceRenderingOff;
            internal bool IsVisible;
            internal int Layer;
            internal ShadowCastingMode ShadowCastingMode;
            internal bool CameraDrawsLayer;
            internal bool RenderEligible;
            internal bool ReadFailed;

            internal int Signature
            {
                get
                {
                    unchecked
                    {
                        int signature = Present ? 1 : 0;
                        signature = signature * 31 + (ActiveInHierarchy ? 1 : 0);
                        signature = signature * 31 + (Enabled ? 1 : 0);
                        signature = signature * 31 + (ForceRenderingOff ? 1 : 0);
                        signature = signature * 31 + (IsVisible ? 1 : 0);
                        signature = signature * 31 + Layer;
                        signature = signature * 31 + (int)ShadowCastingMode;
                        signature = signature * 31 + (CameraDrawsLayer ? 1 : 0);
                        signature = signature * 31 + (RenderEligible ? 1 : 0);
                        signature = signature * 31 + (ReadFailed ? 1 : 0);
                        return signature;
                    }
                }
            }
        }

        // A visor farther than this from its camera target point was parked away
        // from the camera by a consumer (the standard hide idiom, e.g. a
        // notSpawnedPosition or down-5000 park) — never snap a parked visor
        // back into frame.
        private const float VisorHardGlueMaxTrackDistanceMeters = 2f;

        /// <summary>
        /// Visor parity (2026-07-22): vanilla glues localVisor to
        /// localVisorTargetPoint (a child of the gameplay camera) every owner
        /// LateUpdate — position hard-snap but rotation Lerp at 53 deg/s
        /// (decompiled PlayerControllerB LateUpdate). Fast scripted or
        /// clip-animated camera moves outrun that lerp and the mask edge sweeps
        /// into frame, which is why consumers historically hid the mask during
        /// every custom animation. While a local first-person live-body session is active,
        /// re-glue the visor HARD (position and rotation) from
        /// Application.onBeforeRender — after every LateUpdate writer, i.e. on
        /// the pose that actually renders — so consumers can keep the mask
        /// visible like the vanilla terminal does.
        /// </summary>
        private void StartLocalVisorHardGlue()
        {
            StopLocalVisorHardGlue();
            if (ConsumerOwnsCameraPresentation)
            {
                context?.Logger?.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.visor_glue_skipped: " +
                    $"handle={(context != null ? context.Handle.ToString() : "<none>")} " +
                    $"interaction='{context?.Manifest?.interactionId ?? "<none>"}' " +
                    "reason='local_camera_owned_externally' " +
                    "action='leave_visor_to_external_owner'.");
                return;
            }

            if (!InteractionAnimationApiRestoreDiagnostics.HardVisorGlueDuringSessionEnabled)
                return;

            GameNetcodeStuff.PlayerControllerB player = context?.Request?.Player;
            if (player == null || !IsLocalPlayer(player))
                return;

            try
            {
                visorHardGlueVisor = player.localVisor;
                visorHardGlueTarget = player.localVisorTargetPoint;
            }
            catch
            {
                visorHardGlueVisor = null;
                visorHardGlueTarget = null;
            }
            if (visorHardGlueVisor == null || visorHardGlueTarget == null)
                return;

            try
            {
                Application.onBeforeRender += ApplyLocalVisorHardGlue;
                visorHardGlueSubscribed = true;
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.visor_glue_subscribe_failed: " +
                    exception.Message);
            }
        }

        private void StopLocalVisorHardGlue()
        {
            if (visorHardGlueSubscribed)
            {
                try { Application.onBeforeRender -= ApplyLocalVisorHardGlue; }
                catch { }
            }
            visorHardGlueSubscribed = false;
            visorHardGlueVisor = null;
            visorHardGlueTarget = null;
            visorHardGlueAppliedLogged = false;
            visorHardGlueParkedLogged = false;
        }

        private void ApplyLocalVisorHardGlue()
        {
            try
            {
                if (!active || visorHardGlueVisor == null || visorHardGlueTarget == null)
                    return;

                Vector3 targetPosition = visorHardGlueTarget.position;
                float positionDelta =
                    Vector3.Distance(visorHardGlueVisor.position, targetPosition);
                if (positionDelta > VisorHardGlueMaxTrackDistanceMeters)
                {
                    if (!visorHardGlueParkedLogged)
                    {
                        visorHardGlueParkedLogged = true;
                        context?.Logger?.LogInfo(
                            "[LCInteractionAnimationAPI] live_body.visor_glue_parked: " +
                            $"handle={context.Handle} positionDelta={positionDelta:0.##}m " +
                            "action='skip_while_parked'.");
                    }
                    return;
                }

                if (!visorHardGlueAppliedLogged)
                {
                    visorHardGlueAppliedLogged = true;
                    context?.Logger?.LogInfo(
                        "[LCInteractionAnimationAPI] live_body.visor_glue_active: " +
                        $"handle={context.Handle} preGluePositionDelta={positionDelta:0.####}m " +
                        $"preGlueRotationDelta={Quaternion.Angle(visorHardGlueVisor.rotation, visorHardGlueTarget.rotation):0.##}deg.");
                }

                visorHardGlueVisor.SetPositionAndRotation(
                    targetPosition,
                    visorHardGlueTarget.rotation);
            }
            catch { }
        }

        private void LogFinalFrameTransformChainDiagnostic()
        {
            try
            {
                if (!active || context?.Logger == null ||
                    !InteractionAnimationApiRestoreDiagnostics.RestoreSeamFrameLoggerEnabled ||
                    elapsedSeconds < nextTransformChainDiagnosticsAtSeconds ||
                    Time.frameCount == lastTransformChainDiagnosticsFrame)
                    return;

                Camera camera = null;
                try
                {
                    camera = context.Request?.Player != null
                        ? context.Request.Player.gameplayCamera
                        : null;
                }
                catch { }

                if (camera == null || rightArmIkTarget == null || rightHandBone == null)
                    return;

                lastTransformChainDiagnosticsFrame = Time.frameCount;
                nextTransformChainDiagnosticsAtSeconds = elapsedSeconds + 0.25f;

                Transform cameraTransform = camera.transform;
                Transform cameraParent = cameraTransform.parent;
                Rect pixelRect = camera.pixelRect;
                Vector3 targetToHandPosition =
                    rightArmIkTarget.InverseTransformPoint(rightHandBone.position);
                Vector3 targetToHandEuler =
                    (Quaternion.Inverse(rightArmIkTarget.rotation) *
                     rightHandBone.rotation).eulerAngles;
                float targetToHandPositionError =
                    Vector3.Distance(rightArmIkTarget.position, rightHandBone.position);
                float targetToHandRotationError =
                    Quaternion.Angle(rightArmIkTarget.rotation, rightHandBone.rotation);

                context.Logger.LogInfo(
                    "[LCInteractionAnimationAPI] live_body.transform_chain: " +
                    $"handle={context.Handle} phase='before_render' frame={Time.frameCount} " +
                    $"elapsed={elapsedSeconds:0.###} " +
                    $"camera=[name='{camera.name}' parent='{(cameraParent != null ? cameraParent.name : "<null>")}' " +
                    $"fov={camera.fieldOfView:0.####} aspect={camera.aspect:0.######} " +
                    $"pixelWidth={camera.pixelWidth} pixelHeight={camera.pixelHeight} " +
                    $"pixelRect=({pixelRect.x:0.##},{pixelRect.y:0.##},{pixelRect.width:0.##},{pixelRect.height:0.##}) " +
                    $"near={camera.nearClipPlane:0.####}] " +
                    $"rightTarget={DescribeTransformInCamera(camera, rightArmIkTarget)} " +
                    $"rightHand={DescribeTransformInCamera(camera, rightHandBone)} " +
                    $"targetToHand=[position=({targetToHandPosition.x:0.######},{targetToHandPosition.y:0.######},{targetToHandPosition.z:0.######}) " +
                    $"euler=({targetToHandEuler.x:0.####},{targetToHandEuler.y:0.####},{targetToHandEuler.z:0.####}) " +
                    $"positionErrorMeters={targetToHandPositionError:0.######} " +
                    $"rotationErrorDegrees={targetToHandRotationError:0.####}] " +
                    $"prop={DescribePropTransform(camera)} " +
                    $"propBounds={DescribePropBounds(camera)} " +
                    $"propAimAxis={DescribePropAimAxis(camera)}.");
            }
            catch (Exception exception)
            {
                context?.Logger?.LogWarning(
                    "[LCInteractionAnimationAPI] live_body.transform_chain_failed: " +
                    exception.Message);
                StopTransformChainDiagnostics();
            }
        }

        private static string DescribeTransformInCamera(Camera camera, Transform transform)
        {
            if (camera == null || transform == null)
                return "<null>";

            Vector3 cameraPosition = camera.transform.InverseTransformPoint(transform.position);
            Vector3 cameraEuler =
                (Quaternion.Inverse(camera.transform.rotation) * transform.rotation).eulerAngles;
            Vector3 localPosition = transform.localPosition;
            Vector3 localEuler = transform.localEulerAngles;
            Vector3 lossyScale = transform.lossyScale;
            return $"[camPos=({cameraPosition.x:0.######},{cameraPosition.y:0.######},{cameraPosition.z:0.######}) " +
                   $"camEuler=({cameraEuler.x:0.####},{cameraEuler.y:0.####},{cameraEuler.z:0.####}) " +
                   $"localPos=({localPosition.x:0.######},{localPosition.y:0.######},{localPosition.z:0.######}) " +
                   $"localEuler=({localEuler.x:0.####},{localEuler.y:0.####},{localEuler.z:0.####}) " +
                   $"lossyScale=({lossyScale.x:0.######},{lossyScale.y:0.######},{lossyScale.z:0.######})]";
        }

        private string DescribePropTransform(Camera camera)
        {
            if (propInstance == null)
                return "<null>";

            Transform prop = propInstance.transform;
            Vector3 cameraPosition = camera.transform.InverseTransformPoint(prop.position);
            Vector3 cameraEuler =
                (Quaternion.Inverse(camera.transform.rotation) * prop.rotation).eulerAngles;
            Vector3 cameraForward =
                camera.transform.InverseTransformDirection(prop.forward).normalized;
            Vector3 localPosition = prop.localPosition;
            Vector3 localEuler = prop.localEulerAngles;
            Vector3 localScale = prop.localScale;
            Vector3 lossyScale = prop.lossyScale;
            return $"[localPos=({localPosition.x:0.######},{localPosition.y:0.######},{localPosition.z:0.######}) " +
                   $"localEuler=({localEuler.x:0.####},{localEuler.y:0.####},{localEuler.z:0.####}) " +
                   $"localScale=({localScale.x:0.######},{localScale.y:0.######},{localScale.z:0.######}) " +
                   $"lossyScale=({lossyScale.x:0.######},{lossyScale.y:0.######},{lossyScale.z:0.######}) " +
                   $"camPos=({cameraPosition.x:0.######},{cameraPosition.y:0.######},{cameraPosition.z:0.######}) " +
                   $"camEuler=({cameraEuler.x:0.####},{cameraEuler.y:0.####},{cameraEuler.z:0.####}) " +
                   $"camForward=({cameraForward.x:0.######},{cameraForward.y:0.######},{cameraForward.z:0.######})]";
        }

        private string DescribePropBounds(Camera camera)
        {
            if (propInstance == null)
                return "<null>";

            bool hasCameraBounds = TryGetRendererBoundsInSpace(
                propInstance, camera.transform, out Bounds cameraBounds);
            bool hasLocalBounds = TryGetRendererBoundsInSpace(
                propInstance, propInstance.transform, out Bounds localBounds);
            return $"[hasCameraBounds={hasCameraBounds} " +
                   $"camMin={DescribeBoundsMin(cameraBounds, hasCameraBounds)} " +
                   $"camMax={DescribeBoundsMax(cameraBounds, hasCameraBounds)} " +
                   $"hasLocalBounds={hasLocalBounds} " +
                   $"localMin={DescribeBoundsMin(localBounds, hasLocalBounds)} " +
                   $"localMax={DescribeBoundsMax(localBounds, hasLocalBounds)}]";
        }

        private string DescribePropAimAxis(Camera camera)
        {
            if (propInstance == null)
                return "<null>";

            bool hasLocalBounds = TryGetRendererBoundsInSpace(
                propInstance, propInstance.transform, out Bounds localBounds);
            float axisLength = hasLocalBounds ? Mathf.Max(0.25f, localBounds.max.z) : 0.5f;
            Vector3 localOrigin = Vector3.zero;
            Vector3 localTip = new Vector3(0f, 0f, axisLength);
            Vector3 worldOrigin = propInstance.transform.TransformPoint(localOrigin);
            Vector3 worldTip = propInstance.transform.TransformPoint(localTip);
            Vector3 cameraOrigin = camera.transform.InverseTransformPoint(worldOrigin);
            Vector3 cameraTip = camera.transform.InverseTransformPoint(worldTip);
            Vector3 viewportOrigin = camera.WorldToViewportPoint(worldOrigin);
            Vector3 viewportTip = camera.WorldToViewportPoint(worldTip);

            int pixelWidth = camera.pixelWidth > 0 ? camera.pixelWidth : Mathf.Max(1, Screen.width);
            int pixelHeight = camera.pixelHeight > 0 ? camera.pixelHeight : Mathf.Max(1, Screen.height);
            Vector2 pixelOrigin = new Vector2(
                viewportOrigin.x * pixelWidth, viewportOrigin.y * pixelHeight);
            Vector2 pixelTip = new Vector2(
                viewportTip.x * pixelWidth, viewportTip.y * pixelHeight);
            Vector2 crosshair = new Vector2(pixelWidth * 0.5f, pixelHeight * 0.5f);
            Vector2 direction = pixelTip - pixelOrigin;
            float denominator = direction.sqrMagnitude;
            float rayParameter = 0f;
            float missPixels = float.MaxValue;
            float missNormalizedHeight = float.MaxValue;
            bool pointsTowardCrosshair = false;
            if (denominator > 1e-8f)
            {
                rayParameter = Vector2.Dot(crosshair - pixelOrigin, direction) / denominator;
                Vector2 closest = pixelOrigin + direction * Mathf.Max(0f, rayParameter);
                missPixels = Vector2.Distance(crosshair, closest);
                missNormalizedHeight = missPixels / pixelHeight;
                pointsTowardCrosshair =
                    rayParameter >= 0f &&
                    viewportOrigin.z > camera.nearClipPlane &&
                    viewportTip.z > camera.nearClipPlane;
            }

            return $"[localOrigin=({localOrigin.x:0.######},{localOrigin.y:0.######},{localOrigin.z:0.######}) " +
                   $"localTip=({localTip.x:0.######},{localTip.y:0.######},{localTip.z:0.######}) " +
                   $"camOrigin=({cameraOrigin.x:0.######},{cameraOrigin.y:0.######},{cameraOrigin.z:0.######}) " +
                   $"camTip=({cameraTip.x:0.######},{cameraTip.y:0.######},{cameraTip.z:0.######}) " +
                   $"viewportOrigin=({viewportOrigin.x:0.######},{viewportOrigin.y:0.######},{viewportOrigin.z:0.######}) " +
                   $"viewportTip=({viewportTip.x:0.######},{viewportTip.y:0.######},{viewportTip.z:0.######}) " +
                   $"closestRayParameter={rayParameter:0.######} missPixels={missPixels:0.###} " +
                   $"missNormalizedHeight={missNormalizedHeight:0.######} " +
                   $"pointsTowardCrosshair={pointsTowardCrosshair}]";
        }

        private static bool TryGetRendererBoundsInSpace(
            GameObject root, Transform space, out Bounds bounds)
        {
            bounds = new Bounds();
            if (root == null || space == null)
                return false;

            bool found = false;
            Renderer[] renderers = root.GetComponentsInChildren<Renderer>(false);
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null || !renderer.enabled)
                    continue;

                Vector3 min = renderer.bounds.min;
                Vector3 max = renderer.bounds.max;
                for (int x = 0; x < 2; x++)
                for (int y = 0; y < 2; y++)
                for (int z = 0; z < 2; z++)
                {
                    Vector3 worldCorner = new Vector3(
                        x == 0 ? min.x : max.x,
                        y == 0 ? min.y : max.y,
                        z == 0 ? min.z : max.z);
                    Vector3 point = space.InverseTransformPoint(worldCorner);
                    if (!found)
                    {
                        bounds = new Bounds(point, Vector3.zero);
                        found = true;
                    }
                    else
                    {
                        bounds.Encapsulate(point);
                    }
                }
            }

            return found;
        }

        private static string DescribeBoundsMin(Bounds bounds, bool valid)
        {
            if (!valid)
                return "<null>";
            Vector3 min = bounds.min;
            return $"({min.x:0.######},{min.y:0.######},{min.z:0.######})";
        }

        private static string DescribeBoundsMax(Bounds bounds, bool valid)
        {
            if (!valid)
                return "<null>";
            Vector3 max = bounds.max;
            return $"({max.x:0.######},{max.y:0.######},{max.z:0.######})";
        }

        private string DescribeLayerState(int layerIndex)
        {
            if (bodyAnimator == null || layerIndex < 0 || layerIndex >= bodyAnimator.layerCount)
                return "<invalid_layer>";

            try
            {
                AnimatorStateInfo stateInfo = bodyAnimator.GetCurrentAnimatorStateInfo(layerIndex);
                return $"hash={stateInfo.shortNameHash} normalizedTime={stateInfo.normalizedTime:0.###}";
            }
            catch
            {
                return "<state_unavailable>";
            }
        }

        private float GetLayerWeight(int layerIndex)
        {
            if (bodyAnimator == null || layerIndex < 0 || layerIndex >= bodyAnimator.layerCount)
                return -1f;

            try { return bodyAnimator.GetLayerWeight(layerIndex); } catch { return -1f; }
        }

        private static string DescribeLocalPosition(Transform transform)
        {
            if (transform == null)
                return "<null>";

            Vector3 position = transform.localPosition;
            return $"({position.x:0.###},{position.y:0.###},{position.z:0.###})";
        }

        private static string DescribeLocalEuler(Transform transform)
        {
            if (transform == null)
                return "<null>";

            Vector3 euler = transform.localEulerAngles;
            return $"({euler.x:0.#},{euler.y:0.#},{euler.z:0.#})";
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

        private static Transform ResolvePropAttachBone(
            Transform root,
            InteractionAnimationManifest.PropManifest prop)
        {
            return PropAttachBoneResolver.Resolve(
                root,
                prop,
                (candidate, path) => candidate.Find(path),
                FindChildRecursive);
        }

        private static bool IsRigBuilderComponent(Component component)
        {
            Type type = component != null ? component.GetType() : null;
            if (type == null)
                return false;

            return string.Equals(type.Name, "RigBuilder", StringComparison.Ordinal) ||
                   string.Equals(type.FullName, "UnityEngine.Animations.Rigging.RigBuilder", StringComparison.Ordinal);
        }

        private void SetLayerWeightIfValid(int layerIndex, float weight)
        {
            if (layerIndex < 0 || bodyAnimator == null || layerIndex >= bodyAnimator.layerCount)
                return;

            try { bodyAnimator.SetLayerWeight(layerIndex, Mathf.Clamp01(weight)); } catch { }
        }

        private float GetLayerWeightOrZero(int layerIndex)
        {
            if (layerIndex < 0 || bodyAnimator == null || layerIndex >= bodyAnimator.layerCount)
                return 0f;

            try { return Mathf.Clamp01(bodyAnimator.GetLayerWeight(layerIndex)); }
            catch { return 0f; }
        }

        private void SetBoolIfExists(string parameterName, bool value)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || !HasParameter(parameterName, AnimatorControllerParameterType.Bool))
                return;

            try { bodyAnimator.SetBool(parameterName, value); } catch { }
        }

        private void FireTriggerIfExists(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
                return;

            try
            {
                bodyAnimator.ResetTrigger(parameterName);
                bodyAnimator.SetTrigger(parameterName);
            }
            catch { }
        }

        private void ResetTriggerIfExists(string parameterName)
        {
            if (string.IsNullOrWhiteSpace(parameterName) || !HasParameter(parameterName, AnimatorControllerParameterType.Trigger))
                return;

            try { bodyAnimator.ResetTrigger(parameterName); } catch { }
        }

        // Animator.parameters allocates a fresh managed array on every read, and this
        // lookup runs several times per tick from the locomotion sync plus once per
        // consumer-fired trigger (per shot on automatic weapons). Parameter presence is
        // immutable per controller, so cache it per controller instance.
        private RuntimeAnimatorController parameterCacheController;
        private readonly Dictionary<(string, AnimatorControllerParameterType), bool> parameterPresenceCache =
            new Dictionary<(string, AnimatorControllerParameterType), bool>();

        private bool HasParameter(string parameterName, AnimatorControllerParameterType parameterType)
        {
            if (bodyAnimator == null)
                return false;

            try
            {
                RuntimeAnimatorController controller = bodyAnimator.runtimeAnimatorController;
                if (!ReferenceEquals(controller, parameterCacheController))
                {
                    parameterPresenceCache.Clear();
                    parameterCacheController = controller;
                }

                (string, AnimatorControllerParameterType) key = (parameterName, parameterType);
                if (parameterPresenceCache.TryGetValue(key, out bool cached))
                    return cached;

                bool found = false;
                AnimatorControllerParameter[] parameters = bodyAnimator.parameters;
                for (int i = 0; i < parameters.Length; i++)
                {
                    if (parameters[i].type == parameterType &&
                        string.Equals(parameters[i].name, parameterName, StringComparison.Ordinal))
                    {
                        found = true;
                        break;
                    }
                }

                parameterPresenceCache[key] = found;
                return found;
            }
            catch { }

            return false;
        }

        private static int FindLayerIndex(Animator animator, string layerName)
        {
            if (animator == null || string.IsNullOrWhiteSpace(layerName))
                return -1;

            try
            {
                for (int i = 0; i < animator.layerCount; i++)
                {
                    if (string.Equals(animator.GetLayerName(i), layerName, StringComparison.Ordinal))
                        return i;
                }
            }
            catch { }

            return -1;
        }

        private void ReleaseBundle(bool retainOwnedBundles = false)
        {
            if (bundle != null && ownsBundle)
            {
                if (retainOwnedBundles)
                    RetainedBundles.Add(bundle);
                else
                    bundle.Unload(false);
            }

            bundle = null;
            ownsBundle = false;

            if (clipPackBundle != null && ownsClipPackBundle)
            {
                if (retainOwnedBundles)
                    RetainedBundles.Add(clipPackBundle);
                else
                    clipPackBundle.Unload(false);
            }

            clipPackBundle = null;
            ownsClipPackBundle = false;
        }

        internal static void ShutdownBundleCache()
        {
            foreach (AssetBundle cached in RetainedBundles)
            {
                if (cached == null)
                    continue;

                try { cached.Unload(false); } catch { }
            }

            RetainedBundles.Clear();
        }

        private void CleanupFailedStart()
        {
            StopTransformChainDiagnostics();
            StopExternalCameraPresentationDiagnostics();
            StopLocalVisorHardGlue();
            DestroyProp();
            StopLocalCameraRotationStabilizer(restoreSessionEntryRotation: true);
            StopLocalCameraPositionStabilizer(restorePosition: true, deferRelease: false);
            RestoreLiveRigBuilders();
            ReleaseBundle();
            bodyAnimator = null;
            appliedController = null;
            snapshot = null;
            rigControlPoseSnapshot = null;
            rigControlRoot = null;
            thirdPersonRigControlPoseSnapshot = null;
            scopedFirstPersonPoseSnapshot = null;
            ResetCameraDisplacementGuardState();
            context = null;
            active = false;
        }

        private static bool TryResolveBundlePath(
            string bundleFileName,
            string assetRootPath,
            out string resolvedPath,
            out string reason)
        {
            return InteractionAnimationAssetPathResolver.TryResolveBundlePath(
                bundleFileName,
                assetRootPath,
                out resolvedPath,
                out reason);
        }

        private sealed class SeamPhaseStopwatch
        {
            private readonly Stopwatch stopwatch = Stopwatch.StartNew();
            private long previousElapsedTicks;

            internal double TotalMilliseconds => stopwatch.Elapsed.TotalMilliseconds;

            internal double LapMilliseconds()
            {
                long elapsedTicks = stopwatch.ElapsedTicks;
                long phaseTicks = elapsedTicks - previousElapsedTicks;
                previousElapsedTicks = elapsedTicks;
                return phaseTicks * 1000d / Stopwatch.Frequency;
            }
        }

        private readonly struct CameraRotationSnapshot
        {
            internal Transform GameplayCameraTransform { get; }
            internal Quaternion GameplayCameraLocalRotation { get; }
            internal bool GameplayCameraCaptured { get; }
            internal Transform CameraContainerTransform { get; }
            internal Quaternion CameraContainerLocalRotation { get; }
            internal bool CameraContainerCaptured { get; }
            internal bool HasAnyRotation => GameplayCameraCaptured || CameraContainerCaptured;

            internal CameraRotationSnapshot(
                Transform gameplayCameraTransform,
                Quaternion gameplayCameraLocalRotation,
                bool gameplayCameraCaptured,
                Transform cameraContainerTransform,
                Quaternion cameraContainerLocalRotation,
                bool cameraContainerCaptured)
            {
                GameplayCameraTransform = gameplayCameraTransform;
                GameplayCameraLocalRotation = gameplayCameraLocalRotation;
                GameplayCameraCaptured = gameplayCameraCaptured;
                CameraContainerTransform = cameraContainerTransform;
                CameraContainerLocalRotation = cameraContainerLocalRotation;
                CameraContainerCaptured = cameraContainerCaptured;
            }
        }

        private readonly struct VisorPoseSnapshot
        {
            internal SeamTransformPose LocalVisor { get; }
            internal SeamTransformPose LocalVisorTargetPoint { get; }
            internal bool HasAnyPose =>
                LocalVisor.Captured || LocalVisorTargetPoint.Captured;
            internal bool HasAnyRestoreEligiblePose =>
                (LocalVisor.Captured && LocalVisor.UnderAnimatorHierarchy) ||
                (LocalVisorTargetPoint.Captured &&
                 LocalVisorTargetPoint.UnderAnimatorHierarchy);

            internal VisorPoseSnapshot(
                SeamTransformPose localVisor,
                SeamTransformPose localVisorTargetPoint)
            {
                LocalVisor = localVisor;
                LocalVisorTargetPoint = localVisorTargetPoint;
            }
        }

        private readonly struct SeamTransformPose
        {
            internal Transform Transform { get; }
            internal Vector3 LocalPosition { get; }
            internal Quaternion LocalRotation { get; }
            internal Vector3 LocalScale { get; }
            internal Vector3 WorldPosition { get; }
            internal Quaternion WorldRotation { get; }
            internal Vector3 WorldScale { get; }
            internal bool UnderAnimatorHierarchy { get; }
            internal bool Captured { get; }

            internal SeamTransformPose(
                Transform transform,
                bool underAnimatorHierarchy)
            {
                Transform = transform;
                LocalPosition = transform.localPosition;
                LocalRotation = transform.localRotation;
                LocalScale = transform.localScale;
                WorldPosition = transform.position;
                WorldRotation = transform.rotation;
                WorldScale = transform.lossyScale;
                UnderAnimatorHierarchy = underAnimatorHierarchy;
                Captured = true;
            }
        }

        private readonly struct RigBuilderState
        {
            private readonly Behaviour behaviour;

            internal RigBuilderState(Behaviour behaviour)
            {
                this.behaviour = behaviour;
            }

            internal void Restore()
            {
                if (behaviour != null)
                    behaviour.enabled = true;
            }
        }

        private sealed class TransformPoseSnapshot
        {
            private readonly TransformPose[] poses;

            internal int Count => poses.Length;

            private TransformPoseSnapshot(TransformPose[] poses)
            {
                this.poses = poses ?? Array.Empty<TransformPose>();
            }

            internal static TransformPoseSnapshot CaptureDescendants(Transform root)
            {
                return Capture(root, includeRoot: false);
            }

            internal static TransformPoseSnapshot CaptureSubtree(Transform root)
            {
                return Capture(root, includeRoot: true);
            }

            private static TransformPoseSnapshot Capture(Transform root, bool includeRoot)
            {
                if (root == null)
                    return new TransformPoseSnapshot(Array.Empty<TransformPose>());

                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                int capacity = includeRoot ? transforms.Length : Math.Max(0, transforms.Length - 1);
                var captured = new List<TransformPose>(capacity);
                for (int i = 0; i < transforms.Length; i++)
                {
                    Transform transform = transforms[i];
                    if (transform == null || (!includeRoot && transform == root))
                        continue;
                    captured.Add(new TransformPose(transform));
                }

                return new TransformPoseSnapshot(captured.ToArray());
            }

            internal int Restore()
            {
                int restored = 0;
                for (int i = 0; i < poses.Length; i++)
                {
                    if (poses[i].Restore())
                        restored++;
                }
                return restored;
            }

            internal int RestoreExcept(ISet<Transform> excludedTransforms)
            {
                int restored = 0;
                for (int i = 0; i < poses.Length; i++)
                {
                    Transform transform = poses[i].Transform;
                    if (excludedTransforms != null && excludedTransforms.Contains(transform))
                        continue;
                    if (poses[i].Restore())
                        restored++;
                }
                return restored;
            }
        }

        private readonly struct TransformPose
        {
            private readonly Transform transform;
            private readonly Vector3 localPosition;
            private readonly Quaternion localRotation;
            private readonly Vector3 localScale;

            internal Transform Transform => transform;

            internal TransformPose(Transform transform)
            {
                this.transform = transform;
                localPosition = transform.localPosition;
                localRotation = transform.localRotation;
                localScale = transform.localScale;
            }

            internal bool Restore()
            {
                if (transform == null)
                    return false;
                transform.localPosition = localPosition;
                transform.localRotation = localRotation;
                transform.localScale = localScale;
                return true;
            }
        }

        private static bool IsLocalPlayer(GameNetcodeStuff.PlayerControllerB player)
        {
            if (player == null)
                return false;

            try
            {
                GameNetcodeStuff.PlayerControllerB localPlayer =
                    GameNetworkManager.Instance != null
                        ? GameNetworkManager.Instance.localPlayerController
                        : null;
                if (localPlayer == null && StartOfRound.Instance != null)
                    localPlayer = StartOfRound.Instance.localPlayerController;
                return ReferenceEquals(player, localPlayer);
            }
            catch
            {
                return false;
            }
        }
    }

    // Runs after vanilla player/camera LateUpdate so an opt-in interaction controller cannot
    // visibly drag the viewpoint through an animated parent. Rotation is intentionally untouched:
    // the player retains normal mouse look while only the camera position is stabilized.
    [DefaultExecutionOrder(32000)]
    internal sealed class LocalCameraPositionStabilizer : MonoBehaviour
    {
        private Transform playerRoot;
        private Transform cameraTransform;
        private Vector3 playerLocalPosition;
        private int releaseLateUpdates = -1;

        internal void Initialize(
            Transform playerRoot,
            Transform cameraTransform,
            Vector3 playerLocalPosition)
        {
            this.playerRoot = playerRoot;
            this.cameraTransform = cameraTransform;
            this.playerLocalPosition = playerLocalPosition;
            ApplyNow();
        }

        internal void ReleaseAfterLateUpdates(int lateUpdates)
        {
            releaseLateUpdates = Mathf.Max(1, lateUpdates);
        }

        /// <summary>
        /// Re-reads the stored player-local target from the camera's CURRENT world position.
        /// The restore-scoped pin memorizes the stop-entry pose, which is contaminated by the
        /// authored clip's final frame; after the camera-chain snap-to-rest heals the chain,
        /// retargeting makes every later ApplyNow (including the deferred-release LateUpdates)
        /// hold the healed pose instead of re-applying the contaminated one.
        /// </summary>
        internal void RetargetToCurrentPosition()
        {
            if (playerRoot == null || cameraTransform == null)
                return;
            playerLocalPosition = playerRoot.InverseTransformPoint(cameraTransform.position);
        }

        internal void ApplyNow()
        {
            if (playerRoot == null || cameraTransform == null)
                return;
            cameraTransform.position = playerRoot.TransformPoint(playerLocalPosition);
        }

        private void LateUpdate()
        {
            ApplyNow();
            if (releaseLateUpdates < 0)
                return;

            releaseLateUpdates--;
            if (releaseLateUpdates <= 0)
            {
                enabled = false;
                UnityEngine.Object.Destroy(this);
            }
        }
    }

    // Consumer camera effects can run after PlayerControllerB has written the current pitch.
    // Anchor local Y/Z after ordinary LateUpdates and again before render, always from the
    // immutable session-entry values so no previous frame's output can become the next input.
    [DefaultExecutionOrder(32000)]
    internal sealed class LocalCameraRotationStabilizer : MonoBehaviour
    {
        private Transform cameraTransform;
        private float sessionEntryLocalYaw;
        private float sessionEntryLocalRoll;
        private bool beforeRenderSubscribed;

        internal void Initialize(
            Transform cameraTransform,
            float sessionEntryLocalYaw,
            float sessionEntryLocalRoll)
        {
            UnsubscribeBeforeRender();
            this.cameraTransform = cameraTransform;
            this.sessionEntryLocalYaw = sessionEntryLocalYaw;
            this.sessionEntryLocalRoll = sessionEntryLocalRoll;
            enabled = true;
            Application.onBeforeRender += ApplyNow;
            beforeRenderSubscribed = true;
            ApplyNow();
        }

        internal void ApplyNow()
        {
            if (cameraTransform == null)
                return;

            Vector3 currentLocalEuler = cameraTransform.localEulerAngles;
            cameraTransform.localRotation = Quaternion.Euler(
                currentLocalEuler.x,
                sessionEntryLocalYaw,
                sessionEntryLocalRoll);
        }

        private void LateUpdate()
        {
            ApplyNow();
        }

        private void OnDisable()
        {
            UnsubscribeBeforeRender();
        }

        private void OnDestroy()
        {
            UnsubscribeBeforeRender();
        }

        private void UnsubscribeBeforeRender()
        {
            if (!beforeRenderSubscribed)
                return;

            try { Application.onBeforeRender -= ApplyNow; } catch { }
            beforeRenderSubscribed = false;
        }
    }
}
