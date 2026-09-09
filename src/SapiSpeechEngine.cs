using System;
using System.IO;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Threading;

namespace PointCursor
{
    internal sealed class SapiSpeechEngine : IDisposable
    {
        private sealed class Request { public long Ticket; public string Word, Voice; public int Rate, Volume; public bool Warm; }
        private readonly object sync = new object();
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private readonly AudioOutput output;
        private Request pending;
        private Thread thread;
        private volatile bool disposed;
        public event Action<string> Failed;
        public SapiSpeechEngine(AudioOutput output) { this.output = output; }
        public void Speak(long ticket, string word, string voice, int rate, int volume, bool warm)
        {
            lock (sync)
            {
                if (disposed) return;
                pending = new Request { Ticket = ticket, Word = word, Voice = voice, Rate = rate, Volume = volume, Warm = warm };
                if (thread == null) { thread = new Thread(Pump) { IsBackground = true, Name = "PointCursor SAPI" }; thread.SetApartmentState(ApartmentState.MTA); thread.Start(); }
                changed.Set();
            }
        }
        public void Stop() { lock (sync) pending = null; if (!disposed) changed.Set(); }
        private void Pump()
        {
            SpeechSynthesizer synth = null;
            try
            {
                while (!disposed)
                {
                    Request request; lock (sync) { request = pending; pending = null; }
                    if (request == null) { changed.WaitOne(250); continue; }
                    try
                    {
                        if (synth == null) synth = new SpeechSynthesizer();
                        using (var stream = new MemoryStream())
                        using (var done = new ManualResetEvent(false))
                        {
                            Exception error = null;
                            var completionLock = new object(); bool accepting = true;
                            EventHandler<SpeakCompletedEventArgs> completed = delegate(object sender, SpeakCompletedEventArgs args) {
                                lock (completionLock) { if (accepting) { error = args.Error; done.Set(); } }
                            };
                            synth.SpeakCompleted += completed;
                            try
                            {
                                synth.SelectVoice(request.Voice); synth.Rate = request.Rate; synth.Volume = 100;
                                synth.SetOutputToAudioStream(stream, new SpeechAudioFormatInfo(24000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                                synth.SpeakAsync(request.Word);
                                long start = Native.Now; bool cancel = false;
                                while (!done.WaitOne(25))
                                {
                                    if (!cancel && (disposed || (!request.Warm && request.Ticket != output.Generation) || Native.Now - start > 5000))
                                    { cancel = true; synth.SpeakAsyncCancelAll(); }
                                    if (Native.Now - start > 7000) throw new TimeoutException();
                                }
                                synth.SetOutputToNull();
                                if (error != null) throw new InvalidOperationException("SAPI synthesis failed.", error);
                                if (!cancel && !request.Warm && !disposed && request.Ticket == output.Generation)
                                    output.Play(request.Ticket, request.Word, stream.ToArray(), request.Volume);
                            }
                            finally { lock (completionLock) accepting = false; synth.SpeakCompleted -= completed; }
                        }
                    }
                    catch (Exception)
                    {
                        if (synth != null) { synth.Dispose(); synth = null; }
                        if (!disposed && request.Ticket == output.Generation && Failed != null) Failed("Windows 语音合成失败，请检查语音包或切换语音。");
                    }
                }
            }
            finally { if (synth != null) synth.Dispose(); }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; lock (sync) pending = null; changed.Set();
            if (thread == null || thread.Join(2500)) changed.Dispose();
        }
    }
}
