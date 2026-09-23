using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace PointCursor
{
    internal sealed class SpeechVoice
    {
        public readonly string Name, Culture;
        public readonly bool Enabled;
        public SpeechVoice(string name, string culture, bool enabled)
        { Name = name; Culture = culture; Enabled = enabled; }
        public override string ToString() { return (Culture == "en-GB" ? "英式" : "美式") + " · " + Name; }
    }

    internal sealed class VoiceCatalog
    {
        public readonly ReadOnlyCollection<SpeechVoice> Voices;
        public VoiceCatalog(IEnumerable<SpeechVoice> installed)
        {
            // Filter by exact locale: other English accents are outside the product scope.
            Voices = Array.AsReadOnly(installed.Where(v => v.Enabled && !String.IsNullOrWhiteSpace(v.Name)
                && (v.Culture == "en-US" || v.Culture == "en-GB"))
                .OrderBy(v => v.Culture == "en-US" ? 0 : 1).ThenBy(v => v.Name, StringComparer.Ordinal).ToArray());
        }
        public SpeechVoice Find(string name) { return Voices.FirstOrDefault(v => v.Name == name); }
        public SpeechVoice Resolve(string preferred)
        { return Find(preferred) ?? Find(AppSettings.DefaultVoice) ?? Voices.FirstOrDefault(); }
    }
}
