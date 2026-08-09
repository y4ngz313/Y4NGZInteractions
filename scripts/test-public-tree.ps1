$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$brand = "Y4NGZ"
$consumerSuffixes = @("Upgrades", "Company", "Monsters")
$forbiddenNames = @($consumerSuffixes | ForEach-Object { $brand + $_ })
$skip = @(
    "AGENTS.md",
    "CLAUDE.md",
    "scripts/test-public-tree.ps1"
)

$tracked = @(git -C $repoRoot ls-files --cached --others --exclude-standard)
$violations = New-Object Collections.Generic.List[string]
foreach ($relativePath in $tracked) {
    $normalized = $relativePath.Replace("\", "/")
    if ($skip -contains $normalized -or $normalized.EndsWith(".png")) {
        continue
    }
    $fullPath = Join-Path $repoRoot $relativePath
    if (-not (Test-Path -LiteralPath $fullPath -PathType Leaf)) {
        continue
    }

    $text = Get-Content -LiteralPath $fullPath -Raw -ErrorAction SilentlyContinue
    if ($null -eq $text) {
        continue
    }

    foreach ($name in $forbiddenNames) {
        if ($text.IndexOf($name, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
            $violations.Add($normalized + ": consumer name")
        }
    }
    if ($text -match "[A-Za-z]:\\Users\\" -or
        $text -match "[A-Za-z]:\\Lethal Company Modding\\") {
        $violations.Add($normalized + ": machine-local path")
    }
}

if ($violations.Count -gt 0) {
    throw "Public-tree sanitization failed: $($violations -join '; ')"
}

$projectText = Get-Content -LiteralPath (
    Join-Path $repoRoot "src/Y4NGZInteractions/Y4NGZInteractions.csproj") -Raw
foreach ($name in $forbiddenNames) {
    if ($projectText.IndexOf($name, [StringComparison]::OrdinalIgnoreCase) -ge 0) {
        throw "Consumer assembly reference found: $name"
    }
}

Write-Output "Tracked public tree and dependency declarations are consumer-agnostic."
