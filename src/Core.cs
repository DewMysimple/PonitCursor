using System;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml.Serialization;

namespace PointCursor
{
    public static class WordRules
    {
        private static readonly Regex Word = new Regex(@"\A[A-Za-z]+(?:['\u2019-][A-Za-z]+)*\z", RegexOptions.CultureInvariant);
        // Explicit surrounding punctuation: do not turn URLs, paths, code, or email into words.
        private static readonly char[] Edges = " \t\r\n.,!?;:\"'\u2018\u2019\u201c\u201d()[]{}<>\u300a\u300b\u3002\uff0c\uff01\uff1f\uff1b\uff1a".ToCharArray();
        public static string Normalize(string input)
        {
            if (String.IsNullOrWhiteSpace(input) || input.Length > 128) return null;
            string text = input.Trim(Edges);
            return text.Length > 0 && text.Length <= 64 && Word.IsMatch(text) ? text.Replace('\u2019', '\'') : null;
        }
    }

    public sealed class SelectionGate
    {
        private long generation;
        private long spoken = -1;
        public long Current { get { return generation; } }
        public long Invalidate() { return ++generation; }
        public bool TryAccept(long ticket)
        {
            if (ticket != generation || spoken == ticket) return false;
            spoken = ticket;
            return true;
        }
    }

    public sealed class CopyGate
    {
        private IntPtr window;
        private uint sequence;
        private long expires;
        private bool armed;
        public void Arm(IntPtr source, uint before, long now)
        { window = source; sequence = before; expires = now + 1200; armed = true; }
        public void Reset() { armed = false; }
        public bool TryConsume(IntPtr source, uint after, long now)
        {
            if (!armed) return false;
            if (now > expires || window != source) { armed = false; return false; }
            if (after == sequence) return false;
            armed = false;
            return true;
        }
    }

    public sealed class AppSettings
    {
        public bool CopyToSpeak = true;
        public int Rate = -1;
        public int Volume = 85;
        public string Voice = "Microsoft Zira Desktop";
        public static string FilePath { get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "PointCursor", "settings.xml"); } }
        public void Validate()
        {
            Rate = Math.Max(-5, Math.Min(5, Rate));
            Volume = Math.Max(0, Math.Min(100, Volume));
            if (Voice == null) Voice = "";
        }
        public static AppSettings Load(string path, out bool reset)
        {
            reset = false;
            try
            {
                if (!File.Exists(path)) return new AppSettings();
                using (var reader = System.Xml.XmlReader.Create(path, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = 16384 }))
                {
                    var value = (AppSettings)new XmlSerializer(typeof(AppSettings)).Deserialize(reader);
                    value.Validate(); return value;
                }
            }
            catch (Exception ex)
            {
                if (!(ex is IOException) && !(ex is InvalidOperationException) && !(ex is UnauthorizedAccessException) && !(ex is System.Xml.XmlException)) throw;
                reset = true; return new AppSettings();
            }
        }
        public bool Save(string path)
        {
            try
            {
                Validate(); Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
                string temp = path + ".tmp";
                using (var stream = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                    new XmlSerializer(typeof(AppSettings)).Serialize(stream, this);
                if (File.Exists(path)) File.Replace(temp, path, null); else File.Move(temp, path);
                return true;
            }
            catch (IOException) { return false; }
            catch (UnauthorizedAccessException) { return false; }
        }
    }
}
