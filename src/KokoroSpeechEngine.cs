using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Media;
using System.Text;
using System.Threading.Tasks;

namespace PointCursor
{
    internal sealed class KokoroSpeechEngine : IDisposable
    {
        private readonly object sync = new object();
        private readonly string root, nodePath, workerPath, modelPath, voicesPath, tempPath;
        private Process worker;
        private SoundPlayer player;
        private string lastWave;
        private int generation, nextRequest;
        private bool busy, disposed;

        public event Action<string> Failed;
        public event Action<string> Started;

        public bool Available
        {
            get
            {
                return File.Exists(nodePath)
                    && File.Exists(workerPath)
                    && File.Exists(Path.Combine(root, "lib", "kokoro.js"))
                    && File.Exists(Path.Combine(modelPath, "config.json"))
                    && File.Exists(Path.Combine(modelPath, "tokenizer.json"))
                    && File.Exists(Path.Combine(modelPath, "onnx", "model_quantized.onnx"))
                    && File.Exists(Path.Combine(root, "node_modules", "@huggingface", "transformers", "package.json"))
                    && File.Exists(Path.Combine(root, "node_modules", "@huggingface", "transformers", "dist", "transformers.node.mjs"))
                    && File.Exists(Path.Combine(root, "node_modules", "onnxruntime-common", "dist", "esm", "index.js"))
                    && File.Exists(Path.Combine(root, "node_modules", "onnxruntime-node", "dist", "index.js"))
                    && File.Exists(Path.Combine(root, "node_modules", "onnxruntime-node", "bin", "napi-v3", "win32", "x64", "onnxruntime_binding.node"))
                    && File.Exists(Path.Combine(root, "node_modules", "onnxruntime-node", "bin", "napi-v3", "win32", "x64", "onnxruntime.dll"))
                    && File.Exists(Path.Combine(root, "node_modules", "phonemizer", "dist", "phonemizer.js"))
                    && Directory.Exists(voicesPath);
            }
        }

        public KokoroSpeechEngine(string rootPath)
        {
            root = rootPath;
            nodePath = Path.Combine(root, "node.exe");
            workerPath = Path.Combine(root, "worker.mjs");
            modelPath = Path.Combine(root, "model");
            voicesPath = Path.Combine(root, "voices");
            tempPath = Path.Combine(Path.GetTempPath(), "PointCursor-Kokoro-" + Process.GetCurrentProcess().Id.ToString(CultureInfo.InvariantCulture));
        }

        public IList<string> GetVoiceIds()
        {
            var result = new List<string>();
            if (!Available) return result;
            foreach (string file in Directory.GetFiles(voicesPath, "*.bin", SearchOption.TopDirectoryOnly))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (id.Length > 3 && (id[0] == 'a' || id[0] == 'b') && (id[1] == 'f' || id[1] == 'm') && id[2] == '_') result.Add(id);
            }
            result.Sort(delegate(string left, string right) {
                if (left == "af_heart") return right == "af_heart" ? 0 : -1;
                if (right == "af_heart") return 1;
                return StringComparer.Ordinal.Compare(left, right);
            });
            return result;
        }

        public static double RateToSpeed(int rate)
        {
            int bounded = Math.Max(-5, Math.Min(5, rate));
            return 1.0 + bounded * 0.08;
        }

        public void Speak(string text, string voice, int rate, int volume)
        {
            if (!Available) throw new InvalidOperationException("Kokoro runtime is incomplete.");
            int ticket, requestId;
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("KokoroSpeechEngine");
                ticket = ++generation;
                requestId = ++nextRequest;
                StopPlayerLocked();
                DeleteLastWaveLocked();
                if (busy) KillWorkerLocked();
                busy = true;
            }
            Task.Run(delegate { Generate(ticket, requestId, text, voice, RateToSpeed(rate), Math.Max(0, Math.Min(100, volume))); });
        }

        private void Generate(int ticket, int requestId, string text, string voice, double speed, int volume)
        {
            Process current = null;
            string output = null;
            try
            {
                current = GetOrStartWorker(ticket);
                if (current == null) return;
                Directory.CreateDirectory(tempPath);
                output = Path.Combine(tempPath, "audio-" + requestId.ToString(CultureInfo.InvariantCulture) + ".wav");
                string command = String.Join("\t", new[] {
                    "SPEAK",
                    requestId.ToString(CultureInfo.InvariantCulture),
                    voice,
                    speed.ToString("0.00", CultureInfo.InvariantCulture),
                    volume.ToString(CultureInfo.InvariantCulture),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(text)),
                    Convert.ToBase64String(Encoding.UTF8.GetBytes(output))
                });
                lock (sync)
                {
                    if (disposed || ticket != generation || worker != current) return;
                    current.StandardInput.WriteLine(command);
                    current.StandardInput.Flush();
                }
                string response = ReadLine(current, 30000);
                string expected = "DONE\t" + requestId.ToString(CultureInfo.InvariantCulture);
                if (!String.Equals(response, expected, StringComparison.Ordinal)) throw new InvalidOperationException(ParseError(response));
                if (!IsWave(output)) throw new InvalidDataException("Kokoro did not create a valid WAV file.");

                var nextPlayer = new SoundPlayer(output);
                nextPlayer.Load();
                Action<string> started = null;
                lock (sync)
                {
                    if (disposed || ticket != generation || worker != current)
                    {
                        nextPlayer.Dispose();
                        TryDelete(output);
                        return;
                    }
                    busy = false;
                    player = nextPlayer;
                    lastWave = output;
                    player.Play();
                    started = Started;
                }
                if (started != null) started(text);
            }
            catch (Exception ex)
            {
                bool notify;
                lock (sync)
                {
                    notify = !disposed && ticket == generation;
                    if (worker == current) KillWorkerLocked();
                    if (ticket == generation) busy = false;
                }
                if (output != null) TryDelete(output);
                if (notify)
                {
                    Action<string> failed = Failed;
                    if (failed != null) failed("Kokoro 发音失败，请重试或切换到 Windows 系统语音。" + ErrorDetail(ex));
                }
            }
        }

        private Process GetOrStartWorker(int ticket)
        {
            Process current;
            bool waitForReady = false;
            lock (sync)
            {
                if (disposed || ticket != generation) return null;
                if (worker == null || HasExited(worker))
                {
                    if (worker != null) { worker.Dispose(); worker = null; }
                    var start = new ProcessStartInfo {
                        FileName = nodePath,
                        Arguments = "\"" + workerPath.Replace("\"", "\\\"") + "\"",
                        WorkingDirectory = root,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        RedirectStandardInput = true,
                        RedirectStandardOutput = true
                    };
                    start.EnvironmentVariables["NODE_NO_WARNINGS"] = "1";
                    worker = new Process { StartInfo = start };
                    if (!worker.Start()) throw new InvalidOperationException("Unable to start Kokoro worker.");
                    waitForReady = true;
                }
                current = worker;
            }
            if (waitForReady && !String.Equals(ReadLine(current, 30000), "READY", StringComparison.Ordinal)) throw new InvalidOperationException("Kokoro worker did not become ready.");
            lock (sync) return !disposed && ticket == generation && worker == current ? current : null;
        }

        private static string ReadLine(Process process, int timeout)
        {
            Task<string> read = process.StandardOutput.ReadLineAsync();
            if (!read.Wait(timeout)) throw new TimeoutException("Kokoro worker timed out.");
            string value = read.Result;
            if (value == null) throw new EndOfStreamException("Kokoro worker exited unexpectedly.");
            return value;
        }

        private static string ParseError(string response)
        {
            if (response == null) return "Kokoro worker returned no response.";
            string[] fields = response.Split('\t');
            if (fields.Length == 3 && fields[0] == "ERROR")
            {
                try { return Encoding.UTF8.GetString(Convert.FromBase64String(fields[2])); }
                catch (FormatException) { }
            }
            return "Unexpected Kokoro worker response.";
        }

        private static string ErrorDetail(Exception error)
        {
            AggregateException aggregate = error as AggregateException;
            if (aggregate != null && aggregate.InnerExceptions.Count == 1) error = aggregate.InnerExceptions[0];
            return error is TimeoutException ? "（推理超时）" : "";
        }

        private static bool IsWave(string path)
        {
            try
            {
                if (!File.Exists(path) || new FileInfo(path).Length <= 44) return false;
                using (var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    var header = new byte[4];
                    return stream.Read(header, 0, header.Length) == 4 && Encoding.ASCII.GetString(header) == "RIFF";
                }
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }

        private static bool HasExited(Process process)
        {
            try { return process.HasExited; }
            catch (InvalidOperationException) { return true; }
        }

        public void Stop()
        {
            lock (sync)
            {
                if (disposed) return;
                generation++;
                StopPlayerLocked();
                DeleteLastWaveLocked();
                if (busy) KillWorkerLocked();
                busy = false;
            }
        }

        private void StopPlayerLocked()
        {
            if (player == null) return;
            try { player.Stop(); } catch (InvalidOperationException) { }
            player.Dispose();
            player = null;
        }

        private void DeleteLastWaveLocked()
        {
            if (lastWave == null) return;
            TryDelete(lastWave);
            lastWave = null;
        }

        private void KillWorkerLocked()
        {
            if (worker == null) return;
            try { if (!worker.HasExited) worker.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            try { worker.WaitForExit(1000); } catch (InvalidOperationException) { }
            worker.Dispose();
            worker = null;
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }

        public void Dispose()
        {
            lock (sync)
            {
                if (disposed) return;
                disposed = true;
                generation++;
                StopPlayerLocked();
                DeleteLastWaveLocked();
                KillWorkerLocked();
                busy = false;
            }
            try
            {
                if (Directory.Exists(tempPath))
                {
                    foreach (string file in Directory.GetFiles(tempPath, "audio-*.wav", SearchOption.TopDirectoryOnly)) TryDelete(file);
                    Directory.Delete(tempPath, false);
                }
            }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
