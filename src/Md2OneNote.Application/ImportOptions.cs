using System;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    /// <summary>
    /// Bounds on the work a single import may do, so that a hostile or pathological document
    /// fails within a bounded time rather than hanging OneNote (NFR-8).
    /// </summary>
    public sealed class ImportLimits
    {
        public ImportLimits(long maxSourceBytes, int maxDiagramsPerDocument, long maxPageBytes)
        {
            if (maxSourceBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxSourceBytes));
            if (maxDiagramsPerDocument <= 0) throw new ArgumentOutOfRangeException(nameof(maxDiagramsPerDocument));
            if (maxPageBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxPageBytes));

            MaxSourceBytes = maxSourceBytes;
            MaxDiagramsPerDocument = maxDiagramsPerDocument;
            MaxPageBytes = maxPageBytes;
        }

        public long MaxSourceBytes { get; }

        /// <summary>
        /// Counted after deduplication. Diagrams beyond the limit are not rendered; they fall
        /// back to code blocks with an explanation, which keeps the import completing (FR-14).
        /// </summary>
        public int MaxDiagramsPerDocument { get; }

        /// <summary>
        /// Ceiling on the generated page XML. Base64 images inflate the payload substantially,
        /// and a page large enough to hurt OneNote is better refused than written.
        /// </summary>
        public long MaxPageBytes { get; }

        public static ImportLimits Default
        {
            get { return new ImportLimits(8L * 1024 * 1024, 100, 64L * 1024 * 1024); }
        }
    }

    public sealed class ImportOptions
    {
        public ImportOptions(
            string toolVersion,
            ParseOptions parseOptions,
            AssetPolicy assetPolicy,
            ImportLimits limits,
            string supersedingTitleFormat,
            bool navigateToFirstCreatedPage)
            : this(toolVersion, parseOptions, assetPolicy, limits, supersedingTitleFormat, navigateToFirstCreatedPage, false)
        {
        }

        public ImportOptions(
            string toolVersion,
            ParseOptions parseOptions,
            AssetPolicy assetPolicy,
            ImportLimits limits,
            string supersedingTitleFormat,
            bool navigateToFirstCreatedPage,
            bool reimportUnchanged)
        {
            if (string.IsNullOrEmpty(toolVersion)) throw new ArgumentNullException(nameof(toolVersion));
            if (parseOptions == null) throw new ArgumentNullException(nameof(parseOptions));
            if (assetPolicy == null) throw new ArgumentNullException(nameof(assetPolicy));
            if (limits == null) throw new ArgumentNullException(nameof(limits));
            if (string.IsNullOrEmpty(supersedingTitleFormat)) throw new ArgumentNullException(nameof(supersedingTitleFormat));

            ToolVersion = toolVersion;
            ParseOptions = parseOptions;
            AssetPolicy = assetPolicy;
            Limits = limits;
            SupersedingTitleFormat = supersedingTitleFormat;
            NavigateToFirstCreatedPage = navigateToFirstCreatedPage;
            ReimportUnchanged = reimportUnchanged;
        }

        /// <summary>
        /// Import a file again even though it is unchanged since its last import into this
        /// section. Off by default (FR-19: unchanged files are skipped); the add-in turns it on
        /// for a second pass after asking the user. The page created is a superseding one, so the
        /// two copies stay tellable apart (FR-20).
        /// </summary>
        public bool ReimportUnchanged { get; }

        public ImportOptions WithReimportUnchanged(bool reimportUnchanged)
        {
            return new ImportOptions(
                ToolVersion, ParseOptions, AssetPolicy, Limits, SupersedingTitleFormat,
                NavigateToFirstCreatedPage, reimportUnchanged);
        }

        public string ToolVersion { get; }

        public ParseOptions ParseOptions { get; }

        public AssetPolicy AssetPolicy { get; }

        public ImportLimits Limits { get; }

        /// <summary>
        /// How a page that supersedes an earlier import is titled: {0} is the document title,
        /// {1} the local import time. FR-20 requires the two be distinguishable; the exact
        /// convention is OQ-1 and deliberately configurable until that is settled.
        /// </summary>
        public string SupersedingTitleFormat { get; }

        public bool NavigateToFirstCreatedPage { get; }

        public const string DefaultSupersedingTitleFormat = "{0} (imported {1:yyyy-MM-dd HH:mm})";

        public static ImportOptions CreateDefault(string toolVersion, ParseOptions parseOptions)
        {
            return new ImportOptions(
                toolVersion,
                parseOptions,
                AssetPolicy.Default,
                ImportLimits.Default,
                DefaultSupersedingTitleFormat,
                true);
        }
    }
}
