Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$resolverPath = Join-Path $repoRoot "src/Y4NGZInteractions/InteractionAnimationApi/InteractionAnimationAssetPathResolver.cs"
$resolverSource = Get-Content -Raw -LiteralPath $resolverPath
$shimSource = @'

// Stand-in for BepInEx.Paths so the raw resolver source compiles without BepInEx.dll
// (the NuGet package ships no lib assembly; game installs provide it at runtime). The
// resolver already treats a throwing Paths as "outside a BepInEx host" via try/catch,
// and the shimmed entry points below never reach GetDefaultAssetRoots anyway.
namespace BepInEx
{
    public static class Paths
    {
        public static string PluginPath
        {
            get
            {
                throw new System.InvalidOperationException(
                    "BepInEx host unavailable in static resolver test.");
            }
        }
    }
}

public static class InteractionAnimationAssetPathResolverTestShim
{
    public static bool Normalize(string root, out string normalized, out string reason)
    {
        return Y4NGZInteractions.InteractionAnimationApi.InteractionAnimationAssetPathResolver
            .TryNormalizeAssetRoot(root, out normalized, out reason);
    }

    public static bool Resolve(
        string fileName,
        string root,
        string[] legacyRoots,
        out string resolved,
        out string reason)
    {
        return Y4NGZInteractions.InteractionAnimationApi.InteractionAnimationAssetPathResolver
            .TryResolveBundlePath(fileName, root, legacyRoots, out resolved, out reason);
    }
}
'@
Add-Type -TypeDefinition ($resolverSource + $shimSource)

function Assert-True {
    param([bool] $Value, [string] $Message)
    if (-not $Value) { throw $Message }
}

function Assert-False {
    param([bool] $Value, [string] $Message)
    if ($Value) { throw $Message }
}

function Assert-Equal {
    param([string] $Expected, [string] $Actual, [string] $Message)
    if (-not [string]::Equals($Expected, $Actual, [StringComparison]::OrdinalIgnoreCase)) {
        throw "$Message Expected='$Expected' Actual='$Actual'"
    }
}

$testRoot = Join-Path ([IO.Path]::GetTempPath()) ("Y4NGZInteractionsAssetRoot-" + [Guid]::NewGuid())
$consumerRoot = Join-Path $testRoot "consumer"
$legacyFirst = Join-Path $testRoot "legacy-first"
$legacySecond = Join-Path $testRoot "legacy-second"
$outsideRoot = Join-Path $testRoot "outside"

try {
    New-Item -ItemType Directory -Path $consumerRoot, $legacyFirst, $legacySecond, $outsideRoot | Out-Null
    $consumerBundle = Join-Path $consumerRoot "weapon.animationbundle"
    $legacyBundle = Join-Path $legacySecond "legacy.animationbundle"
    $outsideBundle = Join-Path $outsideRoot "escape.animationbundle"
    New-Item -ItemType File -Path $consumerBundle, $legacyBundle, $outsideBundle | Out-Null

    $normalized = $null
    $reason = $null
    Assert-True ([InteractionAnimationAssetPathResolverTestShim]::Normalize(
        ($consumerRoot + [IO.Path]::DirectorySeparatorChar), [ref] $normalized, [ref] $reason)) `
        "Existing consumer root should normalize."
    Assert-Equal ([IO.Path]::GetFullPath($consumerRoot)) $normalized "Normalized consumer root mismatch."

    $resolved = $null
    Assert-True ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        "weapon.animationbundle", $normalized, @($legacyFirst, $legacySecond), [ref] $resolved, [ref] $reason)) `
        "Relative consumer bundle should resolve."
    Assert-Equal $consumerBundle $resolved "Consumer bundle did not resolve beneath AssetRootPath."

    Assert-False ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        "../outside/escape.animationbundle", $normalized, @(), [ref] $resolved, [ref] $reason)) `
        "Relative path escape should be rejected."
    Assert-True $reason.StartsWith("asset_bundle_path_escapes_root:") "Relative escape reason mismatch."

    Assert-False ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        $outsideBundle, $normalized, @(), [ref] $resolved, [ref] $reason)) `
        "Absolute path escape should be rejected."
    Assert-True $reason.StartsWith("asset_bundle_path_escapes_root:") "Absolute escape reason mismatch."

    $missingRoot = Join-Path $testRoot "missing-root"
    Assert-False ([InteractionAnimationAssetPathResolverTestShim]::Normalize(
        $missingRoot, [ref] $normalized, [ref] $reason)) "Missing consumer root should fail."
    Assert-True $reason.StartsWith("pack_asset_root_missing:") "Missing-root reason mismatch."

    Assert-True ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        "missing.animationbundle", $consumerRoot, @(), [ref] $resolved, [ref] $reason)) `
        "A safe missing path should still resolve for presenter diagnostics."
    Assert-False (Test-Path -LiteralPath $resolved -PathType Leaf) "Missing consumer bundle unexpectedly exists."

    Assert-True ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        "legacy.animationbundle", "", @($legacyFirst, $legacySecond), [ref] $resolved, [ref] $reason)) `
        "Legacy bundle should resolve without AssetRootPath."
    Assert-Equal $legacyBundle $resolved "Legacy fallback order changed."

    Assert-True ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        "legacy-missing.animationbundle", "", @($legacyFirst, $legacySecond), [ref] $resolved, [ref] $reason)) `
        "Legacy missing bundle should retain diagnostic fallback."
    Assert-Equal (Join-Path $legacyFirst "legacy-missing.animationbundle") $resolved `
        "Legacy missing-file diagnostic path changed."

    Assert-True ([InteractionAnimationAssetPathResolverTestShim]::Resolve(
        $outsideBundle, "", @($legacyFirst), [ref] $resolved, [ref] $reason)) `
        "Legacy rooted path should remain supported when AssetRootPath is absent."
    Assert-Equal $outsideBundle $resolved "Legacy rooted-path behavior changed."
}
finally {
    $resolvedTestRoot = [IO.Path]::GetFullPath($testRoot)
    $resolvedSystemTemp = [IO.Path]::GetFullPath([IO.Path]::GetTempPath())
    if ($resolvedTestRoot.StartsWith($resolvedSystemTemp, [StringComparison]::OrdinalIgnoreCase) -and
        (Test-Path -LiteralPath $resolvedTestRoot)) {
        Remove-Item -LiteralPath $resolvedTestRoot -Recurse -Force
    }
}

Write-Host "Interaction animation consumer-root resolution checks passed."
