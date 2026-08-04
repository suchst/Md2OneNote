using System;

namespace Md2OneNote.Core
{
    /// <summary>
    /// A diagram the document needs but the parser cannot produce. Emitted by pass 1 and
    /// satisfied by pass 2 (DESIGN.md §3).
    /// </summary>
    public sealed class DiagramRequest
    {
        public DiagramRequest(string key, string language, string source)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (string.IsNullOrEmpty(language)) throw new ArgumentNullException(nameof(language));
            if (source == null) throw new ArgumentNullException(nameof(source));

            Key = key;
            Language = language;
            Source = source;
        }

        /// <summary>
        /// Stable identity of the request. Equal language and source produce an equal key, which
        /// is what makes deduplication and caching possible (NFR-20).
        /// </summary>
        public string Key { get; }

        public string Language { get; }

        public string Source { get; }

        public static DiagramRequest Create(string language, string source)
        {
            if (string.IsNullOrEmpty(language)) throw new ArgumentNullException(nameof(language));
            if (source == null) throw new ArgumentNullException(nameof(source));

            var key = Sha256.OfString(language + "\0" + source);
            return new DiagramRequest(key, language, source);
        }
    }
}
