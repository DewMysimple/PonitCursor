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
                Rules(); Gates(); Settings(); Pcm(); RuntimeChecks(); Audio(); KokoroBridge(); KokoroFailures(); SapiBridge(); Worker().GetAwaiter().GetResult();
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
                string[] kokoro = service.Voices.Where(v => v.StartsWith("Kokoro · ", StringComparison.Ordinal)).ToArray();
                Check(kokoro.Length == 28, "Kokoro English voices discovered");
                Check(kokoro.Any(v => v.Contains("af_heart")) && kokoro.Any(v => v.Contains("bm_george")), "Kokoro American and British voices listed");
                Check(Math.Abs(KokoroSpeechEngine.RateToSpeed(-5) - 0.6) < 0.001 && Math.Abs(KokoroSpeechEngine.RateToSpeed(5) - 1.4) < 0.001, "Kokoro rate mapping bounded");
                service.Voices.Clear(); Check(!service.Available && !service.Speak("hello", new AppSettings()), "missing English voice handled without playback");
            }
        }
        private static void KokoroBridge()
        {
            string runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kokoro");
            using (var engine = new KokoroSpeechEngine(runtime))
            using (var started = new ManualResetEvent(false))
            {
                string failure = null;
                engine.Started += delegate { started.Set(); };
                engine.Failed += delegate(string message) { failure = message; started.Set(); };
                var clock = Stopwatch.StartNew(); engine.Prepare("bf_emma");
                Check(SpinWait.SpinUntil(() => engine.IsReady || failure != null, 15000) && failure == null, "Kokoro preloads and warms silently");
                Console.WriteLine("Kokoro warm-up ms: " + clock.ElapsedMilliseconds);
                int workerId = engine.WorkerId;
                for (int i = 0; i < 15; i++) { engine.Stop(); engine.Prepare("bf_emma"); }
                clock.Restart();
                engine.Speak("hello", "af_heart", -1, 85);
                Check(started.WaitOne(15000) && failure == null, "Kokoro C# bridge generates and starts playback");
                Console.WriteLine("Kokoro warm hello ms: " + clock.ElapsedMilliseconds);
                Check(engine.WorkerId == workerId, "mouse resets preserve preloaded model");
                started.Reset(); failure = null;
                engine.Speak("English", "bf_emma", 0, 85);
                Check(started.WaitOne(15000) && failure == null, "Kokoro worker reused for British voice");
                engine.Stop();
                string spoken = null; engine.Started += delegate(string word) { spoken = word; };
                for (int i = 0; i < 10; i++) { engine.Speak("information", "bf_emma", 0, 85); Thread.Sleep(35); engine.Stop(); }
                started.Reset(); clock.Restart(); engine.Speak("apple", "bf_emma", 0, 85);
                Check(SpinWait.SpinUntil(() => spoken == "apple" || failure != null, 5000) && failure == null, "rapid changes play newest word");
                Console.WriteLine("Kokoro newest after cancellation ms: " + clock.ElapsedMilliseconds);
                Check(engine.WorkerId == workerId, "ordinary cancelled inference retains worker PID");
                engine.Stop(); spoken = null; Thread.Sleep(800);
                Check(spoken == null, "stop never publishes stale speech");
            }
            Check(!Directory.GetDirectories(Path.GetTempPath(), "PointCursor-Kokoro-" + Process.GetCurrentProcess().Id + "-*").Any(), "Kokoro dispose removes temporary audio directory");
        }
        private static void SapiBridge()
        {
            using (var service = new SpeechService())
            using (var started = new ManualResetEvent(false))
            {
                string voice = service.Voices.First(v => !service.IsKokoroVoice(v)), failure = null, spoken = null;
                var settings = new AppSettings { Voice = voice, Volume = 85 };
                service.Failed += delegate(string error) { failure = error; started.Set(); };
                service.Started += delegate(long id, string word) { spoken = word; started.Set(); };
                service.Prepare(settings); Thread.Sleep(200);
                var clock = Stopwatch.StartNew(); service.Speak("English", settings);
                Check(started.WaitOne(5000) && failure == null && spoken == "English", "SAPI renders through shared output");
                Console.WriteLine("SAPI warm English ms: " + clock.ElapsedMilliseconds);
                service.Suspend(); started.Reset(); spoken = null; Thread.Sleep(150);
                Check(!started.WaitOne(100), "pause cancels playback");
                service.Prepare(settings); service.Speak("apple", settings);
                Check(started.WaitOne(5000) && failure == null && spoken == "apple", "resume reopens shared device from sample zero");
                service.Stop();
            }
        }
        private static void KokoroFailures()
        {
            string script = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fault-worker.mjs");
            string runtime = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Kokoro");
            try
            {
                File.WriteAllText(script, "process.stdout.write('READY\\n'); process.stdin.resume(); setInterval(()=>{},1000);");
                using (var output = new AudioOutput())
                using (var engine = new KokoroSpeechEngine(runtime, output, script))
                {
                    engine.Prepare("af_heart"); Check(SpinWait.SpinUntil(() => engine.IsReady, 5000), "fault worker starts");
                    int id = engine.WorkerId;
                    engine.Speak(output.Stop(), "hello", "af_heart", 0, 85); Thread.Sleep(150);
                    var clock = Stopwatch.StartNew(); output.Stop(); engine.Stop();
                    Check(SpinWait.SpinUntil(() => engine.WorkerId == 0, 2500), "stuck cancelled Kokoro worker killed within grace period");
                    Console.WriteLine("Stale worker termination ms: " + clock.ElapsedMilliseconds);
                    engine.Prepare("af_heart"); Check(SpinWait.SpinUntil(() => engine.IsReady, 5000) && engine.WorkerId != id, "worker recovers after stuck inference");
                }
                File.WriteAllText(script, "process.exit(2);");
                using (var output = new AudioOutput())
                using (var engine = new KokoroSpeechEngine(runtime, output, script))
                {
                    string error = null; engine.Failed += delegate(string message) { error = message; };
                    engine.Prepare("af_heart");
                    Check(SpinWait.SpinUntil(() => error != null, 5000) && !engine.IsReady, "worker early exit reports recoverable failure");
                }
            }
            finally { File.Delete(script); }
        }
        private static void Pcm()
        {
            var buffer = new PcmPlaybackBuffer(); var source = new byte[] { 1, 0, 2, 0, 255, 127, 0, 128 };
            buffer.DeviceOpened(2); long ticket = buffer.Cancel(); buffer.Set(ticket, source, 100);
            var first = new byte[6];
            Check(buffer.Fill(first, 3) == ticket && first.SequenceEqual(new byte[] { 0, 0, 0, 0, 1, 0 }), "device guard preserves very quiet first PCM sample");
            var rest = new byte[8]; buffer.Fill(rest, 4);
            Check(rest.SequenceEqual(new byte[] { 2, 0, 255, 127, 0, 128, 0, 0 }), "PCM tail preserved across device buffers");
            Check(buffer.Fill(rest, 4) == -1 && rest.All(b => b == 0), "idle renderer outputs silence");
            long latest = buffer.Cancel();
            Check(!buffer.Set(ticket, source, 100), "stale PCM cannot replace newest audio");
            buffer.Set(latest, source, 100); buffer.Fill(first, 1); buffer.Cancel(); buffer.Fill(rest, 4);
            Check(rest.All(b => b == 0), "cancellation clears remaining audio");
            Check(RuntimeAssets.Validate(Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString()), false) != null, "missing runtime diagnosed");
        }
        private static void RuntimeChecks()
        {
            string root = Path.Combine(Path.GetTempPath(), "PointCursor-integrity-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            try
            {
                var lines = new List<string>();
                for (int i = 0; i < 20; i++)
                {
                    string file = i + ".bin"; var bytes = new byte[] { 1, 2, 3, 4 };
                    File.WriteAllBytes(Path.Combine(root, file), bytes);
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        lines.Add("4\t" + BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-", "").ToLowerInvariant() + "\t" + file);
                }
                File.WriteAllLines(Path.Combine(root, "assets.tsv"), lines);
                Check(RuntimeAssets.Validate(root, true) == null, "complete runtime hashes validate");
                File.WriteAllBytes(Path.Combine(root, "0.bin"), new byte[] { 4, 3, 2, 1 });
                Check(RuntimeAssets.Validate(root, true) != null, "same-length damaged asset detected by full diagnostic");
                File.WriteAllText(Path.Combine(root, "0.bin"), "version https://git-lfs.github.com/spec/v1\noid sha256:example\nsize 4\n");
                Check(RuntimeAssets.Validate(root, false) != null, "unhydrated LFS pointer rejected at startup");
                File.Delete(Path.Combine(root, "0.bin"));
                Check(RuntimeAssets.Validate(root, false) != null, "missing runtime file detected at startup");
            }
            finally { foreach (string file in Directory.GetFiles(root)) File.Delete(file); Directory.Delete(root, false); }
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
            using (var service = new SpeechService())
            using (var form = new SettingsForm(new AppSettings { Voice = service.Voices.First(v => v.StartsWith("Kokoro · ", StringComparison.Ordinal)) }, service.Voices))
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
