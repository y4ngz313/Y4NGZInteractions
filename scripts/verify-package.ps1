[CmdletBinding()]
param(
    [string]$ArchivePath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($ArchivePath)) {
    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Raw
    $version = [string]$props.Project.PropertyGroup.Y4NGZInteractionsVersion
    $ArchivePath = Join-Path $repoRoot (
        "release/dist/y4ngz313-Y4NGZInteractions-" + $version + ".zip")
}
$ArchivePath = [IO.Path]::GetFullPath($ArchivePath)
if (-not (Test-Path -LiteralPath $ArchivePath -PathType Leaf)) {
    throw "Candidate archive is missing: $ArchivePath"
}

Add-Type -AssemblyName System.IO.Compression.FileSystem
$archive = [IO.Compression.ZipFile]::OpenRead($ArchivePath)
try {
    $actual = @($archive.Entries |
        Where-Object { -not [string]::IsNullOrEmpty($_.Name) } |
        ForEach-Object { $_.FullName.Replace("\", "/") } |
        Sort-Object)
    $expected = @(
        "CHANGELOG.md",
        "LICENSE",
        "README.md",
        "Y4NGZInteractions.dll",
        "icon.png",
        "manifest.json"
    ) | Sort-Object

    if (($actual -join "|") -ne ($expected -join "|")) {
        throw "Unexpected package contents. Actual: $($actual -join ', ')"
    }

    $manifestEntry = $archive.GetEntry("manifest.json")
    $reader = New-Object IO.StreamReader($manifestEntry.Open())
    try {
        $manifest = $reader.ReadToEnd() | ConvertFrom-Json
    }
    finally {
        $reader.Dispose()
    }

    [xml]$props = Get-Content -LiteralPath (Join-Path $repoRoot "Directory.Build.props") -Raw
    $version = [string]$props.Project.PropertyGroup.Y4NGZInteractionsVersion
    if ($manifest.version_number -ne $version) {
        throw "Manifest version '$($manifest.version_number)' does not match '$version'."
    }
    if ($version -notmatch '^\d+\.\d+\.\d+$') {
        throw "Manifest version '$version' is not a three-part numeric Thunderstore version."
    }

    $expectedName = "Y4NGZInteractions"
    $expectedWebsite = "https://github.com/y4ngz313"
    $expectedDescription = "Shared local animation presentation, resource ownership, lifecycle, and restoration API for Lethal Company mods."
    $expectedDependencies = @("BepInEx-BepInExPack-5.4.2305")
    $actualDependencies = @($manifest.dependencies | ForEach-Object { [string]$_ })
    if ([string]$manifest.name -cne $expectedName -or
        [string]$manifest.website_url -cne $expectedWebsite -or
        [string]$manifest.description -cne $expectedDescription) {
        throw "Archive manifest metadata does not match the canonical package metadata."
    }
    if ($expectedDescription.Length -gt 250) {
        throw "Thunderstore manifest description exceeds 250 characters."
    }
    if (($actualDependencies -join "`n") -cne ($expectedDependencies -join "`n")) {
        throw "Archive manifest dependencies do not match the audited dependency list."
    }

    $readmeEntry = $archive.GetEntry("README.md")
    $readmeReader = New-Object IO.StreamReader($readmeEntry.Open())
    try {
        $readmeText = $readmeReader.ReadToEnd()
    }
    finally {
        $readmeReader.Dispose()
    }
    if ($readmeText -notmatch '(?m)^#\s+\S' -or
        $readmeText -match '(?m)^#{1,6}(?!#)\S') {
        throw "Archive README headings are missing or malformed."
    }
    if ($readmeText -match '(?i)\b[A-Z]:\\|file://') {
        throw "Archive README contains a machine-local path."
    }
    if ($readmeText -match '(?i)https://github\.com/y4ngz313/Y4NGZInteractions(?:[/#?]|$)') {
        throw "Archive README links to the private development repository."
    }
    if (-not $readmeText.Contains($expectedDependencies[0])) {
        throw "Archive README does not document the required package dependency."
    }
}
finally {
    $archive.Dispose()
}

Write-Output "Package verification passed: $ArchivePath"
