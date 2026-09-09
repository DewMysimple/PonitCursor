using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PointCursor
{
    internal sealed class KokoroSpeechEngine : IDisposable
    {
        private sealed class Request { public long Ticket; public string Text, Voice; public int Rate, Volume; }
        private readonly object sync = new object();
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private readonly string root, tempPath, workerScript;
        private readonly AudioOutput output;
        private readonly bool ownsOutput;
        private Thread thread;
        private Process worker;
        private Request pending;
        private bool prepare;
        private string warmVoice = "af_heart";
        private volatile bool disposed;
        private int requestId;
        public event Action<string> Failed;
        public event Action<string> Started;
        public bool IsReady { get; private set; }
        public int WorkerId { get { lock (sync) return worker == null ? 0 : worker.Id; } }
        public string RuntimeError { get; private set; }
        public bool Available { get { return RuntimeError == null; } }
        public KokoroSpeechEngine(string rootPath) : this(rootPath, new AudioOutput(), true) { }
        internal KokoroSpeechEngine(string rootPath, AudioOutput output) : this(rootPath, output, false) { }
        internal KokoroSpeechEngine(string rootPath, AudioOutput output, string script) : this(rootPath, output, false) { workerScript = script; }
        private KokoroSpeechEngine(string rootPath, AudioOutput output, bool owns)
        {
            root = rootPath; this.output = output; ownsOutput = owns;
            workerScript = Path.Combine(root, "worker.mjs");
            tempPath = Path.Combine(Path.GetTempPath(), "PointCursor-Kokoro-" + Process.GetCurrentProcess().Id + "-" + Guid.NewGuid().ToString("N"));
            RuntimeError = RuntimeAssets.Validate(root, false);
            if (owns) output.Started += delegate(long id, string text) { if (Started != null) Started(text); };
            if (owns) output.Failed += delegate(string message) { if (Failed != null) Failed(message); };
        }
        public IList<string> GetVoiceIds()
        {
            var result = new List<string>();
            if (!Available) return result;
            foreach (string file in Directory.GetFiles(Path.Combine(root, "voices"), "*.bin"))
            {
                string id = Path.GetFileNameWithoutExtension(file);
                if (System.Text.RegularExpressions.Regex.IsMatch(id, @"\A[ab][fm]_[a-z]+\z")) result.Add(id);
            }
            result.Sort(delegate(string a, string b) { return a == "af_heart" ? (b == a ? 0 : -1) : b == "af_heart" ? 1 : StringComparer.Ordinal.Compare(a, b); });
            return result;
        }
        public static double RateToSpeed(int rate) { return 1 + Math.Max(-5, Math.Min(5, rate)) * 0.08; }
        private void EnsureThread()
        {
            if (thread != null) return;
            thread = new Thread(Pump) { IsBackground = true, Name = "PointCursor Kokoro" }; thread.Start();
        }
        public void Prepare(string voice)
        {
            if (!Available) return;
            lock (sync) { if (disposed) return; warmVoice = voice; prepare = true; EnsureThread(); changed.Set(); }
            output.Prepare();
        }
        public void Speak(string text, string voice, int rate, int volume)
        { Speak(output.Stop(), text, voice, rate, volume); }
        internal void Speak(long ticket, string text, string voice, int rate, int volume)
        {
            if (!Available) throw new InvalidOperationException(RuntimeError);
            lock (sync)
            {
                if (disposed) throw new ObjectDisposedException("KokoroSpeechEngine");
                pending = new Request { Ticket = ticket, Text = text, Voice = voice, Rate = rate, Volume = volume };
                warmVoice = voice; EnsureThread(); changed.Set();
            }
            output.Prepare();
        }
        public void Stop()
        {
            if (ownsOutput) output.Stop();
            lock (sync) { pending = null; }
            if (!disposed) changed.Set();
        }
        private void Pump()
        {
            try
            {
                while (!disposed)
                {
                    Request request; string voice;
                    lock (sync)
                    {
                        request = pending; pending = null; voice = warmVoice;
                        if (request == null && !prepare) voice = null;
                        prepare = false;
                    }
                    if (voice == null) { changed.WaitOne(250); continue; }
                    string path = null;
                    try
                    {
                        EnsureWorker(voice);
                        if (request == null || request.Ticket != output.Generation) continue;
                        Directory.CreateDirectory(tempPath);
                        int id = ++requestId;
                        path = Path.Combine(tempPath, "audio-" + id.ToString(CultureInfo.InvariantCulture) + ".wav");
                        string command = String.Join("\t", new[] { "SPEAK", id.ToString(CultureInfo.InvariantCulture), request.Voice,
                            RateToSpeed(request.Rate).ToString("0.00", CultureInfo.InvariantCulture), "100",
                            Convert.ToBase64String(Encoding.UTF8.GetBytes(request.Text)), Convert.ToBase64String(Encoding.UTF8.GetBytes(path)) });
                        worker.StandardInput.WriteLine(command); worker.StandardInput.Flush();
                        string line = ReadLine(10000, request.Ticket);
                        if (line != "DONE\t" + id.ToString(CultureInfo.InvariantCulture)) throw new InvalidDataException("Kokoro worker protocol error.");
                        if (!disposed && request.Ticket == output.Generation)
                            output.Play(request.Ticket, request.Text, PcmAudio.ReadWave(path), request.Volume);
                    }
                    catch (Exception ex)
                    {
                        KillWorker();
                        if (!disposed && (request == null || request.Ticket == output.Generation) && !(ex is OperationCanceledException))
                        {
                            string message = ex is TimeoutException ? "Kokoro 合成超时，请重试。" :
                                "Kokoro 启动或合成失败，请运行诊断脚本检查运行时文件。";
                            if (Failed != null) Failed(message);
                        }
                    }
                    finally { if (path != null) TryDelete(path); }
                }
            }
            finally
            {
                KillWorker();
                try { if (Directory.Exists(tempPath)) Directory.Delete(tempPath, false); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            }
        }
        private void EnsureWorker(string voice)
        {
            if (worker != null && !worker.HasExited && IsReady) return;
            KillWorker();
            var start = new ProcessStartInfo {
                FileName = Path.Combine(root, "node.exe"),
                Arguments = "\"" + workerScript + "\" --voice " + voice,
                WorkingDirectory = root, UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardInput = true, RedirectStandardOutput = true, RedirectStandardError = true
            };
            start.EnvironmentVariables["NODE_NO_WARNINGS"] = "1";
            var process = new Process { StartInfo = start };
            // Drain stderr without keeping generated text or paths in a history.
            process.ErrorDataReceived += delegate { };
            lock (sync) { if (disposed) { process.Dispose(); throw new OperationCanceledException(); } worker = process; process.Start(); }
            process.BeginErrorReadLine();
            if (ReadLine(15000, -1) != "READY") throw new InvalidDataException("Kokoro warm-up failed.");
            IsReady = true;
        }
        private string ReadLine(int timeout, long ticket)
        {
            Task<string> read = worker.StandardOutput.ReadLineAsync();
            long start = Native.Now, staleAt = 0;
            while (!read.Wait(25))
            {
                if (disposed) throw new OperationCanceledException();
                if (ticket >= 0 && ticket != output.Generation)
                {
                    if (staleAt == 0) staleAt = Native.Now;
                    // Retain the warm model for ordinary inference; kill stuck stale work.
                    if (Native.Now - staleAt > 1000) throw new OperationCanceledException();
                }
                if (Native.Now - start > timeout) throw new TimeoutException();
            }
            return read.Result;
        }
        private void KillWorker()
        {
            Process process; lock (sync) { process = worker; worker = null; IsReady = false; }
            if (process == null) return;
            try { if (!process.HasExited) process.Kill(); process.WaitForExit(1000); }
            catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            finally { process.Dispose(); }
        }
        private static void TryDelete(string path)
        { try { File.Delete(path); } catch (IOException) { } catch (UnauthorizedAccessException) { } }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lock (sync) { pending = null; }
            changed.Set();
            if (thread == null || thread.Join(2500)) changed.Dispose();
            if (ownsOutput) output.Dispose();
        }
    }
}
