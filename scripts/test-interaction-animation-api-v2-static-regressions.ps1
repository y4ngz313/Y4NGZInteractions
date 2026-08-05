Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$apiRoot = Join-Path $repoRoot "src/Y4NGZInteractions/InteractionAnimationApi"
$pluginFile = Join-Path $repoRoot "src/Y4NGZInteractions/Plugin.cs"
$apiPluginFile = Join-Path $repoRoot "src/Y4NGZInteractions/InteractionAnimationApiPlugin.cs"
$liveBodyPresenterFile = Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs"
$restoreDiagnosticsFile = Join-Path $repoRoot "src/Y4NGZInteractions/RestoreDiagnostics.cs"

function Assert-File {
    param([string] $Path)
    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Expected file missing: $Path"
    }
}

function Assert-Content {
    param(
        [string] $Path,
        [string] $Pattern
    )
    $content = Get-Content -Raw -LiteralPath $Path
    if (-not $content.Contains($Pattern)) {
        throw "Expected pattern '$Pattern' missing from $Path"
    }
}

function Assert-NotContent {
    param(
        [string] $Path,
        [string] $Pattern
    )
    $content = Get-Content -Raw -LiteralPath $Path
    if ($content.Contains($Pattern)) {
        throw "Unexpected pattern '$Pattern' found in $Path"
    }
}

function Assert-ContentOrder {
    param(
        [string] $Path,
        [string[]] $Patterns
    )
    $content = Get-Content -Raw -LiteralPath $Path
    $cursor = -1
    foreach ($pattern in $Patterns) {
        $index = $content.IndexOf($pattern, $cursor + 1, [StringComparison]::Ordinal)
        if ($index -lt 0) {
            throw "Expected ordered pattern '$pattern' missing after offset $cursor in $Path"
        }
        $cursor = $index
    }
}

Assert-File $apiPluginFile
Assert-File (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs")
Assert-File (Join-Path $apiRoot "LCInteractionAnimationAPI.cs")
Assert-File (Join-Path $apiRoot "InteractionAnimationCoordinator.cs")
Assert-File (Join-Path $apiRoot "InteractionAnimationTypes.cs")
Assert-File (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs")
Assert-File (Join-Path $apiRoot "Backends/IInteractionAnimationBackend.cs")
Assert-File (Join-Path $apiRoot "Presenters/IInteractionPresenter.cs")
Assert-File (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs")

Assert-Content $pluginFile "InteractionAnimationApiPlugin.Initialize"
Assert-Content $pluginFile "InteractionAnimationApiPlugin.Initialize(Config, Logger)"
Assert-Content $pluginFile "InteractionAnimationApiPlugin.Tick"
Assert-Content $pluginFile "InteractionAnimationApiPlugin.Shutdown"
Assert-Content $pluginFile "InteractionAnimationApiDebugProbe.Initialize(Config, Logger)"
Assert-Content $pluginFile "InteractionAnimationApiDebugProbe.Tick()"
Assert-Content $pluginFile "InteractionAnimationApiDebugProbe.Shutdown()"
Assert-Content $pluginFile "harmony.PatchAll()"

Assert-Content (Join-Path $apiRoot "LCInteractionAnimationAPI.cs") "public static class LCInteractionAnimationAPI"
Assert-Content (Join-Path $apiRoot "LCInteractionAnimationAPI.cs") "TryRegisterInteractionPack"
Assert-Content (Join-Path $apiRoot "LCInteractionAnimationAPI.cs") "TryStartInteraction"
Assert-Content (Join-Path $apiRoot "LCInteractionAnimationAPI.cs") "TryStopInteraction"
Assert-Content (Join-Path $apiRoot "LCInteractionAnimationAPI.cs") "IsPlayerInteractionActive"
Assert-Content (Join-Path $apiRoot "LCInteractionAnimationAPI.cs") "TryStopPlayerInteractions"

Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "internal sealed class InteractionAnimationCoordinator"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "private readonly Dictionary<string, InteractionAnimationPackDefinition> packs"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "private readonly Dictionary<InteractionAnimationHandle, InteractionAnimationSession> activeSessions"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "private DateTime startedUtc;"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "startedUtc = DateTime.UtcNow;"
Assert-NotContent (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "private readonly DateTime startedUtc;"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "TryAcquireBodyWorldAnimator"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "player_animator_owned_externally"
Assert-Content (Join-Path $apiRoot "InteractionAnimationCoordinator.cs") "InteractionAnimationStopReason.Interrupted"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "live_body.ownership_lost"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "live_body.camera_displacement_guard"
Assert-Content $apiPluginFile '"Camera Displacement Guard Exempt Interactions"'
Assert-Content $apiPluginFile '"Special Animation Auto-Stop Exempt Interactions"'
# Consumer-agnostic API: no interaction id ships as a built-in exemption. Interactions
# declare their own exemptions in the manifest; the config lists stay as operator overrides.
Assert-Content $apiPluginFile 'internal const string DefaultCameraDisplacementGuardExemptInteractions = "";'
Assert-Content $apiPluginFile 'internal const string DefaultSpecialAnimationAutoStopExemptInteractions = "";'
Assert-Content $apiPluginFile "Authoring.InteractionAnimationManifest manifest)"
Assert-Content $apiPluginFile "manifest.exemptFromCameraDisplacementGuard ||"
Assert-Content $apiPluginFile "manifest.exemptFromSpecialAnimationAutoStop ||"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public bool exemptFromCameraDisplacementGuard;"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public bool exemptFromSpecialAnimationAutoStop;"
Assert-ContentOrder $apiPluginFile @(
    "internal static bool IsCameraDisplacementGuardExempt(",
    "Authoring.InteractionAnimationManifest manifest)",
    "manifest.exemptFromCameraDisplacementGuard ||",
    "IsCameraDisplacementGuardExempt(manifest.interactionId);"
)
Assert-ContentOrder $apiPluginFile @(
    "internal static bool IsSpecialAnimationAutoStopExempt(",
    "Authoring.InteractionAnimationManifest manifest)",
    "manifest.exemptFromSpecialAnimationAutoStop ||",
    "IsSpecialAnimationAutoStopExempt(manifest.interactionId);"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "cameraGuardExempt = InteractionAnimationApiPlugin.IsCameraDisplacementGuardExempt(",
    "context.Manifest);"
)
Assert-Content $liveBodyPresenterFile "IsSpecialAnimationAutoStopExempt(context?.Manifest)"
Assert-Content $apiPluginFile "IsCameraDisplacementGuardExempt"
Assert-Content $apiPluginFile "StringSplitOptions.RemoveEmptyEntries"
Assert-Content $apiPluginFile "StringComparison.OrdinalIgnoreCase"
Assert-Content $liveBodyPresenterFile "CameraDisplacementGuardThreshold = 1.25f"
Assert-Content $liveBodyPresenterFile "VanillaCameraPlayerLocalRestExpectation"
Assert-Content $liveBodyPresenterFile '"try_start_pre_controller"'
Assert-Content $liveBodyPresenterFile "live_body.camera_displacement_guard.baseline_captured"
Assert-Content $liveBodyPresenterFile "live_body.camera_displacement_guard.guard_exempt"
Assert-Content $liveBodyPresenterFile "live_body.camera_displacement_guard.pre_existing_displacement"
Assert-Content $liveBodyPresenterFile "live_body.camera_displacement_guard.stop"
Assert-Content $liveBodyPresenterFile "baseline_contaminated="
Assert-Content $liveBodyPresenterFile "pre_existing_rotation_residue="
Assert-Content $liveBodyPresenterFile "camera_container_deviation_from_rest="
Assert-Content $liveBodyPresenterFile "CameraRotationResidueThresholdDegrees = 0.02f"
Assert-Content $liveBodyPresenterFile "VanillaCameraContainerLocalRestEuler"
Assert-Content $liveBodyPresenterFile "cameraGuardPreExistingDisplacementDetected"
Assert-Content $liveBodyPresenterFile "currentDisplacementFromVanillaRest <= cameraBaselineDisplacementFromVanillaRest"
Assert-Content $liveBodyPresenterFile "action='continue_exempt'"
Assert-Content $liveBodyPresenterFile "action='continue_pre_existing_not_worsened'"
Assert-Content $liveBodyPresenterFile "action='stop_genuinely_new_displacement'"
Assert-NotContent $liveBodyPresenterFile "The authored controller moved the local camera/body hierarchy"
Assert-ContentOrder $liveBodyPresenterFile @(
    "bodyAnimator = context.Request.Player.playerBodyAnimator;",
    "CaptureLocalCameraBaseline();",
    "StartLocalCameraRotationStabilizer();",
    "TryApplyController(body, out reason)"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "if (cameraGuardExempt)",
    "live_body.camera_displacement_guard.guard_exempt",
    "if (cameraGuardPreExistingDisplacementDetected &&",
    "currentDisplacementFromVanillaRest <= cameraBaselineDisplacementFromVanillaRest",
    "shouldAutoStop = true;",
    "live_body.camera_displacement_guard.stop"
)
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "live_body.scoped_fp_pose_captured"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "live_body.scoped_fp_pose_restored"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "scopedFirstPersonTransformRestore"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "live_body.local_camera_stabilizer_started"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "LocalCameraPositionStabilizer"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "ReleaseAfterLateUpdates(2)"
Assert-Content $liveBodyPresenterFile "LocalCameraRotationStabilizer"
Assert-Content $liveBodyPresenterFile "live_body.camera_rotation_stabilizer_started"
Assert-Content $liveBodyPresenterFile "Application.onBeforeRender += ApplyNow;"
Assert-Content $liveBodyPresenterFile "Application.onBeforeRender -= ApplyNow;"
Assert-Content $liveBodyPresenterFile "currentLocalEuler.x,"
Assert-Content $liveBodyPresenterFile "sessionEntryLocalYaw,"
Assert-Content $liveBodyPresenterFile "sessionEntryLocalRoll);"
Assert-NotContent $liveBodyPresenterFile "currentLocalEuler.y +"
Assert-NotContent $liveBodyPresenterFile "currentLocalEuler.z +"
Assert-Content $liveBodyPresenterFile "Application.onBeforeRender += LogFinalFrameTransformChainDiagnostic;"
Assert-Content $liveBodyPresenterFile "Application.onBeforeRender -= LogFinalFrameTransformChainDiagnostic;"
Assert-Content $liveBodyPresenterFile "live_body.transform_chain:"
Assert-Content $liveBodyPresenterFile "phase='before_render'"
Assert-Content $liveBodyPresenterFile "missNormalizedHeight="
Assert-Content $restoreDiagnosticsFile '"Restore Pristine Rig Control Pose"'
Assert-Content $restoreDiagnosticsFile '"Restore Third-Person Rig Control Pose"'
Assert-Content $restoreDiagnosticsFile '"Restore Pristine Third-Person Rig Control Pose"'
Assert-Content $restoreDiagnosticsFile '"Enable Remote Rig Diff Probe"'
Assert-Content $restoreDiagnosticsFile '"Restore Vanilla Arms Glue"'
Assert-Content $restoreDiagnosticsFile '"Restore Camera Rotation"'
Assert-Content $restoreDiagnosticsFile '"Stabilize Camera Rotation During Session"'
Assert-Content $restoreDiagnosticsFile '"Restore Camera Rotation Snap To Rest"'
Assert-Content $restoreDiagnosticsFile '"Restore Visor Pose"'
Assert-Content $restoreDiagnosticsFile '"Enable External Camera Presentation Logger"'
Assert-Content $restoreDiagnosticsFile "ExternalCameraPresentationLoggerEnabled"
Assert-Content $liveBodyPresenterFile "StartExternalCameraPresentationDiagnostics();"
Assert-Content $liveBodyPresenterFile "StopExternalCameraPresentationDiagnostics();"
Assert-Content $liveBodyPresenterFile "Application.onBeforeRender += LogExternalCameraPresentationState;"
Assert-Content $liveBodyPresenterFile "Application.onBeforeRender -= LogExternalCameraPresentationState;"
Assert-Content $liveBodyPresenterFile "live_body.external_camera_presentation_state"
Assert-Content $liveBodyPresenterFile "live_body.external_camera_presentation_invariant_failed"
Assert-Content $liveBodyPresenterFile '"first_person_arms"'
Assert-Content $liveBodyPresenterFile '"local_visor"'
Assert-Content $liveBodyPresenterFile '"world_body"'
Assert-Content $liveBodyPresenterFile "renderer.forceRenderingOff"
Assert-Content $liveBodyPresenterFile "renderer.shadowCastingMode"
Assert-Content $liveBodyPresenterFile "renderer.isVisible"
Assert-ContentOrder $liveBodyPresenterFile @(
    "private void StartLocalVisorHardGlue()",
    "if (LocalCameraOwnedExternally)",
    "live_body.visor_glue_skipped",
    "if (!InteractionAnimationApiRestoreDiagnostics.HardVisorGlueDuringSessionEnabled)",
    "Application.onBeforeRender += ApplyLocalVisorHardGlue;"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "private VisorPoseSnapshot CaptureSeamVisorPose(string phase)",
    "if (LocalCameraOwnedExternally)",
    "reason='local_camera_owned_externally' action='leave_visor_to_external_owner'"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "private void ReapplySeamVisorPose(",
    "if (LocalCameraOwnedExternally)",
    "reason='local_camera_owned_externally' action='leave_visor_to_external_owner'"
)
Assert-Content $restoreDiagnosticsFile "TryRestorePristineRigControlPose"
Assert-Content $restoreDiagnosticsFile "TryRestorePristineThirdPersonRigControlPose"
Assert-Content $restoreDiagnosticsFile "TryCapturePristineThirdPersonRigPose"
Assert-Content $restoreDiagnosticsFile "ThirdPersonRigRestPositionThreshold = 0.05f"
Assert-Content $restoreDiagnosticsFile "ThirdPersonRigRestRotationThresholdDegrees = 8f"
Assert-Content $restoreDiagnosticsFile '"spine.003/LeftArm_target"'
Assert-Content $restoreDiagnosticsFile '"spine.003/RightArm_target"'
Assert-Content $restoreDiagnosticsFile '"Rig 1/LeftLeg/LeftLeg_target"'
Assert-Content $restoreDiagnosticsFile '"Rig 1/RightLeg/RightLeg_target"'
Assert-Content $restoreDiagnosticsFile '"spine/thigh.L/shin.L"'
Assert-Content $restoreDiagnosticsFile '"spine/thigh.R/shin.R"'
Assert-Content $restoreDiagnosticsFile '"[RestoreSeam.tprig] pristine_capture_sanity: "'
Assert-ContentOrder $restoreDiagnosticsFile @(
    "string sanityAction = plausibility.Plausible",
    '? "accept_authored_default"',
    ': "accept_authored_default_pending_runtime_recapture"',
    "action='{sanityAction}'",
    "candidate.SetCaptureMetadata(plausibility.Plausible, source);",
    "PristineThirdPersonRigPoses[player] = candidate;"
)
Assert-Content $restoreDiagnosticsFile "source = ThirdPersonRigAuthoredDefaultSource"
Assert-Content $restoreDiagnosticsFile '[HarmonyPatch(typeof(PlayerControllerB), "Awake")]'
Assert-Content $restoreDiagnosticsFile "[HarmonyPriority(Priority.First)]"
Assert-NotContent $restoreDiagnosticsFile '"[RestoreSeam.tprig] pristine_capture_rejected: "'
Assert-NotContent $restoreDiagnosticsFile "action='retry_first_clean_sight'"
Assert-Content $restoreDiagnosticsFile "baseline_contaminated=True"
Assert-Content $restoreDiagnosticsFile '"[RemoteRigDiff] sample_begin: "'
Assert-Content $restoreDiagnosticsFile "NotifyRemoteRigProbeSessionBegin"
Assert-Content $restoreDiagnosticsFile "remoteRigProbeSessionBeginUninitializedLogged"
Assert-Content $restoreDiagnosticsFile "prepareForLiveBodyStartUninitializedLogged"
Assert-Content $restoreDiagnosticsFile "initialized=False reason='restore_diagnostics_not_initialized'"
Assert-Content $restoreDiagnosticsFile "action='skip_restore_diagnostics_not_initialized'"
Assert-Content $restoreDiagnosticsFile '"restore_plus_2_lateupdates"'
Assert-Content $restoreDiagnosticsFile '"restore_plus_5_seconds"'
Assert-Content $restoreDiagnosticsFile "localPosition="
Assert-Content $restoreDiagnosticsFile "localRotation="
Assert-Content $restoreDiagnosticsFile "localScale="
Assert-Content $liveBodyPresenterFile "CaptureThirdPersonRigControlPose();"
Assert-Content $liveBodyPresenterFile "RestoreThirdPersonRigControlPose(animatorRestored);"
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.tprig] capture_skipped: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.tprig] pristine_restore_gate: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.tprig] restored: "'
Assert-Content $liveBodyPresenterFile "context?.Logger ?? InteractionAnimationApiRestoreDiagnostics.StaticLogger"
Assert-Content $restoreDiagnosticsFile "private const int StartRenderFutureFrames = 3;"
Assert-Content $restoreDiagnosticsFile "private const int StopRenderFutureFrames = 5;"
Assert-Content $restoreDiagnosticsFile 'new RenderSeamWindow("start")'
Assert-Content $restoreDiagnosticsFile 'new RenderSeamWindow("stop")'
Assert-Content $restoreDiagnosticsFile "cameraWorldEuler="
Assert-Content $restoreDiagnosticsFile "hand.RWorldEuler="
Assert-Content $restoreDiagnosticsFile "hand.LWorldEuler="
Assert-Content $restoreDiagnosticsFile "playerModelArmsMetarigWorldEuler="
Assert-Content $restoreDiagnosticsFile "localArmsTransformWorldPos="
Assert-Content $restoreDiagnosticsFile "localArmsTransformWorldEuler="
Assert-Content $restoreDiagnosticsFile "animatedProp="
Assert-Content $restoreDiagnosticsFile "heldItem="
Assert-Content $restoreDiagnosticsFile "localVisorWorldPos="
Assert-Content $restoreDiagnosticsFile "localVisorWorldEuler="
Assert-Content $restoreDiagnosticsFile "localVisorTargetPointWorldPos="
Assert-Content $restoreDiagnosticsFile "localVisorTargetPointWorldEuler="
Assert-Content $restoreDiagnosticsFile "localVisorRenderers="
Assert-Content $restoreDiagnosticsFile "visorCameraEnabled="
Assert-Content $restoreDiagnosticsFile "deltaTime="
Assert-Content $restoreDiagnosticsFile "unscaledDeltaTime="
Assert-Content $restoreDiagnosticsFile "cameraFov="
Assert-Content $restoreDiagnosticsFile "cameraNearClip="
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.armsglue] gates: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.camerarotation] captured: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.camerarotation] reapplied: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.camerarotation] stop_restore_gate: "'
Assert-Content $liveBodyPresenterFile "discardedGameplayCameraLocalYaw="
Assert-Content $liveBodyPresenterFile "discardedGameplayCameraLocalRoll="
Assert-Content $liveBodyPresenterFile "discardedCameraContainerDeviationFromRest="
Assert-Content $liveBodyPresenterFile "Quaternion.Euler(capturedGameplayCameraEuler.x, 0f, 0f)"
Assert-Content $liveBodyPresenterFile "captured.CameraContainerTransform.localEulerAngles ="
Assert-Content $liveBodyPresenterFile "VanillaCameraContainerLocalRestEuler;"
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.visor] hierarchy: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.visor] captured: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.visor] reapplied: "'
Assert-Content $liveBodyPresenterFile '"[RestoreSeam.timing] "'
Assert-Content $liveBodyPresenterFile 'armsMetarig.localEulerAngles = new Vector3(-90f, 0f, 0f);'
Assert-Content $liveBodyPresenterFile "cameraContainer.position + gameplayCamera.transform.up * -0.5f"
Assert-Content $liveBodyPresenterFile "armsMetarig.position + armsMetarig.forward * -0.445f"
Assert-Content $liveBodyPresenterFile "armsMetarig.rotation = armsRotationTarget.rotation;"
Assert-NotContent $liveBodyPresenterFile "if (player.inSpecialInteractAnimation)"
Assert-NotContent $liveBodyPresenterFile "if (!player.localArmsMatchCamera)"
Assert-ContentOrder $liveBodyPresenterFile @(
    "bool inSpecialInteractAnimation = player.inSpecialInteractAnimation;",
    "bool localArmsMatchCamera = player.localArmsMatchCamera;",
    '"[RestoreSeam.armsglue] gates: "',
    "if (inSpecialInteractAnimation)",
    "armsMetarig.localEulerAngles = new Vector3(-90f, 0f, 0f);",
    "if (!headBobbingEnabled && cameraContainer != null && armsMetarig != null)",
    "if (localArmsMatchCamera)",
    "cameraContainer.position + gameplayCamera.transform.up * -0.5f",
    "armsMetarig.position + armsMetarig.forward * -0.445f",
    "armsMetarig.rotation = armsRotationTarget.rotation;"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "AttachPropIfConfigured();",
    "InteractionAnimationApiRestoreDiagnostics.NotifyLiveBodyStarted(",
    "active = true;"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "active = true;",
    "StartTransformChainDiagnostics();",
    "StartExternalCameraPresentationDiagnostics();",
    "StartLocalVisorHardGlue();"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    'CameraRotationSnapshot startCameraRotation = CaptureSeamCameraRotation("start");',
    'VisorPoseSnapshot startVisorPose = CaptureSeamVisorPose("start");',
    "TryApplyController(body, out reason)",
    'RebuildRigBuilders("start");',
    'ReapplySeamCameraRotation(startCameraRotation, "start", animatorRestored: true);',
    'ReapplySeamVisorPose(startVisorPose, "start", animatorRestored: true);',
    "try { bodyAnimator.Update(0f); } catch { }",
    'EvaluateRigBuilders("start");'
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "InteractionAnimationApiRestoreDiagnostics.NotifyStop(player, propInstance);",
    "StartRestoreScopedCameraPositionStabilizer();",
    'CameraRotationSnapshot stopCameraRotation = CaptureSeamCameraRotation("stop");',
    "StopLocalCameraRotationStabilizer(restoreSessionEntryRotation: true);",
    'VisorPoseSnapshot stopVisorPose = CaptureSeamVisorPose("stop");',
    "DestroyProp();",
    "string restoreResult = RestoreAnimator"
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "RestoreScopedFirstPersonPose();",
    "RestoreRigControlPose();",
    "RestoreThirdPersonRigControlPose(animatorRestored);",
    'RebuildRigBuilders("restore");',
    'ReapplySeamCameraRotation(stopCameraRotation, "stop", animatorRestored);',
    'ReapplySeamVisorPose(stopVisorPose, "stop", animatorRestored);',
    "ApplyVanillaLocalArmsGlueBeforeRigEvaluation();",
    "try { bodyAnimator?.Update(0f); } catch { }",
    'EvaluateRigBuilders("restore");'
)
Assert-ContentOrder $liveBodyPresenterFile @(
    "InteractionAnimationApiRestoreDiagnostics.PrepareForLiveBodyStart(",
    "CaptureRigControlPose();",
    "CaptureThirdPersonRigControlPose();",
    "InteractionAnimationApiRestoreDiagnostics.NotifyRemoteRigProbeSessionBegin(",
    "TryApplyController(body, out reason)"
)
# Fix A: implausible Awake-time third-person rig baseline is recaptured at the first
# verified-clean idle session entry, gated by a new default-on kill-switch.
Assert-Content $restoreDiagnosticsFile '"Recapture Implausible Pristine Rig Baseline"'
Assert-Content $restoreDiagnosticsFile "RecaptureImplausiblePristineRigBaselineEnabled"
Assert-Content $restoreDiagnosticsFile "internal static bool TryRecapturePristineThirdPersonRigPoseIfImplausible("
Assert-Content $restoreDiagnosticsFile "ThirdPersonRigRuntimeSettledSource"
Assert-Content $restoreDiagnosticsFile "internal bool PlausibleAtCapture { get; private set; }"
Assert-Content $restoreDiagnosticsFile '"[RestoreSeam.tprig] rest_baseline_recaptured: "'
Assert-Content $restoreDiagnosticsFile "action='replace_authored_default_with_runtime_settled'"
Assert-Content $restoreDiagnosticsFile "reason='candidate_still_implausible' action='retry_next_clean_entry'"
Assert-ContentOrder $liveBodyPresenterFile @(
    "TryRefineCameraChainRestBaselineAtCleanEntry(player, displacement);",
    "TryGetVerifiedCleanIdleState(",
    ".TryRecapturePristineThirdPersonRigPoseIfImplausible("
)
Assert-Content $liveBodyPresenterFile "private static bool TryGetVerifiedCleanIdleState("

# Fix B: the restore-scoped camera position stabilizer is retargeted to the healed pose
# inside the camera-chain snap-to-rest, so its final restore and deferred LateUpdates hold
# the snapped position instead of re-applying the stop-entry (contaminated) one. The snap
# call itself stays before the arms glue in Stop ordering.
Assert-Content $liveBodyPresenterFile "internal void RetargetToCurrentPosition()"
Assert-ContentOrder $liveBodyPresenterFile @(
    "private void ApplyCameraChainPositionSnapToRest()",
    "TryRestorePristineCameraChainPositions",
    "cameraPositionStabilizer.RetargetToCurrentPosition();",
    "snap_applied",
    "stabilizerRetargeted={stabilizerRetargeted}",
    "ApplyCameraChainPositionSnapToRest();",
    "ApplyVanillaLocalArmsGlueBeforeRigEvaluation();"
)

# Fix C: the displacement guard suppresses auto-stop while the camera stays within the
# vanilla-rest envelope (crouch<->stand transitions), as the last suppression before the
# genuine-displacement stop, and the baseline log carries crouch telemetry.
Assert-Content $liveBodyPresenterFile "baseline_crouching="
Assert-ContentOrder $liveBodyPresenterFile @(
    "private void DetectUnsafeCameraDisplacement()",
    "action='continue_exempt'",
    "action='continue_pre_existing_not_worsened'",
    "currentDisplacementFromVanillaRest <= CameraDisplacementGuardThreshold",
    "action='continue_within_vanilla_rest_envelope'",
    "shouldAutoStop = true;",
    "action='stop_genuinely_new_displacement'"
)

Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "stabilizeLocalCameraPosition"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "naturalEndLayerFadeSeconds"
Assert-Content (Join-Path $apiRoot "AnimatorStateSnapshot.cs") "animator.Rebind();"

Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "internal static class InteractionAnimationApiDebugProbe"
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "Enable Page Down Viewmodel Probe"
# The probe carries no consumer payload identity: every id, manifest file name and animator
# parameter is a config entry that defaults to empty, and the probe self-disables unconfigured.
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Pack Id"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Viewmodel Interaction Id"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Live Body Interaction Id"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Viewmodel Manifest File Name"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Body Manifest File Name"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Action Ready Bool Parameter"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Action Trigger Parameter"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") '"Probe Action Index Parameter"'
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "probe.disabled: pack_id_not_configured"
Assert-ContentOrder (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") @(
    "string packId = PackId;",
    "if (string.IsNullOrWhiteSpace(packId))",
    "probeDisabled = true;"
)
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "LCInteractionAnimationAPI.TryRegisterInteractionPack"
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "LCInteractionAnimationAPI.TryStartInteraction"
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "LocalViewmodelPresenter.BeginPreload(viewmodelManifest, logger)"
Assert-Content (Join-Path $apiRoot "InteractionAnimationApiDebugProbe.cs") "PageDownConsumedThisFrame"

Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "public sealed class InteractionAnimationPackDefinition"
Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "public string AssetRootPath { get; set; }"
Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "public sealed class InteractionAnimationDefinition"
Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "public sealed class InteractionAnimationRequest"
Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "public readonly struct InteractionAnimationHandle"
Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "DedicatedLocalViewmodel"
Assert-Content (Join-Path $apiRoot "InteractionAnimationTypes.cs") "BodyWorld"

Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public sealed class InteractionAnimationManifest"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "TryParse"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "Validate"
# The schema ships no consumer prefab/bundle defaults; a viewmodel manifest must author both.
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public string bundleFileName = string.Empty;"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public string prefab = string.Empty;"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "manifest_viewmodel_prefab_empty"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "manifest_viewmodel_bundle_file_empty"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "manifest_socket_prop_empty"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public string ResolvedProp =>"

Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "internal sealed class LocalViewmodelPresenter"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "TryStart"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "BeginPreload"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "context.AssetRootPath"
Assert-Content (Join-Path $apiRoot "Presenters/LiveBodyAnimatorPresenter.cs") "context.AssetRootPath"
Assert-Content (Join-Path $apiRoot "InteractionAnimationAssetPathResolver.cs") "asset_bundle_path_escapes_root"
Assert-Content (Join-Path $apiRoot "InteractionAnimationAssetPathResolver.cs") "pack_asset_root_missing"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "AssetBundle.LoadFromFileAsync"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "viewmodel.bundle_preload_started"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "viewmodel.bundle_preload_completed"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "viewmodel.bundle_preload_in_progress"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "HideLiveFirstPersonRenderers"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "RestoreLiveFirstPersonRenderers"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public string activeBool"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public string enterTrigger"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public string exitTrigger"
Assert-Content (Join-Path $apiRoot "Authoring/InteractionAnimationManifest.cs") "public float exitSeconds"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "SetBoolIfExists(manifest.localViewmodel.activeBool, true)"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "FireTriggerIfExists(manifest.localViewmodel.enterTrigger)"
Assert-Content (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "FireTriggerIfExists(viewmodel.exitTrigger)"
Assert-NotContent (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "local_rotation"
Assert-NotContent (Join-Path $apiRoot "Presenters/LocalViewmodelPresenter.cs") "rendererBonesRemapped"

Assert-Content (Join-Path $repoRoot "docs/ANIMATION_AUTHORING_PIPELINE.md") "sole generic source of truth"
Assert-Content (Join-Path $repoRoot "docs/ANIMATION_AUTHORING_PIPELINE.md") "AssetRootPath"
Assert-Content (Join-Path $repoRoot "docs/ANIMATION_AUTHORING_PIPELINE.md") "is an exclusive resource"

# Consumer-coupling guard: this repo is a consumer-agnostic animation API, so no consumer
# item, weapon or feature name may appear anywhere in its source. New animation content
# belongs in the consuming mod, registered through the API.
$consumerCouplingTerms = @(
    "dronetablet",
    "drone_tablet",
    "drone tablet",
    "heavyshotgun",
    "heavy_shotgun",
    "revolver",
    "heavypistol",
    "heavy_pistol",
    "cctv",
    "kar98",
    "worklight",
    "flamethrower",
    "C:\Users\"
)

$sourceRoot = Join-Path $repoRoot "src/Y4NGZInteractions"
$couplingViolations = New-Object System.Collections.Generic.List[string]
foreach ($sourceFile in Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter *.cs -File) {
    if ($sourceFile.FullName -match '\\(bin|obj)\\') {
        continue
    }

    $lineNumber = 0
    foreach ($line in Get-Content -LiteralPath $sourceFile.FullName) {
        $lineNumber++
        foreach ($term in $consumerCouplingTerms) {
            if ($line.IndexOf($term, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
                $relative = $sourceFile.FullName.Substring($repoRoot.Length + 1)
                $couplingViolations.Add("$relative`:$lineNumber term='$term' -> $($line.Trim())")
            }
        }
    }
}

if ($couplingViolations.Count -gt 0) {
    throw ("Consumer-specific names found in the API source (move them to the consuming mod):" +
        [Environment]::NewLine + ($couplingViolations -join [Environment]::NewLine))
}

Write-Host "Interaction Animation API V2 static regression checks passed."
