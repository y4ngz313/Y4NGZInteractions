[CmdletBinding()]
param(
    [switch]$SkipBuild
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$propsPath = Join-Path $repoRoot "Directory.Build.props"
[xml]$props = Get-Content -LiteralPath $propsPath -Raw
$version = [string]$props.Project.PropertyGroup.Y4NGZInteractionsVersion
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Directory.Build.props does not define Y4NGZInteractionsVersion."
}

$projectPath = Join-Path $repoRoot "src/Y4NGZInteractions/Y4NGZInteractions.csproj"
if (-not $SkipBuild) {
    dotnet build $projectPath -c Release --no-restore
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

Copy-Item -LiteralPath $dllPath -Destination (Join-Path $stagingRoot "Y4NGZInteractions.dll")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "icon.png") -Destination (Join-Path $stagingRoot "icon.png")
Copy-Item -LiteralPath (Join-Path $PSScriptRoot "THUNDERSTORE_README.md") -Destination (Join-Path $stagingRoot "README.md")
Copy-Item -LiteralPath (Join-Path $repoRoot "LICENSE") -Destination (Join-Path $stagingRoot "LICENSE")
Copy-Item -LiteralPath (Join-Path $repoRoot "CHANGELOG.md") -Destination (Join-Path $stagingRoot "CHANGELOG.md")

$manifest = [ordered]@{
    name = "Y4NGZInteractions"
    version_number = $version
    website_url = "https://github.com/y4ngz313/Y4NGZInteractions"
    description = "Standalone local animation presentation, ownership, and restoration API for Lethal Company mods."
    dependencies = @("BepInEx-BepInExPack-5.4.2100")
}
$manifest | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $stagingRoot "manifest.json") -Encoding UTF8

$archivePath = Join-Path $distRoot ("Y4NGZInteractions-" + $version + "-candidate.zip")
if (Test-Path -LiteralPath $archivePath) {
    Remove-Item -LiteralPath $archivePath -Force
}
Compress-Archive -Path (Join-Path $stagingRoot "*") -DestinationPath $archivePath

& (Join-Path $repoRoot "scripts/verify-package.ps1") -ArchivePath $archivePath

Write-Output $archivePath
