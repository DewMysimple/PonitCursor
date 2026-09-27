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
$common = @('/nologo','/optimize+','/platform:x64','/warn:4','/utf8output',('/r:' + (Join-Path $framework 'System.dll')),('/r:' + (Join-Path $framework 'System.Core.dll')))
function Compile([string[]]$CompilerArgs) {
    & $compiler @common @CompilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
}
function RemoveLegacyRuntime([string]$Directory) {
    # Remove only known obsolete generated assets, never arbitrary output contents.
    $boundary = [IO.Path]::GetFullPath($Directory).TrimEnd('\') + '\'
    if (-not ($boundary.StartsWith((Join-Path $root 'build\'), [StringComparison]::OrdinalIgnoreCase) -or $boundary.StartsWith((Join-Path $root 'dist\'), [StringComparison]::OrdinalIgnoreCase))) { throw 'Unsafe output path.' }
    foreach ($name in @('Kokoro', 'tools\check-runtime.ps1')) {
        $target = [IO.Path]::GetFullPath((Join-Path $Directory $name))
        if (-not $target.StartsWith($boundary, [StringComparison]::OrdinalIgnoreCase)) { throw 'Unsafe obsolete asset path.' }
        if (Test-Path -LiteralPath $target) { Remove-Item -LiteralPath $target -Recurse -Force }
    }
}
RemoveLegacyRuntime $out
$core = Join-Path $out 'PointCursor.Core.dll'
Compile @('/target:library', ('/out:' + $core), (Join-Path $root 'src\Core.cs'), (Join-Path $root 'src\SelectionGesture.cs'))
$native = Join-Path $root 'src\Native.cs'
$uia = @(('/r:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')), ('/r:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')), ('/r:' + (Join-Path $framework 'WPF\WindowsBase.dll')))
Compile (@('/target:exe',('/out:' + (Join-Path $out 'PointCursor.Reader.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core),('/r:' + (Join-Path $framework 'Accessibility.dll')),$native,(Join-Path $root 'src\SelectionReader.cs'),(Join-Path $root 'src\LegacySelection.cs')) + $uia)
$desktop = @(('/r:' + (Join-Path $framework 'System.Windows.Forms.dll')), ('/r:' + (Join-Path $framework 'System.Drawing.dll')), ('/r:' + $speech))
$audio = @('PcmAudio.cs','AudioOutput.cs','SapiSpeechEngine.cs','VoiceCatalog.cs') | ForEach-Object { Join-Path $root ('src\' + $_) }
$app = @('Native.cs','SelectionClient.cs','InputMonitor.cs','SpeechService.cs','SettingsForm.cs','App.cs') | ForEach-Object { Join-Path $root ('src\' + $_) }
$app += $audio
Compile (@('/target:winexe',('/out:' + (Join-Path $out 'PointCursor.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core)) + $desktop + $app)
Copy-Item -LiteralPath (Join-Path $root 'PointCursor.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'PointCursor.exe.config') -Destination (Join-Path $out 'PointCursor.Reader.exe.config') -Force
foreach ($doc in @('README.md','COMPATIBILITY.md')) { if (Test-Path -LiteralPath (Join-Path $root $doc)) { Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $out -Force } }
Copy-Item -LiteralPath (Join-Path $root 'diagnose.ps1') -Destination $out -Force
if ($Test) {
    $testOut = Join-Path $root 'build\tests'
    New-Item -ItemType Directory -Force -Path $testOut,(Join-Path $root 'build\qa') | Out-Null
    foreach ($binary in @('PointCursor.exe','PointCursor.exe.config','PointCursor.Core.dll','PointCursor.Reader.exe','PointCursor.Reader.exe.config')) { Copy-Item -LiteralPath (Join-Path $out $binary) -Destination $testOut -Force }
    Compile (@('/target:winexe','/define:POINTCURSOR_QA',('/out:' + (Join-Path $testOut 'PointCursor.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core)) + $desktop + $app)
    Compile (@('/target:winexe',('/out:' + (Join-Path $testOut 'PointCursor.Fixture.exe')),(Join-Path $root 'tests\Fixture.cs')) + $desktop)
    Compile (@('/target:exe','/define:POINTCURSOR_QA',('/out:' + (Join-Path $testOut 'PointCursor.AudioProbe.exe')),('/r:' + $core),(Join-Path $root 'tests\AudioProbe.cs'),$native,(Join-Path $root 'src\PcmAudio.cs'),(Join-Path $root 'src\AudioOutput.cs')) + $desktop)
    RemoveLegacyRuntime $testOut
    Compile (@('/target:exe',('/out:' + (Join-Path $testOut 'PointCursor.Tests.exe')),('/r:' + $core),(Join-Path $root 'tests\Tests.cs'),$native,(Join-Path $root 'src\SelectionClient.cs'),(Join-Path $root 'src\InputMonitor.cs'),(Join-Path $root 'src\SpeechService.cs'),(Join-Path $root 'src\SettingsForm.cs')) + $desktop + $audio)
    & (Join-Path $testOut 'PointCursor.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
}
if ($Package) {
    RemoveLegacyRuntime (Join-Path $root 'build\package\PointCursor')
    # An explicit list prevents stale generated files from re-entering the ZIP.
    $packageFiles = @('PointCursor.exe','PointCursor.exe.config','PointCursor.Core.dll','PointCursor.Reader.exe','PointCursor.Reader.exe.config','README.md','COMPATIBILITY.md','diagnose.ps1') | ForEach-Object { Join-Path $out $_ }
    $legacyArchive = Join-Path $root 'dist\PointCursor-Windows-x64.zip'
    if (Test-Path -LiteralPath $legacyArchive) { Remove-Item -LiteralPath $legacyArchive -Force }
    $archive = Join-Path $root 'dist\PointCursor.zip'
    Compress-Archive -LiteralPath $packageFiles -DestinationPath $archive -Force
    Write-Output ('Packaged: ' + $archive)
}
Write-Output ('Built: ' + (Join-Path $out 'PointCursor.exe'))
