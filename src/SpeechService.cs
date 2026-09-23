using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Speech.Synthesis;

namespace PointCursor
{
    internal sealed class SpeechService : IDisposable
    {
        private readonly VoiceCatalog catalog;
        public ReadOnlyCollection<SpeechVoice> Voices { get { return catalog.Voices; } }
        private readonly AudioOutput output = new AudioOutput();
        private readonly SapiSpeechEngine engine;
        public event Action<string> Failed;
        public event Action<long, string> Started;
        public long Generation { get { return output.Generation; } }
        public string Error { get; private set; }
        public bool Available { get { return Voices.Count != 0; } }

        public SpeechService(AppSettings settings)
        {
            catalog = new VoiceCatalog(new SpeechVoice[0]);
            try
            {
                using (var synth = new SpeechSynthesizer())
                    catalog = new VoiceCatalog(synth.GetInstalledVoices().Select(v =>
                        new SpeechVoice(v.VoiceInfo.Name, v.VoiceInfo.Culture.Name, v.Enabled)));
            }
            catch (InvalidOperationException) { }
            catch (System.Runtime.InteropServices.COMException) { }
            catch (PlatformNotSupportedException) { }
            engine = new SapiSpeechEngine(output);
            engine.Failed += Report;
            output.Failed += Report;
            output.Started += delegate(long ticket, string word) {
                if (ticket == Generation && Started != null) Started(ticket, word);
            };
            SpeechVoice selected = catalog.Resolve(settings.Voice);
            if (selected == null) Error = "未找到美式或英式系统语音，请安装兼容的语音后重启程序。";
            else if (settings.Voice != selected.Name)
            {
                settings.Voice = selected.Name;
                Error = "原语音不可用，已改用：" + selected;
            }
        }

        private void Report(string message) { Error = message; if (Failed != null) Failed(message); }
        public void Prepare(AppSettings settings)
        {
            Stop();
            if (!Available || catalog.Find(settings.Voice) == null) return;
            output.Prepare();
            engine.Speak(Generation, "hello", settings.Voice, settings.Rate, 100, true);
        }
        public bool Speak(string word, AppSettings settings)
        {
            Stop();
            if (catalog.Find(settings.Voice) == null)
            { Report("所选美式或英式语音不可用，请在设置中重新选择。"); return false; }
            Error = null;
            engine.Speak(Generation, word, settings.Voice, settings.Rate, settings.Volume, false);
            return true;
        }
        public void Stop() { output.Stop(); engine.Stop(); }
        public void Suspend() { Stop(); output.Suspend(); }
        public void Dispose() { Stop(); engine.Dispose(); output.Dispose(); }
    }
}
