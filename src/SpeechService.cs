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
        private readonly AudioOutput output = new AudioOutput();
        private readonly SapiSpeechEngine sapi;
        private readonly KokoroSpeechEngine kokoro;
        private readonly Dictionary<string, string> kokoroVoices = new Dictionary<string, string>(StringComparer.Ordinal);
        public event Action<string> Failed;
        public event Action<long, string> Started;
        public long Generation { get { return output.Generation; } }
        public readonly List<string> Voices = new List<string>();
        public string Error { get; private set; }
        public bool Available { get { return Voices.Count > 0; } }
        public SpeechService()
        {
            try
            {
                using (var synth = new SpeechSynthesizer())
                    foreach (InstalledVoice voice in synth.GetInstalledVoices())
                        if (voice.Enabled && voice.VoiceInfo.Culture.TwoLetterISOLanguageName == "en") Voices.Add(voice.VoiceInfo.Name);
            }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (PlatformNotSupportedException) { }
            sapi = new SapiSpeechEngine(output);
            kokoro = new KokoroSpeechEngine(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kokoro"), output);
            sapi.Failed += Report; kokoro.Failed += Report; output.Failed += Report;
            output.Started += delegate(long ticket, string word) { if (ticket == Generation && Started != null) Started(ticket, word); };
            foreach (string id in kokoro.GetVoiceIds())
            {
                string accent = id[0] == 'a' ? "美式" : "英式";
                string gender = id[1] == 'f' ? "女声" : "男声";
                string name = CultureInfo.InvariantCulture.TextInfo.ToTitleCase(id.Substring(3).Replace('_', ' '));
                string display = "Kokoro · " + name + "（" + accent + gender + " · " + id + "）";
                kokoroVoices.Add(display, id); Voices.Add(display);
            }
            if (!kokoro.Available) Error = kokoro.RuntimeError;
            if (!Available) Error = "未找到可用英文语音，请检查 Kokoro 文件或在 Windows 设置中添加英语语音包。";
        }
        private void Report(string message) { Error = message; if (Failed != null) Failed(message); }
        public bool IsKokoroVoice(string voice) { return voice != null && kokoroVoices.ContainsKey(voice); }
        private string Resolve(AppSettings settings)
        {
            if (Voices.Contains(settings.Voice)) return settings.Voice;
            return Voices.FirstOrDefault(v => v.IndexOf("Zira", StringComparison.OrdinalIgnoreCase) >= 0) ?? Voices.FirstOrDefault();
        }
        public void Prepare(AppSettings settings)
        {
            string voice = Resolve(settings), id;
            if (voice == null) return;
            output.Prepare();
            if (kokoroVoices.TryGetValue(voice, out id)) kokoro.Prepare(id);
            else sapi.Speak(Generation, "hello", voice, settings.Rate, 100, true);
        }
        public bool Speak(string word, AppSettings settings)
        {
            if (!Available) return false;
            Stop();
            try
            {
                string voice = Resolve(settings), id;
                if (kokoroVoices.TryGetValue(voice, out id)) kokoro.Speak(Generation, word, id, settings.Rate, settings.Volume);
                else sapi.Speak(Generation, word, voice, settings.Rate, settings.Volume, false);
                Error = null; return true;
            }
            catch (InvalidOperationException) { Error = "语音不可用，请检查运行时或切换语音。"; return false; }
        }
        public void Stop() { output.Stop(); kokoro.Stop(); sapi.Stop(); }
        public void Suspend() { Stop(); output.Suspend(); }
        public void Dispose() { Stop(); kokoro.Dispose(); sapi.Dispose(); output.Dispose(); }
    }
}
