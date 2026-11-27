[CmdletBinding()]
param(
    [string]$ArchivePath
)

$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot

# The compiler records the PDB path in each DLL; the Release PathMap in the build props maps the
# repository root to /_/. Refuse a staged file that still holds a path of the machine that built it.
function Assert-NoBuildMachinePath([byte[]]$Bytes, [string]$Label, [string]$RepoRoot) {
    $texts = @([System.Text.Encoding]::UTF8.GetString($Bytes), [System.Text.Encoding]::Unicode.GetString($Bytes))
    if ($Bytes.Length -gt 1) {
        $texts += [System.Text.Encoding]::Unicode.GetString($Bytes, 1, $Bytes.Length - 1)
    }
    $root = (($RepoRoot.TrimEnd('\', '/') -split '[\\/]') | ForEach-Object { [regex]::Escape($_) }) -join '[\\/]'
    foreach ($text in $texts) {
        $hit = [regex]::Match($text, "[A-Za-z]:[\\/]Users[\\/][\x20-\x7E]*|$root[\x20-\x7E]*", 'IgnoreCase')
        if ($hit.Success) {
            throw "Staged file '$Label' carries a build-machine path: $($hit.Value.Substring(0, [Math]::Min($hit.Value.Length, 120)))"
        }
    }
}

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

    foreach ($entry in @($archive.Entries | Where-Object { $_.Name -match '\.(dll|pdb)$' })) {
        $buffer = New-Object IO.MemoryStream
        $entryStream = $entry.Open()
        try {
            $entryStream.CopyTo($buffer)
        }
        finally {
            $entryStream.Dispose()
        }
        Assert-NoBuildMachinePath -Bytes $buffer.ToArray() -Label $entry.FullName -RepoRoot $repoRoot
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
    $expectedDescription = "Animation library used by Y4NGZ mods. Adds nothing on its own."
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
}
finally {
    $archive.Dispose()
}

Write-Output "Package verification passed: $ArchivePath"
