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
