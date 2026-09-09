using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Speech.Synthesis;
using System.Speech.AudioFormat;

namespace PointCursor
{
    // Short synthetic-only Windows output loopback; never opens a microphone.
    internal static class AudioProbe
    {
        [ComImport, Guid("C8ADBD64-E71E-48A0-A4DE-185C395CD317"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface CaptureClient
        {
            [PreserveSig] int GetBuffer(out IntPtr data, out uint frames, out uint flags, out ulong position, out ulong counter);
            [PreserveSig] int ReleaseBuffer(uint frames);
            [PreserveSig] int GetNextPacketSize(out uint frames);
        }
        [MTAThread]
        static int Main(string[] args)
        {
            try
            {
                string prefix = args[0]; Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(prefix)));
                byte[] pcm;
                if (args.Length > 1) pcm = PcmAudio.ReadWave(args[1]);
                else using (var synth = new SpeechSynthesizer()) using (var stream = new MemoryStream())
                {
                    synth.SelectVoice("Microsoft Zira Desktop"); synth.Volume = 100;
                    synth.SetOutputToAudioStream(stream, new SpeechAudioFormatInfo(24000, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
                    synth.Speak("English"); pcm = stream.ToArray();
                }
                File.WriteAllBytes(prefix + "-source.pcm", pcm);
                var enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumerator();
                AudioInterop.IMMDevice device; AudioInterop.Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                object value; Guid iid = typeof(AudioInterop.IAudioClient).GUID;
                AudioInterop.Check(device.Activate(ref iid, 23, IntPtr.Zero, out value)); var client = (AudioInterop.IAudioClient)value;
                var format = AudioInterop.PcmFormat(); Guid session = Guid.Empty;
                AudioInterop.Check(client.Initialize(0, 0x88020000, 1000000, 0, ref format, ref session));
                iid = typeof(CaptureClient).GUID; AudioInterop.Check(client.GetService(ref iid, out value)); var capture = (CaptureClient)value;
                using (var output = new AudioOutput()) using (var recording = new MemoryStream())
                {
                    string failure = null;
                    output.Failed += delegate(string error) { failure = error; };
                    output.Started += delegate { Console.WriteLine("Started"); };
                    AudioInterop.Check(client.Start());
                    output.Play(output.Stop(), "English", pcm, 100);
                    long start = Environment.TickCount; bool replayed = false;
                    while (unchecked(Environment.TickCount - start) < 6500)
                    {
                        uint frames; AudioInterop.Check(capture.GetNextPacketSize(out frames));
                        while (frames > 0)
                        {
                            IntPtr data; uint flags; ulong position, counter;
                            AudioInterop.Check(capture.GetBuffer(out data, out frames, out flags, out position, out counter));
                            var bytes = new byte[frames * 2]; if ((flags & 2) == 0) Marshal.Copy(data, bytes, 0, bytes.Length);
                            recording.Write(bytes, 0, bytes.Length); AudioInterop.Check(capture.ReleaseBuffer(frames));
                            AudioInterop.Check(capture.GetNextPacketSize(out frames));
                        }
                        if (!replayed && unchecked(Environment.TickCount - start) > 3500) { output.Play(output.Stop(), "English", pcm, 100); replayed = true; }
                        Thread.Sleep(5);
                    }
                    client.Stop(); File.WriteAllBytes(prefix + "-loopback.pcm", recording.ToArray());
                    if (failure != null) throw new Exception(failure);
                    Console.WriteLine("Loopback frames: " + recording.Length / 2);
                }
                AudioInterop.Release(capture); AudioInterop.Release(client); AudioInterop.Release(device); AudioInterop.Release(enumerator);
                return 0;
            }
            catch (Exception error) { Console.Error.WriteLine(error); return 1; }
        }
    }
}
