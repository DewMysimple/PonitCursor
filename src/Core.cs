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
        private static readonly Regex HighlightedWord = new Regex(@"==(?<word>[A-Za-z]+(?:['\u2019-][A-Za-z]+)*)==", RegexOptions.CultureInvariant);
        // Explicit surrounding punctuation: do not turn URLs, paths, code, or email into words.
        private static readonly char[] Edges = " \t\r\n.,!?;:\"'\u2018\u2019\u201c\u201d()[]{}<>\u300a\u300b\u3002\uff0c\uff01\uff1f\uff1b\uff1a".ToCharArray();
        public static string Normalize(string input)
        {
            if (String.IsNullOrWhiteSpace(input) || input.Length > 128) return null;
            string text = input.Trim(Edges);
            return text.Length > 0 && text.Length <= 64 && Word.IsMatch(text) ? text.Replace('\u2019', '\'') : null;
        }
        public static string NormalizeGesture(string input, string context)
        {
            // Obsidian may expose Markdown markers during the same accessibility
            // query that follows a completed selection gesture.
            if (!String.IsNullOrWhiteSpace(input) && input.Length <= 128 && !String.IsNullOrEmpty(context) && context.Length <= 2048)
            {
                string fragment = Normalize(input.Trim(Edges).Trim('='));
                if (fragment != null)
                {
                    foreach (Match match in HighlightedWord.Matches(context))
                    {
                        string candidate = Normalize(match.Groups["word"].Value);
                        if (candidate != null && (String.Equals(candidate, fragment, StringComparison.Ordinal)
                            || (candidate.Length > fragment.Length && candidate.Length - fragment.Length <= 2
                                && (candidate.StartsWith(fragment, StringComparison.Ordinal) || candidate.EndsWith(fragment, StringComparison.Ordinal)))))
                            return candidate;
                    }
                }
            }
            return Normalize(input);
        }
        public static string ReconcileGesture(string input, string context, string enclosing)
        {
            // In source mode the newly inserted leading "==" moves the old screen
            // endpoints into the selected word. Prefer the enclosing accessible word
            // when the raw fragment is compatible; never crop a multi-word range.
            string selected = NormalizeGesture(input, context);
            if (selected == null && !String.IsNullOrWhiteSpace(input) && input.Length <= 128)
                selected = Normalize(input.Trim(Edges).Trim('='));
            string candidate = Normalize(enclosing);
            if (selected != null && candidate != null && candidate.Length >= selected.Length
                && (candidate.Length - selected.Length <= 2 || String.Equals(Normalize(context), candidate, StringComparison.Ordinal))
                && (candidate.StartsWith(selected, StringComparison.Ordinal) || candidate.EndsWith(selected, StringComparison.Ordinal)))
                return candidate;
            return selected;
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
