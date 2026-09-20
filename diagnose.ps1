param([switch]$SkipSynthesis)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voice = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Name -eq 'Microsoft Zira Desktop' })
    if ($voice.Count -eq 0) { throw 'Microsoft Zira Desktop is not installed or enabled. Install this Windows voice, then restart PointCursor.' }
    Write-Output 'Voice: Microsoft Zira Desktop'
    if (-not $SkipSynthesis) {
        $memory = New-Object IO.MemoryStream
        try {
            $synth.SelectVoice('Microsoft Zira Desktop')
            $synth.SetOutputToWaveStream($memory)
            $synth.Speak('hello')
            if ($memory.Length -le 44) { throw 'Zira returned empty audio.' }
            Write-Output ('Zira synthesis bytes: ' + $memory.Length)
        } finally { $synth.SetOutputToNull(); $memory.Dispose() }
    }
} finally { $synth.Dispose() }
Write-Output 'Diagnostics complete. No audio playback, network access, settings change, or reading history.'
