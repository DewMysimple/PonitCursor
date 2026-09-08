using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Speech.Synthesis;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PointCursor
{
    internal static class Tests
    {
        private static int passed;
        private static void Check(bool value, string label)
        { if (!value) throw new Exception("FAIL: " + label); passed++; Console.WriteLine("PASS: " + label); }
        [STAThread]
        public static int Main(string[] args)
        {
            try
            {
                if (args.Length > 0 && args[0] == "--hang") { Console.ReadLine(); Thread.Sleep(10000); return 0; }
                Rules(); Gates(); Settings(); Audio(); Worker().GetAwaiter().GetResult();
                if (args.Length > 0 && args[0] == "--render") Render(args[1]);
                Console.WriteLine("TOTAL: " + passed + " passed"); return 0;
            }
            catch (Exception ex) { Console.Error.WriteLine(ex.ToString()); return 1; }
        }
        private static void Rules()
        {
            foreach (string word in new[] { "hello", "A", "I", "don't", "well-known", "mother-in-law", "English" }) Check(WordRules.Normalize(word) == word, "word " + word);
            Check(WordRules.Normalize(" \r\n\u201cHello!\u201d \t") == "Hello", "surrounding punctuation");
            Check(WordRules.Normalize("don\u2019t") == "don't", "curly apostrophe");
            foreach (string input in new[] { null, "", " ", "hello world", "hello\nworld", "123", "abc123", "中文", "hello中文", "example.com", "https://example.com", "a@b.com", "file/path", "foo_bar", "a+b", "a--b", "a''b", "-word-", "🐈", new string('a', 65), new string(' ', 129) + "hello" }) Check(WordRules.Normalize(input) == null, "reject " + (input == null ? "null" : input.Length > 30 ? "overlength" : input));
        }
        private static void Gates()
        {
            var gate = new SelectionGate();
            long old = gate.Current; long current = gate.Invalidate();
            Check(!gate.TryAccept(old), "reject stale selection");
            Check(gate.TryAccept(current), "accept current selection");
            Check(!gate.TryAccept(current), "suppress auto/copy duplicate");
            Check(gate.TryAccept(gate.Invalidate()), "same word may replay on new gesture");
            var copy = new CopyGate(); IntPtr a = new IntPtr(1), b = new IntPtr(2);
            Check(!copy.TryConsume(a, 2, 0), "ignore unsolicited clipboard change");
            copy.Arm(a, 4, 100);
            Check(!copy.TryConsume(a, 4, 110), "ignore unchanged clipboard");
            Check(copy.TryConsume(a, 5, 120), "accept explicit new copy");
            Check(!copy.TryConsume(a, 6, 130), "consume copy once");
            copy.Arm(a, 6, 100); Check(!copy.TryConsume(b, 7, 120), "reject other foreground");
            Check(!copy.TryConsume(a, 7, 130), "focus change disarms copy");
            copy.Arm(a, 6, 100); Check(!copy.TryConsume(a, 7, 1301), "expire copy intent");
            copy.Arm(a, UInt32.MaxValue, 100); Check(copy.TryConsume(a, 0, 110), "clipboard counter wrap");
            copy.Arm(a, 1, 100); copy.Reset(); Check(!copy.TryConsume(a, 2, 110), "pause disarms copy");
        }
        private static void Settings()
        {
            string folder = Path.Combine(Path.GetTempPath(), "PointCursor-tests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(folder);
            string path = Path.Combine(folder, "settings.xml");
            try
            {
                bool reset;
                var defaults = AppSettings.Load(path, out reset);
                Check(!reset && defaults.CopyToSpeak && defaults.Volume == 85 && defaults.Rate == -1, "first-run defaults");
                defaults.Rate = 100; defaults.Volume = -20; defaults.CopyToSpeak = false;
                Check(defaults.Save(path), "save settings");
                var loaded = AppSettings.Load(path, out reset);
                Check(!reset && loaded.Rate == 5 && loaded.Volume == 0 && !loaded.CopyToSpeak, "round trip and bounds");
                loaded.Rate = 0; Check(loaded.Save(path), "atomic replace existing settings");
                File.WriteAllText(path, "broken xml");
                AppSettings.Load(path, out reset); Check(reset, "recover corrupt settings");
                File.WriteAllText(path, "<!DOCTYPE x [<!ENTITY test SYSTEM 'file:///nonexistent'>]><AppSettings><Voice>&test;</Voice></AppSettings>");
                AppSettings.Load(path, out reset); Check(reset, "reject XML entities");
            }
            finally { if (File.Exists(path)) File.Delete(path); Directory.Delete(folder); }
        }
        private static void Audio()
        {
            using (var synth = new SpeechSynthesizer())
            using (var wave = new MemoryStream())
            {
                var voices = synth.GetInstalledVoices().Where(v => v.Enabled && v.VoiceInfo.Culture.TwoLetterISOLanguageName == "en").ToArray();
                Check(voices.Length > 0, "local English voice installed");
                synth.SelectVoice(voices[0].VoiceInfo.Name); synth.SetOutputToWaveStream(wave); synth.Speak("hello");
                Check(wave.Length > 1000 && System.Text.Encoding.ASCII.GetString(wave.ToArray(), 0, 4) == "RIFF", "local voice generates WAV without web service");
            }
            using (var service = new SpeechService())
            {
                service.Voices.Clear(); Check(!service.Available && !service.Speak("hello", new AppSettings()), "missing English voice handled without playback");
            }
        }
        private static async Task Worker()
        {
            using (var client = new SelectionClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "PointCursor.Reader.exe")))
            {
                SelectionResult result = await client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None);
                Check(result.Status == "stale" && result.Word == null, "worker rejects invalid foreground");
                using (var cancel = new CancellationTokenSource())
                {
                    cancel.Cancel(); bool cancelled = false;
                    try { await client.QueryAsync(IntPtr.Zero, 0, 0, false, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                    Check(cancelled, "cancelled request rejected");
                }
                result = await client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None);
                Check(result.Status == "stale", "worker usable after cancellation");
            }
            using (var client = new SelectionClient(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "missing-reader.exe")))
            { Check((await client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None)).Status == "unavailable", "missing helper handled"); }
            using (var client = new SelectionClient(System.Reflection.Assembly.GetExecutingAssembly().Location, "--hang"))
            {
                var clock = Stopwatch.StartNew();
                Check((await client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None)).Status == "timeout", "hung provider times out");
                Check(clock.ElapsedMilliseconds < 1800, "hung provider bounded wall time");
                using (var cancel = new CancellationTokenSource(120))
                {
                    clock.Restart(); bool cancelled = false;
                    try { await client.QueryAsync(IntPtr.Zero, 0, 0, false, cancel.Token); } catch (OperationCanceledException) { cancelled = true; }
                    Check(cancelled && clock.ElapsedMilliseconds < 1000, "in-flight request cancelled promptly");
                }
            }
        }
        private static void Render(string path)
        {
            Application.EnableVisualStyles();
            using (var form = new SettingsForm(new AppSettings(), new List<string> { "Microsoft Zira Desktop" }))
            {
                form.UpdateStatus("准备好了，选中一个英文单词试试。", false);
                form.StartPosition = FormStartPosition.Manual; form.Location = new Point(-10000, -10000);
                form.Show(); Application.DoEvents();
                using (var bitmap = new Bitmap(form.Width, form.Height)) { form.DrawToBitmap(bitmap, new Rectangle(Point.Empty, bitmap.Size)); bitmap.Save(path); }
                form.AllowClose = true; form.Close();
            }
            Check(File.Exists(path), "settings window rendered");
        }
    }
}
