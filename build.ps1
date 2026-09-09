param([switch]$Test, [switch]$Package)
$ErrorActionPreference = 'Stop'
$root = $PSScriptRoot
$framework = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler = Join-Path $framework 'csc.exe'
if (-not (Test-Path -LiteralPath $compiler)) { throw '.NET Framework 4.x compiler is required.' }
$speech = Get-ChildItem -LiteralPath (Join-Path $env:WINDIR 'Microsoft.NET\assembly\GAC_MSIL\System.Speech') -Recurse -Filter System.Speech.dll | Select-Object -First 1 -ExpandProperty FullName
if (-not $speech) { throw 'System.Speech is required.' }
$out = Join-Path $root 'dist\PointCursor'
New-Item -ItemType Directory -Force -Path $out | Out-Null
$kokoroSource = Join-Path $root 'third_party\kokoro-runtime'
$kokoroRequired = @(
    'node.exe',
    'worker.mjs',
    'lib\kokoro.js',
    'model\config.json',
    'model\tokenizer.json',
    'model\onnx\model_quantized.onnx',
    'voices\af_heart.bin',
    'node_modules\@huggingface\transformers\package.json',
    'node_modules\@huggingface\transformers\dist\transformers.node.mjs',
    'node_modules\onnxruntime-common\dist\esm\index.js',
    'node_modules\onnxruntime-node\dist\index.js',
    'node_modules\onnxruntime-node\bin\napi-v3\win32\x64\onnxruntime_binding.node',
    'node_modules\onnxruntime-node\bin\napi-v3\win32\x64\onnxruntime.dll',
    'node_modules\phonemizer\dist\phonemizer.js'
)
foreach ($relative in $kokoroRequired) { if (-not (Test-Path -LiteralPath (Join-Path $kokoroSource $relative))) { throw ('Kokoro runtime file is required: ' + $relative) } }
$common = @('/nologo','/optimize+','/platform:x64','/warn:4','/utf8output',('/r:' + (Join-Path $framework 'System.dll')),('/r:' + (Join-Path $framework 'System.Core.dll')))
function Compile([string[]]$CompilerArgs) {
    & $compiler @common @CompilerArgs
    if ($LASTEXITCODE -ne 0) { throw 'C# compilation failed.' }
}
$core = Join-Path $out 'PointCursor.Core.dll'
Compile @('/target:library', ('/out:' + $core), (Join-Path $root 'src\Core.cs'))
$native = Join-Path $root 'src\Native.cs'
$uia = @(('/r:' + (Join-Path $framework 'WPF\UIAutomationClient.dll')), ('/r:' + (Join-Path $framework 'WPF\UIAutomationTypes.dll')), ('/r:' + (Join-Path $framework 'WPF\WindowsBase.dll')))
Compile (@('/target:exe',('/out:' + (Join-Path $out 'PointCursor.Reader.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core),('/r:' + (Join-Path $framework 'Accessibility.dll')),$native,(Join-Path $root 'src\SelectionReader.cs'),(Join-Path $root 'src\LegacySelection.cs')) + $uia)
$desktop = @(('/r:' + (Join-Path $framework 'System.Windows.Forms.dll')), ('/r:' + (Join-Path $framework 'System.Drawing.dll')), ('/r:' + $speech))
$app = @('Native.cs','SelectionClient.cs','InputMonitor.cs','KokoroSpeechEngine.cs','SpeechService.cs','SettingsForm.cs','App.cs') | ForEach-Object { Join-Path $root ('src\' + $_) }
Compile (@('/target:winexe',('/out:' + (Join-Path $out 'PointCursor.exe')),('/win32manifest:' + (Join-Path $root 'app.manifest')),('/r:' + $core)) + $desktop + $app)
Copy-Item -LiteralPath (Join-Path $root 'PointCursor.exe.config') -Destination $out -Force
Copy-Item -LiteralPath (Join-Path $root 'PointCursor.exe.config') -Destination (Join-Path $out 'PointCursor.Reader.exe.config') -Force
foreach ($doc in @('README.md','COMPATIBILITY.md')) { if (Test-Path -LiteralPath (Join-Path $root $doc)) { Copy-Item -LiteralPath (Join-Path $root $doc) -Destination $out -Force } }
$kokoroOut = Join-Path $out 'Kokoro'
New-Item -ItemType Directory -Force -Path $kokoroOut | Out-Null
Copy-Item -Path (Join-Path $kokoroSource '*') -Destination $kokoroOut -Recurse -Force
if ($Test) {
    $testOut = Join-Path $root 'build\tests'
    New-Item -ItemType Directory -Force -Path $testOut,(Join-Path $root 'build\qa') | Out-Null
    foreach ($binary in @('PointCursor.exe','PointCursor.exe.config','PointCursor.Core.dll','PointCursor.Reader.exe','PointCursor.Reader.exe.config')) { Copy-Item -LiteralPath (Join-Path $out $binary) -Destination $testOut -Force }
    Compile (@('/target:winexe',('/out:' + (Join-Path $testOut 'PointCursor.Fixture.exe')),(Join-Path $root 'tests\Fixture.cs')) + $desktop)
    $kokoroTestOut = Join-Path $testOut 'Kokoro'
    New-Item -ItemType Directory -Force -Path $kokoroTestOut | Out-Null
    Copy-Item -Path (Join-Path $kokoroOut '*') -Destination $kokoroTestOut -Recurse -Force
    Compile (@('/target:exe',('/out:' + (Join-Path $testOut 'PointCursor.Tests.exe')),('/r:' + $core),(Join-Path $root 'tests\Tests.cs'),$native,(Join-Path $root 'src\SelectionClient.cs'),(Join-Path $root 'src\KokoroSpeechEngine.cs'),(Join-Path $root 'src\SpeechService.cs'),(Join-Path $root 'src\SettingsForm.cs')) + $desktop)
    & (Join-Path $testOut 'PointCursor.Tests.exe')
    if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
    $kokoroSmoke = Join-Path $root 'build\qa\kokoro-smoke.wav'
    & (Join-Path $testOut 'Kokoro\node.exe') (Join-Path $testOut 'Kokoro\worker.mjs') --smoke $kokoroSmoke
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $kokoroSmoke) -or (Get-Item -LiteralPath $kokoroSmoke).Length -le 44 -or [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($kokoroSmoke), 0, 4) -ne 'RIFF') { throw 'Kokoro smoke test failed.' }
}
if ($Package) {
    $stage = Join-Path $root 'build\package\PointCursor'
    New-Item -ItemType Directory -Force -Path $stage | Out-Null
    foreach ($name in @('PointCursor.exe','PointCursor.exe.config','PointCursor.Core.dll','PointCursor.Reader.exe','PointCursor.Reader.exe.config','README.md','COMPATIBILITY.md')) { Copy-Item -LiteralPath (Join-Path $out $name) -Destination $stage -Force }
    $kokoroStage = Join-Path $stage 'Kokoro'
    New-Item -ItemType Directory -Force -Path $kokoroStage | Out-Null
    Copy-Item -Path (Join-Path $kokoroOut '*') -Destination $kokoroStage -Recurse -Force
    $archive = Join-Path $root 'dist\PointCursor-Windows-x64.zip'
    Compress-Archive -LiteralPath $stage -DestinationPath $archive -Force
    Write-Output ('Packaged: ' + $archive)
}
Write-Output ('Built: ' + (Join-Path $out 'PointCursor.exe'))
