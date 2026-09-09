using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;

namespace PointCursor
{
    internal sealed class SpeechService : IDisposable
    {
        private SpeechSynthesizer synth;
        private readonly KokoroSpeechEngine kokoro;
        private readonly Dictionary<string, string> kokoroVoices = new Dictionary<string, string>(StringComparer.Ordinal);
        public event Action<string> Failed;
        public event Action<string> Started;
        public readonly List<string> Voices = new List<string>();
        public string Error { get; private set; }
        public bool Available { get { return Voices.Count > 0; } }
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
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException) && !(ex is System.Runtime.InteropServices.COMException) && !(ex is PlatformNotSupportedException)) throw;
                if (synth != null) { synth.Dispose(); synth = null; }
            }
            kokoro = new KokoroSpeechEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kokoro"));
            kokoro.Failed += delegate(string message) { Error = message; if (Failed != null) Failed(message); };
            kokoro.Started += delegate(string word) { if (Started != null) Started(word); };
            AddKokoroVoices();
            if (Voices.Count == 0) Error = "未找到可用英文语音，请检查 Kokoro 文件或在 Windows 设置中添加英语语音包。";
        }
        private void AddKokoroVoices()
        {
            foreach (string id in kokoro.GetVoiceIds())
            {
                string accent = id[0] == 'a' ? "美式" : "英式";
                string gender = id[1] == 'f' ? "女声" : "男声";
                string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Substring(3).Replace('_', ' '));
                string display = "Kokoro · " + name + "（" + accent + gender + " · " + id + "）";
                kokoroVoices.Add(display, id);
                Voices.Add(display);
            }
        }
        public bool IsKokoroVoice(string voice) { return voice != null && kokoroVoices.ContainsKey(voice); }
        public bool Speak(string word, AppSettings settings)
        {
            if (!Available) return false;
            try
            {
                string voice = Voices.Contains(settings.Voice) ? settings.Voice : Voices.FirstOrDefault(v => v.IndexOf("Zira", StringComparison.OrdinalIgnoreCase) >= 0) ?? Voices[0];
                string kokoroId;
                if (kokoroVoices.TryGetValue(voice, out kokoroId))
                {
                    if (synth != null) synth.SpeakAsyncCancelAll();
                    kokoro.Speak(word, kokoroId, settings.Rate, settings.Volume);
                    Error = null; return true;
                }
                if (synth == null) { Error = "Windows 系统语音不可用，请选择 Kokoro 语音。"; return false; }
                kokoro.Stop();
                synth.SpeakAsyncCancelAll();
                synth.SelectVoice(voice); synth.Rate = settings.Rate; synth.Volume = settings.Volume;
                synth.SpeakAsync(word); Error = null; return true;
            }
            catch (Exception ex)
            {
                if (!(ex is InvalidOperationException) && !(ex is ArgumentException) && !(ex is System.Runtime.InteropServices.COMException)) throw;
                Error = "播放失败，请检查语音和音频输出设备。"; return false;
            }
        }
        public void Stop()
        {
            kokoro.Stop();
            if (synth != null) try { synth.SpeakAsyncCancelAll(); } catch (InvalidOperationException) { }
        }
        public void Dispose()
        {
            Stop(); kokoro.Dispose();
            if (synth != null) { synth.Dispose(); synth = null; }
        }
    }
}
