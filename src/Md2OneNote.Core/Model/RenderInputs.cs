using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    /// <summary>
    /// The one:Meta entries written to every created page (FR-17).
    /// </summary>
    public sealed class PageMetadata
    {
        public const string SourceKey = "Md2OneNote.Source";
        public const string HashKey = "Md2OneNote.Hash";
        public const string VersionKey = "Md2OneNote.Version";
        public const string ImportedKey = "Md2OneNote.Imported";

        public PageMetadata(string sourcePath, string sourceHash, string toolVersion, DateTime importedUtc)
        {
            if (sourcePath == null) throw new ArgumentNullException(nameof(sourcePath));
            if (string.IsNullOrEmpty(sourceHash)) throw new ArgumentNullException(nameof(sourceHash));
            if (string.IsNullOrEmpty(toolVersion)) throw new ArgumentNullException(nameof(toolVersion));

            SourcePath = sourcePath;
            SourceHash = sourceHash;
            ToolVersion = toolVersion;
            ImportedUtc = importedUtc;
        }

        public string SourcePath { get; }

        public string SourceHash { get; }

        public string ToolVersion { get; }

        public DateTime ImportedUtc { get; }
    }

    /// <summary>
    /// Everything pass 3 needs beyond the parsed document. All resolution has already happened;
    /// rendering is a pure lookup by request key (DESIGN.md §3).
    /// </summary>
    /// <remarks>
    /// Deliberately carries no page id, unlike DESIGN.md §5.1. The id is an interop concern —
    /// <see cref="IOneNoteGateway.ReplacePageContent"/> stamps it on submission. Keeping it out
    /// of here means rendering does not depend on a page already existing, so every fallible step
    /// of an import can complete before anything is created in the user's notebook (NFR-1).
    /// </remarks>
    public sealed class RenderInputs
    {
        public RenderInputs(
            string title,
            PageMetadata metadata,
            IReadOnlyDictionary<string, DiagramOutcome> diagrams,
            IReadOnlyDictionary<string, AssetOutcome> assets,
            IStringCatalog strings)
        {
            if (string.IsNullOrEmpty(title)) throw new ArgumentNullException(nameof(title));
            if (metadata == null) throw new ArgumentNullException(nameof(metadata));
            if (diagrams == null) throw new ArgumentNullException(nameof(diagrams));
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            Title = title;
            Metadata = metadata;
            Diagrams = diagrams;
            Assets = assets;
            Strings = strings;
        }

        public string Title { get; }

        public PageMetadata Metadata { get; }

        public IReadOnlyDictionary<string, DiagramOutcome> Diagrams { get; }

        public IReadOnlyDictionary<string, AssetOutcome> Assets { get; }

        /// <summary>
        /// Strings emitted into page content, such as the footnote section heading. Passed in
        /// rather than read from resources so that Core stays pure and golden-file tests can pin
        /// an invariant catalog (NFR-11).
        /// </summary>
        public IStringCatalog Strings { get; }
    }
}
