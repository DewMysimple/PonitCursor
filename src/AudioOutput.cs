using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace PointCursor
{
    // Windows shared-mode WASAPI. The session stays running with silence between words,
    // preventing device reopen/power-up races on the first phoneme of each short utterance.
    internal sealed class AudioOutput : IDisposable
    {
        private readonly PcmPlaybackBuffer buffer = new PcmPlaybackBuffer();
        private readonly AutoResetEvent changed = new AutoResetEvent(false);
        private readonly object sync = new object();
        private Thread thread;
        private volatile bool disposed, suspended;
        private string word;
        private long ticket;
        public event Action<long, string> Started;
        public event Action<string> Failed;
        public long Generation { get { return buffer.Generation; } }
        public string DeviceId { get; private set; }
        public bool IsReady { get; private set; }
        public long Stop() { long value = buffer.Cancel(); if (!disposed) changed.Set(); return value; }
        public void Prepare()
        {
            lock (sync)
            {
                if (disposed) return;
                suspended = false;
                if (thread == null) { thread = new Thread(RenderLoop) { IsBackground = true, Name = "PointCursor audio" }; thread.SetApartmentState(ApartmentState.MTA); thread.Start(); }
                changed.Set();
            }
        }
        public void Suspend() { Stop(); suspended = true; changed.Set(); }
        public bool Play(long id, string text, byte[] pcm, int volume)
        {
            lock (sync)
            {
                if (disposed || id != Generation) return false;
                word = text; ticket = id;
                if (!buffer.Set(id, pcm, volume)) return false;
                Prepare(); changed.Set(); return true;
            }
        }
        private void RenderLoop()
        {
            while (!disposed)
            {
                if (suspended) { changed.WaitOne(250); continue; }
                AudioInterop.IMMDeviceEnumerator enumerator = null;
                AudioInterop.IMMDevice device = null;
                AudioInterop.IAudioClient client = null;
                AudioInterop.IAudioRenderClient render = null;
                using (var ready = new AutoResetEvent(false))
                {
                    try
                    {
                        enumerator = (AudioInterop.IMMDeviceEnumerator)new AudioInterop.MMDeviceEnumerator();
                        AudioInterop.Check(enumerator.GetDefaultAudioEndpoint(0, 1, out device));
                        string id; AudioInterop.Check(device.GetId(out id)); DeviceId = id;
                        Guid clientId = typeof(AudioInterop.IAudioClient).GUID; object value;
                        AudioInterop.Check(device.Activate(ref clientId, 23, IntPtr.Zero, out value)); client = (AudioInterop.IAudioClient)value;
                        var format = AudioInterop.PcmFormat(); Guid session = Guid.Empty;
                        AudioInterop.Check(client.Initialize(0, 0x88040000, 0, 0, ref format, ref session));
                        AudioInterop.Check(client.SetEventHandle(ready.SafeWaitHandle.DangerousGetHandle()));
                        uint capacity; AudioInterop.Check(client.GetBufferSize(out capacity));
                        Guid renderId = typeof(AudioInterop.IAudioRenderClient).GUID;
                        AudioInterop.Check(client.GetService(ref renderId, out value)); render = (AudioInterop.IAudioRenderClient)value;
                        var data = new byte[capacity * 2];
                        // A guard only when opening/reopening a device; normal words add no delay.
                        buffer.DeviceOpened(PcmAudio.SampleRate * 150 / 1000);
                        AudioInterop.Check(client.Start()); IsReady = true;
                        long lastDeviceCheck = Native.Now;
                        while (!disposed && !suspended)
                        {
                            WaitHandle.WaitAny(new WaitHandle[] { ready, changed }, 100);
                            uint padding; AudioInterop.Check(client.GetCurrentPadding(out padding));
                            uint count = capacity - Math.Min(capacity, padding);
                            if (count != 0)
                            {
                                IntPtr destination; AudioInterop.Check(render.GetBuffer(count, out destination));
                                long started = buffer.Fill(data, (int)count);
                                Marshal.Copy(data, 0, destination, (int)count * 2);
                                AudioInterop.Check(render.ReleaseBuffer(count, 0));
                                if (started >= 0)
                                {
                                    Action<long, string> notify = null; string text = null;
                                    lock (sync) { if (started == Generation && started == ticket) { notify = Started; text = word; } }
                                    if (notify != null) notify(started, text);
                                }
                            }
                            if (Native.Now - lastDeviceCheck > 1000)
                            {
                                lastDeviceCheck = Native.Now; AudioInterop.IMMDevice next = null;
                                try
                                {
                                    AudioInterop.Check(enumerator.GetDefaultAudioEndpoint(0, 1, out next));
                                    string nextId; AudioInterop.Check(next.GetId(out nextId)); if (nextId != id) break;
                                }
                                finally { AudioInterop.Release(next); }
                            }
                        }
                    }
                    catch (Exception)
                    {
                        if (!disposed && !suspended && Failed != null) Failed("音频输出暂不可用，请检查 Windows 默认播放设备。");
                        changed.WaitOne(1000);
                    }
                    finally
                    {
                        IsReady = false;
                        if (client != null) client.Stop();
                        AudioInterop.Release(render); AudioInterop.Release(client); AudioInterop.Release(device); AudioInterop.Release(enumerator);
                    }
                }
            }
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true; buffer.Cancel(); changed.Set();
            if (thread == null || thread.Join(3000)) changed.Dispose();
        }
    }

    internal static class AudioInterop
    {
        internal static void Check(int hr) { if (hr < 0) Marshal.ThrowExceptionForHR(hr); }
        internal static void Release(object value) { if (value != null && Marshal.IsComObject(value)) Marshal.ReleaseComObject(value); }
        [StructLayout(LayoutKind.Sequential, Pack = 2)] internal struct WaveFormat
        { public ushort Tag, Channels; public uint Rate, ByteRate; public ushort Align, Bits, Extra; }
        internal static WaveFormat PcmFormat() { return new WaveFormat { Tag = 1, Channels = 1, Rate = PcmAudio.SampleRate, ByteRate = PcmAudio.SampleRate * 2, Align = 2, Bits = 16 }; }
        [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")] internal class MMDeviceEnumerator { }
        [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMMDeviceEnumerator
        {
            [PreserveSig] int EnumAudioEndpoints(int flow, uint mask, out IntPtr devices);
            [PreserveSig] int GetDefaultAudioEndpoint(int flow, int role, out IMMDevice device);
            [PreserveSig] int GetDevice([MarshalAs(UnmanagedType.LPWStr)] string id, out IMMDevice device);
            [PreserveSig] int RegisterEndpointNotificationCallback(IntPtr callback);
            [PreserveSig] int UnregisterEndpointNotificationCallback(IntPtr callback);
        }
        [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IMMDevice
        {
            [PreserveSig] int Activate(ref Guid iid, uint context, IntPtr parameters, [MarshalAs(UnmanagedType.IUnknown)] out object value);
            [PreserveSig] int OpenPropertyStore(uint access, out IntPtr properties);
            [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
            [PreserveSig] int GetState(out uint state);
        }
        [ComImport, Guid("1CB9AD4C-DBFA-4C32-B178-C2F568A703B2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioClient
        {
            [PreserveSig] int Initialize(int share, uint flags, long duration, long period, ref WaveFormat format, ref Guid session);
            [PreserveSig] int GetBufferSize(out uint frames);
            [PreserveSig] int GetStreamLatency(out long latency);
            [PreserveSig] int GetCurrentPadding(out uint frames);
            [PreserveSig] int IsFormatSupported(int share, ref WaveFormat format, out IntPtr closest);
            [PreserveSig] int GetMixFormat(out IntPtr format);
            [PreserveSig] int GetDevicePeriod(out long normal, out long minimum);
            [PreserveSig] int Start();
            [PreserveSig] int Stop();
            [PreserveSig] int Reset();
            [PreserveSig] int SetEventHandle(IntPtr handle);
            [PreserveSig] int GetService(ref Guid iid, [MarshalAs(UnmanagedType.IUnknown)] out object service);
        }
        [ComImport, Guid("F294ACFC-3146-4483-A7BF-ADDCA7C260E2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        internal interface IAudioRenderClient
        {
            [PreserveSig] int GetBuffer(uint frames, out IntPtr data);
            [PreserveSig] int ReleaseBuffer(uint frames, uint flags);
        }
    }
}
