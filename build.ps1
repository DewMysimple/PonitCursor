param([switch]$Test, [switch]$Package, [string]$OutputDirectory = 'dist\PointCursor')
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$speech = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech') -Recurse -Filter System.Speech.dll | Select-Object -First 1 -ExpandProperty FullName
if (-not $speech) { throw 'System.Speech is required.' }
$out = [IO.Path]::GetFullPath((Join-Path $root $OutputDirectory))
if (-not ($out.StartsWith((Join-Path $root 'build\'), [StringComparison]::OrdinalIgnoreCase) -or $out.StartsWith((Join-Path $root 'dist\'), [StringComparison]::OrdinalIgnoreCase))) { throw 'OutputDirectory must stay under build or dist.' }
New-Item -ItemType Directory -Force -Path $out | Out-Null
$kokoroSource = Join-Path $root 'third_party\kokoro-runtime'
& (Join-Path $root 'tools\check-runtime.ps1') -Runtime $kokoroSource
$common = @('/nologo','/optimize+','/platform:x64','/warn:4','/utf8output',('/r:' + (Join-Path $framework 'System.dll')),('/r:' + (Join-Path $framework 'System.Core.dll')))
function Compile([string[]]$CompilerArgs) {
    & $compiler @common @CompilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
}
function SyncRuntime([string]$Source, [string]$Destination) {
    $target = [IO.Path]::GetFullPath($Destination).TrimEnd('\') + '\'
    if (-not ($target.StartsWith((Join-Path $root 'build\'), [StringComparison]::OrdinalIgnoreCase) -or $target.StartsWith((Join-Path $root 'dist\'), [StringComparison]::OrdinalIgnoreCase))) { throw 'Unsafe runtime destination.' }
    New-Item -ItemType Directory -Force -Path $target | Out-Null
    $expected = @{'assets.tsv' = $true}
    foreach ($line in Get-Content -LiteralPath (Join-Path $Source 'assets.tsv') -Encoding UTF8) {
        if (-not $line -or $line.StartsWith('#')) { continue }
        $relative = $line.Split("`t")[2].Replace('/', '\'); $expected[$relative] = $true
        $destinationFile = Join-Path $target $relative
        New-Item -ItemType Directory -Force -Path ([IO.Path]::GetDirectoryName($destinationFile)) | Out-Null
        Copy-Item -LiteralPath (Join-Path $Source $relative) -Destination $destinationFile -Force
    }
    Copy-Item -LiteralPath (Join-Path $Source 'assets.tsv') -Destination $target -Force
    foreach ($file in Get-ChildItem -LiteralPath $target -File -Recurse) {
        $resolved = [IO.Path]::GetFullPath($file.FullName)
        if (-not $resolved.StartsWith($target, [StringComparison]::OrdinalIgnoreCase)) { throw 'Runtime file outside destination.' }
        if (-not $expected.ContainsKey($resolved.Substring($target.Length))) { Remove-Item -LiteralPath $resolved -Force }
    }
}
$core = Join-Path $out 'PointCursor.Core.dll'
Compile @('/target:library', ('/out:' + $core), (Join-Path $root 'src\Core.cs'))
$native = Join-Path $root 'src\Native.cs'
$uia = @(('/r:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')), ('/r:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')), ('/r:' + (Join-Path $framework 'WPF\WindowsBase.dll')))
Compile (@('/target:exe',('/out:' + (Join-Path $out 'PointCursor.Reader.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core),('/r:' + (Join-Path $framework 'Accessibility.dll')),$native,(Join-Path $root 'src\SelectionReader.cs'),(Join-Path $root 'src\LegacySelection.cs')) + $uia)
$desktop = @(('/r:' + (Join-Path $framework 'System.Windows.Forms.dll')), ('/r:' + (Join-Path $framework 'System.Drawing.dll')), ('/r:' + $speech))
$audio = @('PcmAudio.cs','AudioOutput.cs','RuntimeAssets.cs','SapiSpeechEngine.cs') | ForEach-Object { Join-Path $root ('src\' + $_) }
$app = @('Native.cs','SelectionClient.cs','InputMonitor.cs','KokoroSpeechEngine.cs','SpeechService.cs','SettingsForm.cs','App.cs') | ForEach-Object { Join-Path $root ('src\' + $_) }
$app += $audio
Compile (@('/target:winexe',('/out:' + (Join-Path $out 'PointCursor.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core)) + $desktop + $app)
Copy-Item -LiteralPath (Join-Path $root 'PointCursor.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'PointCursor.exe.config') -Destination (Join-Path $out 'PointCursor.Reader.exe.config') -Force
foreach ($doc in @('README.md','COMPATIBILITY.md')) { if (Test-Path -LiteralPath (Join-Path $root $doc)) { Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $out -Force } }
Copy-Item -LiteralPath (Join-Path $root 'diagnose.ps1') -Destination $out -Force
New-Item -ItemType Directory -Force -Path (Join-Path $out 'tools') | Out-Null
Copy-Item -LiteralPath (Join-Path $root 'tools\check-runtime.ps1') -Destination (Join-Path $out 'tools') -Force
$kokoroOut = Join-Path $out 'Kokoro'
New-Item -ItemType Directory -Force -Path $kokoroOut | Out-Null
SyncRuntime $kokoroSource $kokoroOut
if ($Test) {
    $testOut = Join-Path $root 'build\tests'
    New-Item -ItemType Directory -Force -Path $testOut,(Join-Path $root 'build\qa') | Out-Null
    foreach ($binary in @('PointCursor.exe','PointCursor.exe.config','PointCursor.Core.dll','PointCursor.Reader.exe','PointCursor.Reader.exe.config')) { Copy-Item -LiteralPath (Join-Path $out $binary) -Destination $testOut -Force }
    Compile (@('/target:winexe','/define:POINTCURSOR_QA',('/out:' + (Join-Path $testOut 'PointCursor.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core)) + $desktop + $app)
    Compile (@('/target:winexe',('/out:' + (Join-Path $testOut 'PointCursor.Fixture.exe')),(Join-Path $root 'tests\Fixture.cs')) + $desktop)
    Compile (@('/target:exe',('/out:' + (Join-Path $testOut 'PointCursor.AudioProbe.exe')),('/r:' + $core),(Join-Path $root 'tests\AudioProbe.cs'),$native,(Join-Path $root 'src\PcmAudio.cs'),(Join-Path $root 'src\AudioOutput.cs')) + $desktop)
    $kokoroTestOut = Join-Path $testOut 'Kokoro'
    New-Item -ItemType Directory -Force -Path $kokoroTestOut | Out-Null
    SyncRuntime $kokoroOut $kokoroTestOut
    Compile (@('/target:exe',('/out:' + (Join-Path $testOut 'PointCursor.Tests.exe')),('/r:' + $core),(Join-Path $root 'tests\Tests.cs'),$native,(Join-Path $root 'src\SelectionClient.cs'),(Join-Path $root 'src\KokoroSpeechEngine.cs'),(Join-Path $root 'src\SpeechService.cs'),(Join-Path $root 'src\SettingsForm.cs')) + $desktop + $audio)
    & (Join-Path $testOut 'PointCursor.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $kokoroSmoke = Join-Path $root 'build\qa\kokoro-smoke.wav'
    & (Join-Path $testOut 'Kokoro\node.exe') (Join-Path $testOut 'Kokoro\worker.mjs') --smoke $kokoroSmoke
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $kokoroSmoke) -or (Get-Item -LiteralPath $kokoroSmoke).Length -le 44 -or [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($kokoroSmoke), 0, 4) -ne 'RIFF') { throw 'Kokoro smoke test failed.' }
}
if ($Package) {
    $stage = Join-Path $root 'build\package\PointCursor'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($name in @('PointCursor.exe','PointCursor.exe.config','PointCursor.Core.dll','PointCursor.Reader.exe','PointCursor.Reader.exe.config','README.md','COMPATIBILITY.md','diagnose.ps1')) { Copy-Item -LiteralPath (Join-Path $out $name) -Destination $stage -Force }
    New-Item -ItemType Directory -Force -Path (Join-Path $stage 'tools') | Out-Null
    Copy-Item -LiteralPath (Join-Path $out 'tools\check-runtime.ps1') -Destination (Join-Path $stage 'tools') -Force
    $kokoroStage = Join-Path $stage 'Kokoro'
    New-Item -ItemType Directory -Force -Path $kokoroStage | Out-Null
    SyncRuntime $kokoroOut $kokoroStage
    $legacyArchive = Join-Path $root 'dist\PointCursor-Windows-x64.zip'
    if (Test-Path -LiteralPath $legacyArchive) { Remove-Item -LiteralPath $legacyArchive -Force }
    $archive = Join-Path $root 'dist\PointCursor.zip'
    Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $archive -Force
    Write-Output ('Packaged: ' + $archive)
}
Write-Output ('Built: ' + (Join-Path $out 'PointCursor.exe'))
