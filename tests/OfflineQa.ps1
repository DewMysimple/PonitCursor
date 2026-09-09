# Optional elevated QA: only these test executables are blocked, in a try/finally.
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$bin = Join-Path $root 'build\tests'
$prefix = 'PointCursor-QA-' + [Guid]::NewGuid().ToString('N')
$createdRules = @()
try {
    $index = 0
    foreach ($exe in @('PointCursor.exe','PointCursor.Reader.exe','PointCursor.Tests.exe','Kokoro\node.exe')) {
        $path = (Resolve-Path -LiteralPath (Join-Path $bin $exe)).Path
        $name = $prefix + '-' + $index++
        New-NetFirewallRule -Name $name -DisplayName $name -Direction Outbound -Action Block -Program $path -Profile Any -Enabled True | Out-Null
        $createdRules += $name
    }
    & (Join-Path $bin 'PointCursor.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Offline unit/audio checks failed.' }
    & python (Join-Path $PSScriptRoot 'desktop_qa.py')
    if ($LASTEXITCODE -ne 0) { throw 'Offline desktop checks failed.' }
    Write-Output 'PASS: local audio and desktop workflow with outbound network blocked.'
} finally {
    foreach ($name in $createdRules) { Remove-NetFirewallRule -Name $name }
}
