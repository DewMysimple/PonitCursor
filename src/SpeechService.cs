using System;
using System.Collections.Generic;
using System.Linq;
using System.Speech.Synthesis;

namespace PointCursor
{
    internal sealed class SpeechService : IDisposable
    {
        private SpeechSynthesizer synth;
        public event Action<string> Failed;
        public readonly List<string> Voices = new List<string>();
        public string Error { get; private set; }
        public bool Available { get { return synth != null && Voices.Count > 0; } }
        public SpeechService()
        {
            try
            {
                synth = new SpeechSynthesizer();
                synth.SpeakCompleted += delegate(object sender, SpeakCompletedEventArgs e) {
                    if (!e.Cancelled && e.Error != null) {
                        Error = "播放失败，请检查语音和音频输出设备。";
                        if (Failed != null) Failed(Error);
                    }
                };
                foreach (InstalledVoice voice in synth.GetInstalledVoices())
                    if (voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en") Voices.Add(voice.VoiceInfo.Name);
                synth.SetOutputToDefaultAudioDevice();
                if (Voices.Count == 0) Error = "未找到英文语音，请在 Windows 设置中添加英语语音包后重启程序。";
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException) && !(ex is System.Runtime.InteropServices.COMException) && !(ex is PlatformNotSupportedException)) throw;
                Error = "语音设备不可用，请检查 Windows 的音频输出后重启程序。";
                if (synth != null) { synth.Dispose(); synth = null; }
            }
        }
        public bool Speak(string word, AppSettings settings)
        {
            if (!Available) return false;
            try
            {
                synth.SpeakAsyncCancelAll();
                string voice = Voices.Contains(settings.Voice) ? settings.Voice : Voices.FirstOrDefault(v => v.IndexOf("Zira", StringComparison.OrdinalIgnoreCase) >= 0) ?? Voices[0];
                synth.SelectVoice(voice); synth.Rate = settings.Rate; synth.Volume = settings.Volume;
                synth.SpeakAsync(word); Error = null; return true;
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException) && !(ex is ArgumentException) && !(ex is System.Runtime.InteropServices.COMException)) throw;
                Error = "播放失败，请检查语音和音频输出设备。"; return false;
            }
        }
        public void Stop() { if (synth != null) try { synth.SpeakAsyncCancelAll(); } catch (InvalidOperationException) { } }
        public void Dispose() { if (synth != null) { Stop(); synth.Dispose(); synth = null; } }
    }
}
