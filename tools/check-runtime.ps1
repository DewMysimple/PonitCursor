param([string]$Runtime = (Join-Path $PSScriptRoot '..\third_party\kokoro-runtime'))
$ErrorActionPreference = 'Stop'
$base = [IO.Path]::GetFullPath($Runtime).TrimEnd('\') + '\'
$manifest = Join-Path $base 'assets.tsv'
if (-not (Test-Path -LiteralPath $manifest)) { throw 'Missing assets.tsv; use a complete release or repository checkout.' }
$count = 0
foreach ($line in Get-Content -LiteralPath $manifest -Encoding UTF8) {
    if (-not $line -or $line.StartsWith('#')) { continue }
    $fields = $line.Split("`t")
    if ($fields.Count -ne 3 -or $fields[0] -notmatch '^\d+$' -or $fields[1] -notmatch '^[a-f0-9]{64}$') { throw 'Invalid runtime manifest.' }
    $assetPath = [IO.Path]::GetFullPath((Join-Path $base $fields[2]))
    if (-not $assetPath.StartsWith($base, [StringComparison]::OrdinalIgnoreCase)) { throw 'Invalid runtime path.' }
    if (-not (Test-Path -LiteralPath $assetPath) -or (Get-Item -LiteralPath $assetPath).Length -ne [long]$fields[0]) { throw ('Missing/truncated runtime asset: ' + $fields[2] + '. Run git lfs pull and rebuild.') }
    if ((Get-FileHash -LiteralPath $assetPath -Algorithm SHA256).Hash.ToLowerInvariant() -ne $fields[1]) { throw ('Runtime hash mismatch: ' + $fields[2]) }
    $count++
}
if ($count -lt 20) { throw 'Incomplete runtime manifest.' }
Write-Output ('Runtime verified: ' + $count + ' assets (SHA-256).')
