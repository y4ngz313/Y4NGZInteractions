<#
.SYNOPSIS
    Stage the Y4NGZInteractions Thunderstore package.
.DESCRIPTION
    Y4NGZInteractions is a pure animation API: the package is the DLL plus
    Thunderstore metadata, and it ships no animation payload of its own. Every
    bundle, live-body manifest, and finger-pose file belongs to the consuming
    mod, which registers it through the API with its own AssetRootPath.

    Builds the Y4NGZInteractions DLL, writes Thunderstore metadata at the
    package root, verifies the layout (including that no consumer payload leaked
    into the package), and optionally creates
    release/dist/y4ngz313-Y4NGZInteractions-<version>.zip.
#>
[CmdletBinding()]
param(
    [Parameter(Mandatory = $true)]
    [string]$Version,

    [switch]$SkipBuild,
    [switch]$NoZip,
    [switch]$Verify,

    [string]$WebsiteUrl = "https://github.com/y4ngz313/Y4NGZInteractions"
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest

$PackageName = "Y4NGZInteractions"
$PackageNamespace = "y4ngz313"
$PackageId = "$PackageNamespace-$PackageName"
$Description = "Shared Y4NGZ interaction animation API: player animation ownership, lease arbitration, restore guards, and first-person interaction animation playback for Y4NGZ feature mods."

$RepoRoot = Split-Path $PSScriptRoot -Parent
$ProjectPath = Join-Path $RepoRoot "src\Y4NGZInteractions\Y4NGZInteractions.csproj"
$OutputDir = Join-Path $RepoRoot "src\Y4NGZInteractions\bin\Release"
$StagingRoot = Join-Path $PSScriptRoot "staging"
$DistDir = Join-Path $PSScriptRoot "dist"
$StagePath = Join-Path $StagingRoot $PackageName
$BuildDeploySink = Join-Path $PSScriptRoot "build-deploy-sink"

# Y4NGZInteractions has no [BepInDependency] attributes and links only against
# BepInEx, HarmonyX (shipped inside BepInExPack), the game assemblies, and the
# game's own Unity.InputSystem. BepInEx is therefore the only hard dependency.
$Dependencies = @(
    "BepInEx-BepInExPack-5.4.2305"
)

$OptionalIntegrations = @(
    "Y4NGZUpgrades and other Y4NGZ feature mods consume this API when installed",
    "Animation payloads ship with the consuming mod, which passes its own AssetRootPath when registering a pack"
)

# Never ship these. Debug symbols, nested archives, Unity bundle-build side-cars,
# and authoring notes.
$BannedStagePatterns = @(
    "*.pdb",
    "*.zip",
    "*.animationbundle.manifest",
    "*.validation.md",
    "runtime-assets",
    "runtime-assets.manifest"
)

# A pure library must never carry consumer animation content. Staging fails if
# any of these land in the package, whatever produced them.
$BannedPayloadPatterns = @(
    "*.animationbundle",
    "*.lethalbundle",
    "*-livebody.manifest.json",
    "*-fp-finger-poses.json"
)

function Write-Step([string]$Message) {
    Write-Host ""
    Write-Host "--- $Message ---" -ForegroundColor Cyan
}

function Get-SizeLabel([long]$Bytes) {
    if ($Bytes -ge 1MB) { return ("{0} MB" -f [math]::Round($Bytes / 1MB, 2)) }
    return ("{0} KB" -f [math]::Round($Bytes / 1KB, 1))
}

function Initialize-Stage {
    if ($Version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Version '$Version' must use Thunderstore's numeric x.y.z format, such as 1.0.0."
    }

    New-Item -ItemType Directory -Path $StagingRoot -Force | Out-Null
    New-Item -ItemType Directory -Path $DistDir -Force | Out-Null

    $resolvedRoot = [System.IO.Path]::GetFullPath($StagingRoot)
    $resolvedStage = [System.IO.Path]::GetFullPath($StagePath)
    if (-not $resolvedStage.StartsWith($resolvedRoot, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Refusing to clear unexpected staging path: $resolvedStage"
    }

    if (Test-Path -LiteralPath $StagePath) {
        Remove-Item -LiteralPath $StagePath -Recurse -Force
    }
    New-Item -ItemType Directory -Path $StagePath -Force | Out-Null
}

function Invoke-ReleaseBuild {
    if ($SkipBuild) { return }

    Write-Step "Building Release DLL"
    if (-not (Test-Path -LiteralPath $ProjectPath -PathType Leaf)) {
        throw "Missing project file: $ProjectPath"
    }

    # DeployTarget is redirected so a packaging run never writes into the
    # author's Gale test profile.
    $output = & dotnet build $ProjectPath -c Release /p:DeployTarget="$BuildDeploySink" 2>&1
    if ($LASTEXITCODE -ne 0) {
        Write-Host ($output | Out-String) -ForegroundColor DarkRed
        throw "Build failed for $ProjectPath"
    }
    Write-Host "Build succeeded." -ForegroundColor Green
}

function Copy-RequiredFile([string]$SourcePath, [string]$DestinationName) {
    if (-not (Test-Path -LiteralPath $SourcePath -PathType Leaf)) {
        throw "Missing required file: $SourcePath"
    }

    $destination = Join-Path $StagePath $DestinationName
    Copy-Item -LiteralPath $SourcePath -Destination $destination -Force
    $item = Get-Item -LiteralPath $destination
    Write-Host ("  {0} ({1})" -f $DestinationName, (Get-SizeLabel $item.Length))
}

function Copy-RuntimeFiles {
    Write-Step "Copying runtime files"

    $dllPath = Join-Path $OutputDir "Y4NGZInteractions.dll"
    Copy-RequiredFile -SourcePath $dllPath -DestinationName "Y4NGZInteractions.dll"
}

function Write-Metadata {
    Write-Step "Writing Thunderstore metadata"

    $manifest = [ordered]@{
        name = $PackageName
        version_number = $Version
        description = $Description
        website_url = $WebsiteUrl
        dependencies = @($Dependencies)
    }
    $manifest | ConvertTo-Json -Depth 4 | Set-Content -Path (Join-Path $StagePath "manifest.json") -Encoding UTF8

    $iconSource = Join-Path $PSScriptRoot "icon.png"
    Copy-RequiredFile -SourcePath $iconSource -DestinationName "icon.png"

    $authoredReadme = Join-Path $PSScriptRoot "THUNDERSTORE_README.md"
    if (Test-Path -LiteralPath $authoredReadme -PathType Leaf) {
        Copy-Item -LiteralPath $authoredReadme -Destination (Join-Path $StagePath "README.md") -Force
        Write-Host "  manifest.json"
        Write-Host "  README.md (authored: release/THUNDERSTORE_README.md)"
        return
    }

    $readme = [System.Collections.Generic.List[string]]::new()
    $readme.Add("# $PackageName")
    $readme.Add("")
    $readme.Add($Description)
    $readme.Add("")
    $readme.Add("BepInEx GUID: ``com.y4ngz.interactions``")
    $readme.Add("")
    $readme.Add("## Included files")
    $readme.Add("- Y4NGZInteractions.dll")
    $readme.Add("")
    $readme.Add("## Notes")
    foreach ($integration in $OptionalIntegrations) {
        $readme.Add("- $integration")
    }
    $readme.Add("")
    $readme.Add("Generated by release/stage-y4ngz-interactions.ps1.")
    $readme | Set-Content -Path (Join-Path $StagePath "README.md") -Encoding UTF8

    Write-Host "  manifest.json"
    Write-Host "  README.md"
}

function Test-StageLayout {
    Write-Step "Verifying staged layout"

    foreach ($required in @("manifest.json", "icon.png", "README.md", "Y4NGZInteractions.dll")) {
        if (-not (Test-Path -LiteralPath (Join-Path $StagePath $required) -PathType Leaf)) {
            throw "Missing staged file: $required"
        }
    }

    foreach ($bannedPattern in $BannedStagePatterns) {
        $banned = @(Get-ChildItem -Path $StagePath -Recurse -Filter $bannedPattern -File -ErrorAction SilentlyContinue)
        if ($banned.Count -gt 0) {
            throw "Banned file staged ($bannedPattern): $($banned.Name -join ', ')"
        }
    }

    # The API ships no animation content; a payload here means a consumer asset
    # leaked back into the library package.
    foreach ($bannedPattern in $BannedPayloadPatterns) {
        $payload = @(Get-ChildItem -Path $StagePath -Recurse -Filter $bannedPattern -File -ErrorAction SilentlyContinue)
        if ($payload.Count -gt 0) {
            throw "Consumer animation payload staged ($bannedPattern): $($payload.Name -join ', '). Payloads belong to the consuming mod, not this API."
        }
    }

    $dlls = @(Get-ChildItem -Path $StagePath -Recurse -Filter "*.dll" -File)
    $unexpectedDlls = @($dlls | Where-Object { $_.Name -ne "Y4NGZInteractions.dll" -or $_.DirectoryName -ne $StagePath })
    if ($unexpectedDlls.Count -gt 0) {
        throw "Unexpected DLL layout: $($unexpectedDlls.FullName -join ', ')"
    }

    Get-Content -LiteralPath (Join-Path $StagePath "manifest.json") -Raw | ConvertFrom-Json | Out-Null

    $stagedFiles = @(Get-ChildItem -Path $StagePath -Recurse -File)
    $stagedBytes = ($stagedFiles | Measure-Object -Property Length -Sum).Sum
    Write-Host ("Staged {0} files ({1})." -f $stagedFiles.Count, (Get-SizeLabel $stagedBytes))
    Write-Host "Verification passed for $PackageName." -ForegroundColor Green
}

function New-PackageZip {
    if ($NoZip -or $Verify) {
        Write-Host "Zip skipped. Staged files are in $StagePath" -ForegroundColor Cyan
        return
    }

    Write-Step "Creating zip"
    $zipPath = Join-Path $DistDir "$PackageId-$Version.zip"
    if (Test-Path -LiteralPath $zipPath) {
        Remove-Item -LiteralPath $zipPath -Force
    }

    Compress-Archive -Path (Join-Path $StagePath "*") -DestinationPath $zipPath -CompressionLevel Optimal
    $zip = Get-Item -LiteralPath $zipPath
    Write-Host "Created: $zipPath" -ForegroundColor Green
    Write-Host ("Size:    {0}" -f (Get-SizeLabel $zip.Length))
}

Write-Step "Staging $PackageId $Version"
Initialize-Stage
Invoke-ReleaseBuild
Copy-RuntimeFiles
Write-Metadata
Test-StageLayout
New-PackageZip
