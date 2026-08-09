$ErrorActionPreference = "Stop"
$repoRoot = Split-Path -Parent $PSScriptRoot
$files = @(
    (Join-Path $repoRoot "README.md"),
    (Join-Path $repoRoot "CHANGELOG.md")
)
$files += @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "docs") -Recurse -Filter "*.md" |
    ForEach-Object { $_.FullName })
$files += @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "examples") -Recurse -Filter "*.md" |
    ForEach-Object { $_.FullName })
$files += @(Get-ChildItem -LiteralPath (Join-Path $repoRoot "release") -Filter "*.md" |
    ForEach-Object { $_.FullName })

$failures = New-Object Collections.Generic.List[string]
$linkPattern = [regex]'\[[^\]]+\]\(([^)]+)\)'
foreach ($file in $files | Sort-Object -Unique) {
    $text = Get-Content -LiteralPath $file -Raw
    foreach ($match in $linkPattern.Matches($text)) {
        $target = $match.Groups[1].Value.Trim()
        if ($target.StartsWith("http://") -or
            $target.StartsWith("https://") -or
            $target.StartsWith("#") -or
            $target.StartsWith("mailto:")) {
            continue
        }

        $pathPart = ($target -split "#", 2)[0]
        if ([string]::IsNullOrWhiteSpace($pathPart)) {
            continue
        }
        $resolved = Join-Path (Split-Path -Parent $file) $pathPart
        if (-not (Test-Path -LiteralPath $resolved)) {
            $relative = [IO.Path]::GetRelativePath($repoRoot, $file)
            $failures.Add($relative + " -> " + $target)
        }
    }
}

if ($failures.Count -gt 0) {
    throw "Broken Markdown links: $($failures -join '; ')"
}
Write-Output "Markdown link check passed for $($files.Count) files."
