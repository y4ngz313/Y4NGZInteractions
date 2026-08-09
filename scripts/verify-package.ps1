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
        "release/dist/Y4NGZInteractions-" + $version + "-candidate.zip")
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
}
finally {
    $archive.Dispose()
}

Write-Output "Package verification passed: $ArchivePath"
