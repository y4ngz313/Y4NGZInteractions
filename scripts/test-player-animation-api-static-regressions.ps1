$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "src/Y4NGZInteractions/Y4NGZInteractions.csproj"
$sourceRoot = Join-Path $repoRoot "src/Y4NGZInteractions"

[xml]$project = Get-Content -LiteralPath $projectPath -Raw
$packageIds = @($project.Project.ItemGroup.PackageReference.Include)
if ($packageIds -contains "UnityEngine.InputSystem") {
    throw "Production project must not reference the Input System package."
}
if ($null -ne $project.SelectSingleNode("//GameManagedDir")) {
    throw "Production project must not require a local game installation."
}
if (-not (Test-Path -LiteralPath (Join-Path $sourceRoot "PublicAPI.Shipped.txt"))) {
    throw "Shipped public API baseline is missing."
}

$source = (Get-ChildItem -LiteralPath $sourceRoot -Recurse -Filter "*.cs" |
    ForEach-Object { Get-Content -LiteralPath $_.FullName -Raw }) -join [Environment]::NewLine
$forbidden = @(
    "InteractionAnimationPresentationKind.Hybrid",
    "OwnerModId",
    "IInteractionAnimationBackend",
    "InteractionAnimationApiDebugProbe",
    "TryStopPlayerInteractions",
    "IsPlayerInteractionActive",
    "StanceViewpointMismatchTicksRequired"
)
foreach ($term in $forbidden) {
    if ($source.Contains($term)) {
        throw "Removed public/runtime term remains: $term"
    }
}

$required = @(
    "InteractionAnimationConflictPolicy",
    "RejectIfBusy",
    "InterruptExisting",
    "InteractionEnded",
    "TrySetInteractionFloat",
    "TryGetActiveInteraction",
    "InteractionAnimationValidationReport",
    "manifest_schema_1_migrated",
    "stopOnGameplayCameraDisplacement",
    "stabilizeLocalCameraPosition",
    "localCameraOwnedExternally",
    "live_body.playback_rate_sample",
    "StanceViewpointMismatchSecondsRequired",
    "TryReapplyLayerState"
)
foreach ($term in $required) {
    if (-not $source.Contains($term)) {
        throw "Required 1.0 contract term is missing: $term"
    }
}

$presenterPath = Join-Path $sourceRoot "InteractionAnimationApi/Presenters/LiveBodyAnimatorPresenter.cs"
$presenterSource = Get-Content -LiteralPath $presenterPath -Raw
$thirdPersonPoseRequired = @(
    "scopedThirdPersonPoseSnapshot",
    "CaptureScopedThirdPersonPose",
    "RestoreScopedThirdPersonPose",
    "CaptureDescendantsExceptBranch",
    "ResolveDirectChildBranch",
    "transform.IsChildOf(excludedBranch)",
    "live_body.scoped_tp_pose_captured",
    "live_body.scoped_tp_pose_restored"
)
foreach ($term in $thirdPersonPoseRequired) {
    if (-not $presenterSource.Contains($term)) {
        throw "Scoped third-person pose restore term is missing: $term"
    }
}

$captureIndex = $presenterSource.IndexOf("CaptureScopedThirdPersonPose();")
$controllerSwapIndex = $presenterSource.IndexOf("if (!TryApplyController(body, out reason))")
if ($captureIndex -lt 0 -or $controllerSwapIndex -lt 0 -or $captureIndex -ge $controllerSwapIndex) {
    throw "Third-person pose must be captured before the authored controller is applied."
}

$stopStart = $presenterSource.IndexOf("public void Stop(InteractionAnimationStopReason stopReason)")
$stopEnd = $presenterSource.IndexOf("/// <summary>", $stopStart + 1)
if ($stopStart -lt 0 -or $stopEnd -le $stopStart) {
    throw "Could not isolate LiveBodyAnimatorPresenter.Stop for restore-order validation."
}
$stopSource = $presenterSource.Substring($stopStart, $stopEnd - $stopStart)
$locomotionSyncIndex = $stopSource.IndexOf('SyncVanillaLocomotionParameters("stop");')
$thirdPersonRestoreIndex = $stopSource.IndexOf("RestoreScopedThirdPersonPose();")
$firstPersonRestoreIndex = $stopSource.IndexOf("RestoreScopedFirstPersonPose();")
$rigControlRestoreIndex = $stopSource.IndexOf("RestoreThirdPersonRigControlPose(animatorRestored);")
$preBuildProbeIndex = $stopSource.IndexOf(
    "InteractionAnimationApiRestoreDiagnostics.IkBakeProbePhasePreRestoreBuild")
if ($locomotionSyncIndex -lt 0 -or
    $thirdPersonRestoreIndex -le $locomotionSyncIndex -or
    $firstPersonRestoreIndex -le $thirdPersonRestoreIndex -or
    $rigControlRestoreIndex -le $firstPersonRestoreIndex -or
    $preBuildProbeIndex -le $rigControlRestoreIndex) {
    throw "Scoped third-person pose restore order is unsafe."
}

$guardedRestore = [regex]::Match(
    $stopSource,
    '(?s)if \(animatorRestored\)\s*\{.*?RestoreScopedThirdPersonPose\(\);.*?\}\s*RestoreThirdPersonRigControlPose')
if (-not $guardedRestore.Success) {
    throw "Third-person pose restore must remain guarded by successful Animator ownership restoration."
}

$snapshotClearCount = [regex]::Matches(
    $presenterSource,
    "scopedThirdPersonPoseSnapshot = null;").Count
if ($snapshotClearCount -lt 3) {
    throw "Third-person pose snapshot must be cleared on capture, normal stop, and failed start."
}

$syncGuardPath = Join-Path $sourceRoot "InteractionAnimationApi/PlayerAnimationSyncStateGuardPatch.cs"
if (-not (Test-Path -LiteralPath $syncGuardPath)) {
    throw "Multiplayer animation sync state guard is missing."
}
$syncGuardSource = Get-Content -LiteralPath $syncGuardPath -Raw
$syncGuardRequired = @(
    '[HarmonyPatch(typeof(PlayerControllerB), "UpdatePlayerAnimationsToOtherClients")]',
    'ref List<int> ___currentAnimationStateHash',
    'ref List<int> ___previousAnimationStateHash',
    '__instance?.playerBodyAnimator?.layerCount',
    'while (states.Count < requiredCount)'
)
foreach ($term in $syncGuardRequired) {
    if (-not $syncGuardSource.Contains($term)) {
        throw "Multiplayer animation sync guard term is missing: $term"
    }
}

# The library sends no network messages: every client animates and blends its own copy.
$networkTerms = @("CustomMessagingManager", "[ServerRpc", "[ClientRpc", "[Rpc(", "NetworkVariable", "NamedMessage")
foreach ($term in $networkTerms) {
    if ($source.Contains($term)) {
        throw "The library sends no network messages; every client animates and blends its own copy. Found: $term"
    }
}

# Exit pose blend capture points inside Stop: P0 is read before any restore writes a bone,
# and the blend arms after the last transform write.
$stopBeginIndex = $stopSource.IndexOf('LogMidSessionSample("stop_begin"')
$blendCaptureIndex = $stopSource.IndexOf("ExitPoseBlend.CaptureStartPose(")
$blendArmIndex = $stopSource.IndexOf("ExitPoseBlend.Arm(")
$seamGlueIndex = $stopSource.LastIndexOf("GlueSeamVisorToFinalCameraPose(")
if ($stopBeginIndex -lt 0 -or
    $blendCaptureIndex -le $stopBeginIndex -or
    $locomotionSyncIndex -le $blendCaptureIndex) {
    throw "Stop must capture the exit pose blend start pose after LogMidSessionSample(stop_begin) and before SyncVanillaLocomotionParameters(stop)."
}
if ($seamGlueIndex -lt 0 -or $blendArmIndex -le $seamGlueIndex) {
    throw "Stop must arm the exit pose blend after GlueSeamVisorToFinalCameraPose."
}

# Runner execution orders bracket the animator update: blend writes first, callbacks last.
$presentersRoot = Join-Path $sourceRoot "InteractionAnimationApi/Presenters"
$blendRunnerSource = Get-Content -LiteralPath (Join-Path $presentersRoot "ExitPoseBlendRunner.cs") -Raw
if ($blendRunnerSource -notmatch '\[DefaultExecutionOrder\(-31000\)\]\s*internal sealed class ExitPoseBlendRunner\b') {
    throw "ExitPoseBlendRunner must carry [DefaultExecutionOrder(-31000)]."
}
$callbackRunnerSource = Get-Content -LiteralPath (Join-Path $presentersRoot "PostAnimationCallbackRunner.cs") -Raw
if ($callbackRunnerSource -notmatch '\[DefaultExecutionOrder\(31000\)\]\s*internal sealed class PostAnimationCallbackRunner\b') {
    throw "PostAnimationCallbackRunner must carry [DefaultExecutionOrder(31000)]."
}

# The exit pose blend never writes scale.
foreach ($blendFile in Get-ChildItem -LiteralPath $presentersRoot -Filter "ExitPoseBlend*.cs") {
    if ((Get-Content -LiteralPath $blendFile.FullName -Raw).Contains("localScale")) {
        throw "The exit pose blend must not write scale: $($blendFile.Name) contains localScale."
    }
}

# Runtime host LateUpdate order: coordinator tick, arms write-back, metarig root write-back, probe.
$hostSource = Get-Content -LiteralPath (Join-Path $sourceRoot "InteractionRuntimeHost.cs") -Raw
$hostLateUpdate = [regex]::Match($hostSource, '(?s)private void LateUpdate\(\)\s*\{.*?\r?\n        \}')
if (-not $hostLateUpdate.Success) {
    throw "Could not isolate InteractionRuntimeHost.LateUpdate for order validation."
}
$hostOrder = @(
    "InteractionAnimationApiPlugin.Tick(",
    "ArmsMetarigWriteBackOwner.LateUpdateTick(",
    'MetarigRootWriteBackOwner.LateUpdateTick("late")',
    "MovementStartProbe.LateUpdateTick("
) | ForEach-Object { $hostLateUpdate.Value.IndexOf($_) }
if ($hostOrder -contains -1 -or
    $hostOrder[1] -le $hostOrder[0] -or
    $hostOrder[2] -le $hostOrder[1] -or
    $hostOrder[3] -le $hostOrder[2]) {
    throw "InteractionRuntimeHost.LateUpdate must run the coordinator tick, ArmsMetarigWriteBackOwner, MetarigRootWriteBackOwner(late), then MovementStartProbe, in that order."
}

Write-Output "Static API/package guard passed."
