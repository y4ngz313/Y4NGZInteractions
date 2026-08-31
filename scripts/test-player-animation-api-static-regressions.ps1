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

Write-Output "Static API/package guard passed."
