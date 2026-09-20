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
                if (args.Length > 0 && args[0] == "--fault-reader") return FaultReader();
                Rules(); Gates(); Gestures(); Settings(); Pcm(); Audio(); SapiBridge(); Worker().GetAwaiter().GetResult();
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
            Check(WordRules.NormalizeGesture("==hello==", "==hello== world") == "hello", "highlight wrapper after gesture");
            Check(WordRules.NormalizeGesture("hell", "==hello== world") == "hello", "highlight offset shift after gesture");
            Check(WordRules.NormalizeGesture("=hel", "==hello== world") == "hello", "partial highlight marker after gesture");
            Check(WordRules.NormalizeGesture("hello world", "==hello== world") == null, "highlight context does not crop multiple words");
            Check(WordRules.NormalizeGesture("world", "==hello== world") == "world", "unrelated highlight does not replace gesture word");
            Check(WordRules.ReconcileGesture("hell", "hello world", "hello") == "hello", "enclosing word restores shifted final letter");
            Check(WordRules.ReconcileGesture("=hel", "hello world", "hello") == "hello", "enclosing word restores marker-shifted word");
            Check(WordRules.ReconcileGesture("h", "hello", "hello") == "hello", "single accessible word restores source-mode highlight shift");
            Check(WordRules.ReconcileGesture("he", "hello world", "hello") == "he", "enclosing word does not expand distant fragment");
            Check(WordRules.ReconcileGesture("hello world", "hello world", "hello") == null, "enclosing word does not crop multiple words");
        }
        private static void Gates()
        {
            var gate = new SelectionGate();
            long old = gate.Current; long current = gate.Invalidate();
            Check(!gate.TryAccept(old), "reject stale selection");
            Check(gate.TryAccept(current), "accept current selection");
            Check(!gate.TryAccept(current), "suppress duplicate result from one request");
            Check(gate.TryAccept(gate.Invalidate()), "same word may replay on new gesture");
            Check(gate.TryAccept(gate.Invalidate()), "same word may replay on every explicit copy");
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
        private static void Gestures()
        {
            var tracker = new SelectionGesture(4, 4, 4, 4, 500);
            IntPtr window = new IntPtr(1);
            tracker.Down(window, 10, 10, 100);
            Check(tracker.Up( 10, 10) == GestureKind.None, "single click is not a selection");
            tracker.Down(window, 10, 10, 450);
            Check(tracker.Up( 12, 11) == GestureKind.DoubleClick, "double click tolerates jitter and a long second hold");
            tracker.Down(window, 10, 10, 480);
            Check(tracker.Up( 10, 10) == GestureKind.None, "third click does not produce duplicate word");
            tracker.Reset(); tracker.Down(window, 10, 10, 100);
            tracker.Up( 10, 10); tracker.Down(window, 10, 10, 650);
            Check(tracker.Up( 10, 10) == GestureKind.None, "slow clicks are not a double click");
            tracker.Reset(); tracker.Down(window, 10, 10, UInt32.MaxValue - 40);
            tracker.Up( 10, 10); tracker.Down(window, 10, 10, 20);
            Check(tracker.Up( 10, 10) == GestureKind.DoubleClick, "input timestamp wrap preserves double click");
            tracker.Down(window, 10, 10, 1000);
            Check(tracker.Up( 40, 10) == GestureKind.Drag, "coalesced pointer movement still detects drag");
            tracker.Down(window, 10, 10, 1100); tracker.Up(10, 10);
            tracker.Down(new IntPtr(2), 10, 10, 1150);
            Check(tracker.Up(10, 10) == GestureKind.None, "clicks in different windows do not form a double click");
            tracker.Down(window, 10, 10, 1200); tracker.Reset();
            Check(tracker.Up( 40, 10) == GestureKind.None, "interrupted gesture reset");
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
                File.WriteAllText(path, "<AppSettings><Voice>Kokoro old voice</Voice><Rate>2</Rate><Volume>63</Volume><CopyToSpeak>false</CopyToSpeak></AppSettings>");
                loaded = AppSettings.Load(path, out reset);
                Check(!reset && loaded.Rate == 2 && loaded.Volume == 63 && !loaded.CopyToSpeak, "legacy voice ignored while other preferences survive");
                Check(loaded.Save(path) && !File.ReadAllText(path).Contains("<Voice>"), "saved settings drop obsolete voice configuration");
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
                synth.SelectVoice(SpeechService.VoiceName); synth.SetOutputToWaveStream(wave); synth.Speak("hello");
                Check(wave.Length > 1000 && System.Text.Encoding.ASCII.GetString(wave.ToArray(), 0, 4) == "RIFF", "local voice generates WAV without web service");
            }
            using (var service = new SpeechService())
                Check(service.Available && SpeechService.VoiceName == "Microsoft Zira Desktop", "Zira is the sole voice");
        }
        private static void SapiBridge()
        {
            using (var service = new SpeechService())
            using (var started = new ManualResetEvent(false))
            {
                string failure = null, spoken = null;
                var settings = new AppSettings { Volume = 85 };
                service.Failed += delegate(string error) { failure = error; started.Set(); };
                service.Started += delegate(long id, string word) { spoken = word; started.Set(); };
                service.Prepare(settings); Thread.Sleep(200);
                var clock = Stopwatch.StartNew(); service.Speak("English", settings);
                Check(started.WaitOne(5000) && failure == null && spoken == "English", "SAPI renders through shared output");
                Console.WriteLine("SAPI warm English ms: " + clock.ElapsedMilliseconds);
                started.Reset(); spoken = null;
                service.Speak("English", settings);
                Check(started.WaitOne(5000) && failure == null && spoken == "English", "Zira repeats the same word on a new request");
                service.Suspend(); started.Reset(); spoken = null; Thread.Sleep(150);
                Check(!started.WaitOne(100), "pause cancels playback");
                service.Prepare(settings); service.Speak("apple", settings);
                Check(started.WaitOne(5000) && failure == null && spoken == "apple", "resume reopens shared device from sample zero");
                service.Stop();
            }
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
            using (var client = new SelectionClient(System.Reflection.Assembly.GetExecutingAssembly().Location, "--fault-reader"))
            {
                foreach (int fault in new[] { 1, 2, 3 })
                {
                    var failed = await client.QueryAsync(new IntPtr(fault), 0, 0, false, CancellationToken.None);
                    Check(failed.Word == null && failed.Status == (fault == 1 ? "timeout" : "unavailable"), "reader fault bounded: " + fault);
                    Check((await client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None)).Status == "stale", "reader restarts after fault: " + fault);
                }
                using (var cancel = new CancellationTokenSource())
                {
                    var old = client.QueryAsync(new IntPtr(1), 0, 0, false, cancel.Token);
                    await Task.Delay(80);
                    var newest = client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None);
                    var clock = Stopwatch.StartNew(); cancel.Cancel();
                    try { await old; Check(false, "old reader request must cancel"); } catch (OperationCanceledException) { }
                    Check((await newest).Status == "stale" && clock.ElapsedMilliseconds < 1000, "new request proceeds promptly after cancelling hung predecessor");
                }
                for (int i = 0; i < 100; i++)
                    if ((await client.QueryAsync(IntPtr.Zero, 0, 0, false, CancellationToken.None)).Status != "stale") throw new Exception("Reader protocol lost synchronization");
                Check(true, "100 sequential reader requests preserve protocol synchronization");
            }
            var closing = new SelectionClient(System.Reflection.Assembly.GetExecutingAssembly().Location, "--fault-reader");
            var inflight = closing.QueryAsync(new IntPtr(1), 0, 0, false, CancellationToken.None);
            await Task.Delay(80); closing.Dispose();
            bool stopped = false;
            try { await inflight; } catch (OperationCanceledException) { stopped = true; }
            Check(stopped, "disposing reader cancels inflight request without racing process teardown");
        }
        private static int FaultReader()
        {
            string line;
            while ((line = Console.ReadLine()) != null)
            {
                string[] fields = line.Split('|');
                if (fields[1] == "1") Thread.Sleep(10000);
                if (fields[1] == "3") return 2;
                Console.WriteLine(fields[0] + (fields[1] == "2" ? "|word|%" : "|stale|"));
            }
            return 0;
        }
        private static void Render(string path)
        {
            Application.EnableVisualStyles();
            using (var service = new SpeechService())
            using (var form = new SettingsForm(new AppSettings(), service.Available))
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
