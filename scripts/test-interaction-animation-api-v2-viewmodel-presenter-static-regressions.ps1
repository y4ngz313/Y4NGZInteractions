Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$presenterPath = Join-Path $repoRoot "src/Y4NGZInteractions/InteractionAnimationApi/Presenters/LocalViewmodelPresenter.cs"
$manifestPath = Join-Path $repoRoot "src/Y4NGZInteractions/InteractionAnimationApi/Authoring/InteractionAnimationManifest.cs"
$projectPath = Join-Path $repoRoot "src/Y4NGZInteractions/Y4NGZInteractions.csproj"

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

Assert-File $presenterPath
Assert-File $manifestPath
Assert-File $projectPath

# Consumer-agnostic schema: the viewmodel bundle and prefab are authored per interaction,
# never defaulted to one consumer's asset, and validation rejects a manifest missing either.
Assert-Content $manifestPath "public string bundleFileName = string.Empty;"
Assert-Content $manifestPath "public string prefab = string.Empty;"
Assert-Content $manifestPath "manifest_viewmodel_bundle_file_empty"
Assert-Content $manifestPath "manifest_viewmodel_prefab_empty"
Assert-Content $manifestPath "public Vector3 cameraLocalPosition = new Vector3(0f, -0.42f, 0.95f);"
Assert-Content $manifestPath "public Vector3 cameraLocalEuler = Vector3.zero;"
Assert-Content $manifestPath "public Vector3 localScale = new Vector3(0.55f, 0.55f, 0.55f);"
Assert-Content $manifestPath "public string runtimeMaterialMode = string.Empty;"
Assert-Content $manifestPath 'public string cameraAnchor = "Y4NGZ_ViewmodelCameraAnchor";'
Assert-Content $manifestPath "manifest_viewmodel_camera_anchor_empty"

# Socket schema: 'prop' is the generic field; 'tablet' survives only as a legacy alias that
# 'prop' wins over, and validation names the generic field.
Assert-Content $manifestPath "public string prop = string.Empty;"
Assert-Content $manifestPath "public string ResolvedProp =>"
Assert-Content $manifestPath "!string.IsNullOrWhiteSpace(prop) ? prop : tablet;"
Assert-Content $manifestPath "manifest_socket_prop_empty"
Assert-NotContent $manifestPath "manifest_socket_tablet_empty"

Assert-Content $presenterPath "AssetBundle.LoadFromFile"
Assert-Content $presenterPath "LoadAsset<GameObject>"
Assert-Content $presenterPath "LoadAsset<RuntimeAnimatorController>"
Assert-Content $presenterPath "Object.Instantiate"
Assert-Content $presenterPath "gameplayCamera"
Assert-Content $presenterPath "viewmodel.bundle_missing"
Assert-Content $presenterPath "viewmodel.prefab_missing"
Assert-Content $presenterPath "viewmodel.controller_missing"
Assert-Content $presenterPath "local_viewmodel.instantiated"
Assert-Content $presenterPath "local_viewmodel.camera_diagnostics"
Assert-Content $presenterPath "local_viewmodel.renderer_diagnostics"
Assert-Content $presenterPath "local_viewmodel.renderer_material"
Assert-Content $presenterPath "local_viewmodel.anchor_aligned"
Assert-Content $presenterPath "local_viewmodel.runtime_material_applied"
Assert-Content $presenterPath "local_viewmodel.post_frame_renderer_diagnostics"
Assert-Content $presenterPath "ApplyRuntimeSafeViewmodelMaterials"
Assert-Content $presenterPath "TryGetRuntimeMaterialTarget"
Assert-Content $presenterPath "CreateRuntimeSafeMaterial"
Assert-Content $presenterPath "Y4NGZ_RuntimeViewmodel_"
Assert-Content $presenterPath 'SafeGeneratedRuntimeMaterialMode = "safeGenerated"'
Assert-Content $presenterPath "WaitForEndOfFrame"
Assert-Content $presenterPath "AlignViewmodelToCameraAnchor"
Assert-Content $presenterPath "TryFindChildRecursive"
Assert-Content $presenterPath "GeometryUtility.TestPlanesAABB"
Assert-Content $presenterPath "WorldToViewportPoint"
Assert-Content $presenterPath "Object.Destroy"
Assert-Content $presenterPath "ApplyViewmodelRendererVisibility"
Assert-Content $presenterPath "ResolveCameraParent"
Assert-Content $presenterPath "RestoreLiveFirstPersonRenderers"
Assert-NotContent $presenterPath "local_viewmodel.visibility_marker"
Assert-NotContent $presenterPath "CreateDiagnosticVisibilityMarker"
Assert-NotContent $presenterPath "local_viewmodel.diagnostic_material_applied"
Assert-NotContent $presenterPath "ApplyDiagnosticMaterialOverride"
Assert-NotContent $presenterPath "ShouldApplyDiagnosticMaterialOverride"
Assert-NotContent $presenterPath "Y4NGZ_Diagnostic_"
Assert-NotContent $presenterPath "Color.cyan"
Assert-NotContent $presenterPath "local_rotation"
Assert-NotContent $presenterPath "rendererBonesRemapped"

# The presenter reads the prop socket through the alias-resolving accessor, so a legacy
# 'tablet'-only manifest and a modern 'prop' manifest both drive the same code path.
Assert-Content $presenterPath "manifest.sockets.ResolvedProp"
Assert-Content $presenterPath 'target = "prop";'
Assert-NotContent $presenterPath "manifest.sockets.tablet"

# The API project builds the assembly only: animation payloads ship with the consuming mods.
Assert-NotContent $projectPath "DroneTablet"
Assert-NotContent $projectPath "HeavyShotgun"
Assert-NotContent $projectPath "Revolver"
Assert-NotContent $projectPath "animationbundle"
Assert-NotContent $projectPath "runtime-assets"
Assert-NotContent $projectPath "C:\Users\"
Assert-Content $projectPath '<Y4NGZInteractionsDeployFiles Include="$(TargetPath)" />'
Assert-Content $projectPath '<TestProfileRoot Condition="''$(TestProfileRoot)'' == ''''">$(AppData)\com.kesomannen.gale\lethal-company\profiles\terrible</TestProfileRoot>'

Write-Host "Interaction Animation API V2 viewmodel presenter static checks passed."
