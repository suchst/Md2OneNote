using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Path rules for embedding referenced images (DESIGN.md §9).
    /// </summary>
    public sealed class AssetPolicy
    {
        public AssetPolicy(long maxBytes, bool allowParentEscape, ISet<string> allowedExtensions)
        {
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            if (allowedExtensions == null) throw new ArgumentNullException(nameof(allowedExtensions));

            MaxBytes = maxBytes;
            AllowParentEscape = allowParentEscape;
            AllowedExtensions = allowedExtensions;
        }

        public long MaxBytes { get; }

        /// <summary>
        /// Whether "../shared/img.png" may resolve outside the document's directory. Allowed by
        /// default with a warning: the layout is legitimate and common, and the genuinely
        /// dangerous cases (absolute and UNC paths) are rejected regardless of this flag.
        /// </summary>
        public bool AllowParentEscape { get; }

        public ISet<string> AllowedExtensions { get; }

        public static AssetPolicy Default
        {
            get
            {
                var extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
                {
                    ".png", ".jpg", ".jpeg", ".gif"
                };

                return new AssetPolicy(16L * 1024 * 1024, true, extensions);
            }
        }
    }

    /// <summary>
    /// Pass 2 for images. Never throws for a rejected or missing file — the rejection is an
    /// outcome, rendered as a placeholder plus a warning (FR-10).
    /// </summary>
    public interface IAssetSource
    {
        AssetOutcome Load(AssetRequest request, string baseDirectory, AssetPolicy policy);
    }
}
