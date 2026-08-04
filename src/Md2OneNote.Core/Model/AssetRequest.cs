using System;

namespace Md2OneNote.Core
{
    /// <summary>
    /// An image the document references. The raw path is preserved exactly as written so that
    /// path policy decisions are made in one place (DESIGN.md §9), not scattered through parsing.
    /// </summary>
    public sealed class AssetRequest
    {
        public AssetRequest(string key, string rawPath, string altText)
        {
            if (string.IsNullOrEmpty(key)) throw new ArgumentNullException(nameof(key));
            if (rawPath == null) throw new ArgumentNullException(nameof(rawPath));

            Key = key;
            RawPath = rawPath;
            AltText = altText ?? string.Empty;
        }

        public string Key { get; }

        /// <summary>The path exactly as it appears in the Markdown. Never pre-resolved.</summary>
        public string RawPath { get; }

        public string AltText { get; }

        public static AssetRequest Create(string rawPath, string altText)
        {
            if (rawPath == null) throw new ArgumentNullException(nameof(rawPath));

            return new AssetRequest(Sha256.OfString(rawPath), rawPath, altText);
        }
    }
}
