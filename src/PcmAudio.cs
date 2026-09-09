using System;
using System.IO;
using System.Text;

namespace PointCursor
{
    // One format for both engines: mono, 24 kHz, signed 16-bit PCM. Never trim onsets.
    internal static class PcmAudio
    {
        public const int SampleRate = 24000;
        public static byte[] ReadWave(string path)
        {
            using (var stream = File.OpenRead(path))
            using (var reader = new BinaryReader(stream, Encoding.ASCII))
            {
                if (stream.Length < 44 || new string(reader.ReadChars(4)) != "RIFF") throw new InvalidDataException("Invalid WAV header.");
                uint length = reader.ReadUInt32();
                if ((long)length + 8 != stream.Length || new string(reader.ReadChars(4)) != "WAVE") throw new InvalidDataException("Incomplete WAV file.");
                bool format = false;
                while (stream.Position + 8 <= stream.Length)
                {
                    string id = new string(reader.ReadChars(4)); uint size = reader.ReadUInt32();
                    long next = stream.Position + size + (size & 1);
                    if (next > stream.Length) throw new InvalidDataException("Truncated WAV chunk.");
                    if (id == "fmt ")
                    {
                        if (size < 16) throw new InvalidDataException("Invalid WAV format.");
                        ushort tag = reader.ReadUInt16(), channels = reader.ReadUInt16(); uint rate = reader.ReadUInt32();
                        format = tag == 1 && channels == 1 && rate == SampleRate;
                        uint byteRate = reader.ReadUInt32(); ushort align = reader.ReadUInt16(), bits = reader.ReadUInt16();
                        format = format && byteRate == SampleRate * 2 && align == 2 && bits == 16;
                    }
                    else if (id == "data")
                    {
                        if (!format || size == 0 || size > SampleRate * 2 * 30 || size % 2 != 0) throw new InvalidDataException("Expected bounded mono 24 kHz PCM16 audio.");
                        return reader.ReadBytes((int)size);
                    }
                    stream.Position = next;
                }
                throw new InvalidDataException("WAV data missing.");
            }
        }
    }

    // The renderer consumes every source sample from offset zero, including soft consonants
    // and original leading silence. Cancellation replaces the remaining samples, never seeks.
    internal sealed class PcmPlaybackBuffer
    {
        private readonly object sync = new object();
        private byte[] pcm;
        private int cursor, volume, warmup;
        private long generation;
        private bool announced;
        public long Generation { get { lock (sync) return generation; } }
        public long Cancel() { lock (sync) { pcm = null; cursor = 0; announced = false; return ++generation; } }
        public void DeviceOpened(int frames) { lock (sync) { warmup = frames; cursor = 0; announced = false; } }
        public bool Set(long ticket, byte[] samples, int gain)
        {
            if (samples == null || samples.Length == 0 || samples.Length % 2 != 0) throw new ArgumentException("Invalid PCM audio.");
            lock (sync)
            {
                if (ticket != generation) return false;
                pcm = samples; cursor = 0; volume = Math.Max(0, Math.Min(100, gain)); announced = false; return true;
            }
        }
        public long Fill(byte[] target, int frames)
        {
            Array.Clear(target, 0, frames * 2);
            lock (sync)
            {
                int at = Math.Min(warmup, frames); warmup -= at;
                if (pcm == null || at == frames) return -1;
                long started = announced ? -1 : generation; announced = true;
                int available = Math.Min((frames - at) * 2, pcm.Length - cursor);
                if (volume == 100) Buffer.BlockCopy(pcm, cursor, target, at * 2, available);
                else for (int i = 0; i < available; i += 2)
                {
                    short original = (short)(pcm[cursor + i] | pcm[cursor + i + 1] << 8);
                    short value = (short)(original * volume / 100);
                    target[at * 2 + i] = (byte)value; target[at * 2 + i + 1] = (byte)(value >> 8);
                }
                cursor += available;
                if (cursor == pcm.Length) { pcm = null; cursor = 0; }
                return started;
            }
        }
    }
}
