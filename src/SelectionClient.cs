using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace PointCursor
{
    internal sealed class SelectionResult
    {
        public string Status;
        public string Word;
        public SelectionResult(string status, string word) { Status = status; Word = word; }
    }
    internal sealed class SelectionClient : IDisposable
    {
        private readonly SemaphoreSlim mutex = new SemaphoreSlim(1, 1);
        private Process worker;
        private bool disposed;
        private long serial;
        private readonly string executable;
        private readonly string arguments;
        public SelectionClient(string path, string arguments = "") { executable = path; this.arguments = arguments; }
        public async Task<SelectionResult> QueryAsync(IntPtr window, int x, int y, bool guardOnly, CancellationToken cancel)
        { return await QueryAsync(window, x, y, x, y, guardOnly, cancel); }
        public async Task<SelectionResult> QueryAsync(IntPtr window, int startX, int startY, int endX, int endY, bool guardOnly, CancellationToken cancel)
        {
            await mutex.WaitAsync(cancel);
            try
            {
                if (disposed) return new SelectionResult("unavailable", null);
                cancel.ThrowIfCancellationRequested();
                if (worker == null || worker.HasExited)
                {
                    StopWorker();
                    worker = Process.Start(new ProcessStartInfo(executable, arguments) {
                        UseShellExecute = false, CreateNoWindow = true, RedirectStandardInput = true, RedirectStandardOutput = true,
                        WorkingDirectory = Path.GetDirectoryName(executable)
                    });
                }
                string id = (++serial).ToString(System.Globalization.CultureInfo.InvariantCulture);
                worker.StandardInput.WriteLine(id + "|" + window.ToInt64() + "|" + startX + "|" + startY + "|" + endX + "|" + endY + "|" + (guardOnly ? "guard" : "gesture"));
                worker.StandardInput.Flush();
                Task<string> read = worker.StandardOutput.ReadLineAsync();
                Task limit = Task.Delay(900, cancel);
                if (await Task.WhenAny(read, limit) != read)
                {
                    StopWorker();
                    // Observe a pipe error after killing a stuck provider, if any.
                    ObserveFault(read);
                    cancel.ThrowIfCancellationRequested();
                    return new SelectionResult("timeout", null);
                }
                string line = await read;
                cancel.ThrowIfCancellationRequested();
                if (line == null || !line.StartsWith(id + "|", StringComparison.Ordinal)) { StopWorker(); return new SelectionResult("unavailable", null); }
                string[] fields = line.Split('|');
                string word = fields.Length == 3 && fields[1] == "word" ? WordRules.Normalize(Encoding.UTF8.GetString(Convert.FromBase64String(fields[2]))) : null;
                return new SelectionResult(fields[1], word);
            }
            catch (OperationCanceledException) { StopWorker(); throw; }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is InvalidOperationException) && !(ex is System.ComponentModel.Win32Exception) && !(ex is FormatException)) throw;
                StopWorker(); return new SelectionResult("unavailable", null);
            }
            finally { mutex.Release(); }
        }
        private static void ObserveFault(Task task)
        { task.ContinueWith(t => { var ignored = t.Exception; }, TaskContinuationOptions.OnlyOnFaulted); }
        private void StopWorker()
        {
            if (worker == null) return;
            try { if (!worker.HasExited) worker.Kill(); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { }
            worker.Dispose(); worker = null;
        }
        public void Dispose() { disposed = true; StopWorker(); }
    }
}
