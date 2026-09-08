param([Parameter(Mandatory = $true)][string]$Executable)
$ErrorActionPreference = 'Stop'
if (-not (Test-Path -LiteralPath $Executable -PathType Leaf)) { throw 'Obsidian executable not found.' }
$qaRoot = Join-Path (Split-Path $PSScriptRoot -Parent) 'build\qa'
$profileRoot = Join-Path $qaRoot 'isolated-appdata'
$profile = Join-Path $profileRoot 'obsidian'
$vault = Join-Path $qaRoot 'ObsidianTestVault'
New-Item -ItemType Directory -Force -Path $profile,(Join-Path $vault '.obsidian') | Out-Null
$utf8 = New-Object System.Text.UTF8Encoding($false)
$config = @{ vaults = @{ '0123456789abcdef' = @{ path = $vault; ts = [DateTimeOffset]::UtcNow.ToUnixTimeMilliseconds(); open = $true } } }
[IO.File]::WriteAllText((Join-Path $profile 'obsidian.json'), ($config | ConvertTo-Json -Depth 5), $utf8)
$note = "# PointCursor pronunciation test`n`nhello world`n`nwell-known don't English`n`nThis is a synthetic test note.`n"
[IO.File]::WriteAllText((Join-Path $vault 'Pronunciation.md'), $note, $utf8)
[IO.File]::WriteAllText((Join-Path $vault '.obsidian\app.json'), '{"legacyEditor":false,"livePreview":true,"showInlineTitle":false}', $utf8)
# Copy only a public application update, never user settings, notes, plugins or secrets.
$updatedApp = Get-ChildItem -LiteralPath (Join-Path $env:APPDATA 'obsidian') -Filter 'obsidian-*.asar' -ErrorAction SilentlyContinue | Sort-Object LastWriteTime -Descending | Select-Object -First 1
if ($updatedApp) { Copy-Item -LiteralPath $updatedApp.FullName -Destination $profile -Force }
$si = New-Object System.Diagnostics.ProcessStartInfo
$si.FileName = (Resolve-Path -LiteralPath $Executable).Path
$si.Arguments = '--user-data-dir="' + $profile + '" --remote-debugging-port=9237 --remote-debugging-address=127.0.0.1'
$si.UseShellExecute = $false
$si.WindowStyle = 'Hidden'
$si.EnvironmentVariables['APPDATA'] = $profileRoot
$process = [System.Diagnostics.Process]::Start($si)
$process.Id | Set-Content -LiteralPath (Join-Path $qaRoot 'obsidian-test.pid')
Write-Output 'Open Pronunciation.md in the isolated ObsidianTestVault, then run the QA scripts.'
