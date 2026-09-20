using System;
using System.Speech.Synthesis;

namespace PointCursor
{
    internal sealed class SpeechService : IDisposable
    {
        public const string VoiceName = "Microsoft Zira Desktop";
        private readonly AudioOutput output = new AudioOutput();
        private readonly SapiSpeechEngine engine;
        public event Action<string> Failed;
        public event Action<long, string> Started;
        public long Generation { get { return output.Generation; } }
        public string Error { get; private set; }
        public bool Available { get; private set; }

        public SpeechService()
        {
            try
            {
                using (var synth = new SpeechSynthesizer())
                    foreach (InstalledVoice voice in synth.GetInstalledVoices())
                        if (voice.Enabled && voice.VoiceInfo.Name == VoiceName) Available = true;
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
            if (!Available) Error = "未找到 Microsoft Zira Desktop，请在 Windows 中安装此语音后重启程序。";
        }

        private void Report(string message) { Error = message; if (Failed != null) Failed(message); }
        public void Prepare(AppSettings settings)
        {
            if (!Available) return;
            output.Prepare();
            engine.Speak(Generation, "hello", settings.Rate, 100, true);
        }
        public bool Speak(string word, AppSettings settings)
        {
            if (!Available) return false;
            Stop();
            Error = null;
            engine.Speak(Generation, word, settings.Rate, settings.Volume, false);
            return true;
        }
        public void Stop() { output.Stop(); engine.Stop(); }
        public void Suspend() { Stop(); output.Suspend(); }
        public void Dispose() { Stop(); engine.Dispose(); output.Dispose(); }
    }
}
