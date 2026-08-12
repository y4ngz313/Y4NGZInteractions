[CmdletBinding()]
param(
    [switch]$SkipBuild,
    [switch]$NoZip
)

$ErrorActionPreference = "Stop"
Set-StrictMode -Version Latest
$packageName = "Y4NGZInteractions"
$packageNamespace = "y4ngz313"
$packageId = "$packageNamespace-$packageName"
$websiteUrl = "https://github.com/y4ngz313/Y4NGZInteractions"
$description = "Shared local animation presentation, resource ownership, lifecycle, and restoration API for Lethal Company mods."
$dependencies = @("BepInEx-BepInExPack-5.4.2305")

$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$version = [string]$props.Project.PropertyGroup.Y4NGZInteractionsVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Directory.Build.props does not define Y4NGZInteractionsVersion."
}
if ($version -notmatch '^\d+\.\d+\.\d+$') {
    throw "Canonical version '$version' must be a three-part numeric Thunderstore version."
}

$projectPath = Join-Path $repoRoot "src/Y4NGZInteractions/Y4NGZInteractions.csproj"
if (-not $SkipBuild) {
    dotnet build $projectPath -c Release --no-restore /p:EnableProfileDeploy=false
    if ($LASTEXITCODE -ne 0) {
        throw "Release build failed."
    }
}

$dllPath = Join-Path $repoRoot "src/Y4NGZInteractions/bin/Release/Y4NGZInteractions.dll"
if (-not (Test-Path -LiteralPath $dllPath -PathType Leaf)) {
    throw "Release DLL is missing: $dllPath"
}

$fileVersion = [Diagnostics.FileVersionInfo]::GetVersionInfo($dllPath).FileVersion
if (-not ($fileVersion -eq $version -or $fileVersion.StartsWith($version + "."))) {
    throw "DLL version '$fileVersion' does not match canonical version '$version'."
}

$stagingRoot = Join-Path $PSScriptRoot "staging"
$distRoot = Join-Path $PSScriptRoot "dist"
$resolvedRelease = [IO.Path]::GetFullPath($PSScriptRoot)
$resolvedStaging = [IO.Path]::GetFullPath($stagingRoot)
if (-not $resolvedStaging.StartsWith($resolvedRelease, [StringComparison]::OrdinalIgnoreCase)) {
    throw "Staging path escaped the release directory."
}
if (Test-Path -LiteralPath $stagingRoot) {
    Remove-Item -LiteralPath $stagingRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $stagingRoot -Force | Out-Null
New-Item -ItemType Directory -Path $distRoot -Force | Out-Null

$sourceFiles = [ordered]@{
    "Y4NGZInteractions.dll" = $dllPath
    "icon.png" = Join-Path $PSScriptRoot "icon.png"
    "README.md" = Join-Path $PSScriptRoot "THUNDERSTORE_README.md"
    "LICENSE" = Join-Path $repoRoot "LICENSE"
    "CHANGELOG.md" = Join-Path $repoRoot "CHANGELOG.md"
}
foreach ($entry in $sourceFiles.GetEnumerator()) {
    if (-not (Test-Path -LiteralPath $entry.Value -PathType Leaf)) {
        throw "Required package file is missing: $($entry.Value)"
    }
    Copy-Item -LiteralPath $entry.Value -Destination (Join-Path $stagingRoot $entry.Key)
}

$manifest = [ordered]@{
    name = $packageName
    version_number = $version
    website_url = $websiteUrl
    description = $description
    dependencies = $dependencies
}
$manifestPath = Join-Path $stagingRoot "manifest.json"
$manifest | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath $manifestPath -Encoding UTF8

$stagedManifest = Get-Content -LiteralPath $manifestPath -Raw | ConvertFrom-Json
$actualDependencies = @($stagedManifest.dependencies | ForEach-Object { [string]$_ })
if ([string]$stagedManifest.name -cne $packageName -or
    [string]$stagedManifest.version_number -cne $version -or
    [string]$stagedManifest.website_url -cne $websiteUrl -or
    [string]$stagedManifest.description -cne $description) {
    throw "Staged manifest metadata does not match the canonical package metadata."
}
if ($description.Length -gt 250) {
    throw "Thunderstore manifest description exceeds 250 characters."
}
if (($actualDependencies -join "`n") -cne ($dependencies -join "`n")) {
    throw "Staged manifest dependencies do not match the audited dependency list."
}

$authoredReadme = Join-Path $PSScriptRoot "THUNDERSTORE_README.md"
$stagedReadme = Join-Path $stagingRoot "README.md"
if ((Get-FileHash -LiteralPath $authoredReadme -Algorithm SHA256).Hash -cne
    (Get-FileHash -LiteralPath $stagedReadme -Algorithm SHA256).Hash) {
    throw "Staged README.md does not match release/THUNDERSTORE_README.md."
}
$readmeText = Get-Content -LiteralPath $stagedReadme -Raw
if ($readmeText -notmatch '(?m)^#\s+\S' -or $readmeText -match '(?m)^#{1,6}(?!#)\S') {
    throw "Thunderstore README headings are missing or malformed."
}
if ($readmeText -match '(?i)\b[A-Z]:\\|file://') {
    throw "Thunderstore README contains a machine-local path."
}
if (-not $readmeText.Contains($dependencies[0])) {
    throw "Thunderstore README does not document the required package dependency."
}

$expectedEntries = @($sourceFiles.Keys) + "manifest.json" | Sort-Object
$actualEntries = @(Get-ChildItem -LiteralPath $stagingRoot -Force | ForEach-Object { $_.Name }) | Sort-Object
if (($actualEntries -join "|") -cne ($expectedEntries -join "|")) {
    throw "Unexpected staging contents. Actual: $($actualEntries -join ', ')"
}

if ($NoZip) {
    Write-Output $stagingRoot
    return
}

$archivePath = Join-Path $distRoot ($packageId + "-" + $version + ".zip")
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $archivePath

& (Join-Path $repoRoot "scripts/verify-package.ps1") -ArchivePath $archivePath

Write-Output $archivePath
