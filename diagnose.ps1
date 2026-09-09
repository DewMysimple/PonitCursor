param([string]$Runtime = '', [switch]$SkipSynthesis)
$ErrorActionPreference = 'Stop'
if (-not $Runtime) {
    $Runtime = Join-Path $PSScriptRoot 'Kokoro'
    if (-not (Test-Path -LiteralPath $Runtime)) { $Runtime = Join-Path $PSScriptRoot 'third_party\kokoro-runtime' }
}
$Runtime = [IO.Path]::GetFullPath($Runtime)
& (Join-Path $PSScriptRoot 'tools\check-runtime.ps1') -Runtime $Runtime
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $names = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.TwoLetterISOLanguageName -eq 'en' } | ForEach-Object { $_.VoiceInfo.Name })
    Write-Output ('Windows English voices: ' + ($names -join ', '))
    if ($names.Count -gt 0) {
        $memory = New-Object IO.MemoryStream
        try { $synth.SelectVoice($names[0]); $synth.SetOutputToWaveStream($memory); $synth.Speak('hello'); Write-Output ('Windows synthesis bytes: ' + $memory.Length) }
        finally { $synth.SetOutputToNull(); $memory.Dispose() }
    }
} finally { $synth.Dispose() }
if (-not $SkipSynthesis) {
    $wave = Join-Path ([IO.Path]::GetTempPath()) ('PointCursor-diagnostic-' + [Guid]::NewGuid().ToString('N') + '.wav')
    $process = $null
    try {
        $start = New-Object Diagnostics.ProcessStartInfo
        $start.FileName = Join-Path $Runtime 'node.exe'
        $start.Arguments = '"' + (Join-Path $Runtime 'worker.mjs') + '" --smoke "' + $wave + '"'
        $start.WorkingDirectory = $Runtime; $start.UseShellExecute = $false; $start.CreateNoWindow = $true
        $start.RedirectStandardOutput = $true; $start.RedirectStandardError = $true
        $process = New-Object Diagnostics.Process; $process.StartInfo = $start
        $clock = [Diagnostics.Stopwatch]::StartNew(); [void]$process.Start()
        $stdout = $process.StandardOutput.ReadToEndAsync(); $stderr = $process.StandardError.ReadToEndAsync()
        if (-not $process.WaitForExit(15000)) { $process.Kill(); $process.WaitForExit(1000) | Out-Null; throw 'Kokoro synthesis timed out after 15 seconds.' }
        if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $wave)) { throw 'Kokoro could not start/synthesize. Check security software and the runtime directory.' }
        $bytes = [IO.File]::ReadAllBytes($wave)
        if ($bytes.Length -le 44 -or [BitConverter]::ToUInt16($bytes, 20) -ne 1 -or [BitConverter]::ToUInt16($bytes, 34) -ne 16) { throw 'Kokoro generated invalid PCM audio.' }
        Write-Output ('Kokoro cold startup + synthesis: ' + $clock.ElapsedMilliseconds + ' ms; PCM16 WAV bytes: ' + $bytes.Length)
    } finally {
        if ($null -ne $process) { $process.Dispose() }
        if (Test-Path -LiteralPath $wave) { Remove-Item -LiteralPath $wave -Force }
    }
}
Write-Output 'Diagnostics complete. No network download, settings change, or reading history was created.'
