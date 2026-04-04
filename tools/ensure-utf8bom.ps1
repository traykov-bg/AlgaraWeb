# tools/ensure-utf8bom.ps1
# Called from .git/hooks/pre-commit
# Re-encodes staged .cs/.cshtml/.css/.js files to UTF-8 with BOM.

$enc = New-Object System.Text.UTF8Encoding($true)

$staged = & git diff --cached --name-only --diff-filter=ACM |
          Where-Object { $_ -match '\.(cs|cshtml|css|js)$' }

if (-not $staged) { exit 0 }

# Convert Unix-style path from git (/e/.NETProjects/...) to Windows (E:\.NETProjects\...)
$rootRaw = & git rev-parse --show-toplevel
if ($rootRaw -match '^/([a-zA-Z])/(.+)') {
    $root = $Matches[1].ToUpper() + ':' + '\' + ($Matches[2] -replace '/', '\')
} else {
    $root = $rootRaw -replace '/', '\'
}

$converted = 0
foreach ($rel in $staged) {
    $path = Join-Path $root ($rel -replace '/', '\')
    if (-not (Test-Path $path)) { continue }

    # Skip files that already have BOM (EF BB BF)
    $bytes = [IO.File]::ReadAllBytes($path)
    $hasBom = ($bytes.Length -ge 3) -and
              ($bytes[0] -eq 0xEF) -and
              ($bytes[1] -eq 0xBB) -and
              ($bytes[2] -eq 0xBF)

    if ($hasBom) { continue }

    $content = [IO.File]::ReadAllText($path, [Text.Encoding]::UTF8)
    [IO.File]::WriteAllText($path, $content, $enc)
    & git add $rel
    $converted++
    Write-Host "  BOM added: $rel"
}

if ($converted -gt 0) {
    Write-Host "UTF-8 BOM: $converted file(s) converted."
} else {
    Write-Host "UTF-8 BOM: all files already encoded correctly."
}

exit 0
