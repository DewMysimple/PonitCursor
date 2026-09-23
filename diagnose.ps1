param([switch]$SkipSynthesis, [switch]$RequireBothAccents)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Speech
$synth = New-Object System.Speech.Synthesis.SpeechSynthesizer
try {
    $voices = @($synth.GetInstalledVoices() | Where-Object { $_.Enabled -and $_.VoiceInfo.Culture.Name -in @('en-US', 'en-GB') })
    if ($voices.Count -eq 0) { throw 'No compatible US or British English voice is installed. Install a SAPI 5 voice, then restart PointCursor.' }
    foreach ($voice in $voices) {
        Write-Output ('Voice: ' + $voice.VoiceInfo.Name + ' [' + $voice.VoiceInfo.Culture.Name + ']')
        if (-not $SkipSynthesis) {
            $memory = New-Object IO.MemoryStream
            try {
                $synth.SelectVoice($voice.VoiceInfo.Name)
                if ($synth.Voice.Name -ne $voice.VoiceInfo.Name) { throw 'Voice selection did not match the requested name.' }
                $synth.SetOutputToWaveStream($memory)
                $synth.Speak('hello')
                if ($memory.Length -le 44) { throw 'The voice returned empty audio.' }
                Write-Output ('Synthesis bytes: ' + $memory.Length)
            } finally { $synth.SetOutputToNull(); $memory.Dispose() }
        }
    }
    if ($RequireBothAccents -and (@($voices | Where-Object { $_.VoiceInfo.Culture.Name -eq 'en-US' }).Count -eq 0 -or @($voices | Where-Object { $_.VoiceInfo.Culture.Name -eq 'en-GB' }).Count -eq 0)) { throw 'Both en-US and en-GB voices are required for this verification.' }
} finally { $synth.Dispose() }
Write-Output 'Diagnostics complete. No audio playback, network access, settings change, or reading history.'
