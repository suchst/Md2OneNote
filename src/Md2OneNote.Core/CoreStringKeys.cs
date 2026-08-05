namespace Md2OneNote.Core
{
    /// <summary>
    /// Keys for the strings Core puts in front of a user — both those written *into page content*
    /// and those attached to diagnostics. They are localized like any other user-visible text
    /// (NFR-11); they live here rather than in the application layer because Core is what emits
    /// them.
    /// </summary>
    public static class CoreStringKeys
    {
        /// <summary>Heading above the footnote definitions. IMPLEMENTATION.md §7 hardcodes "Notes".</summary>
        public const string FootnotesHeading = "Core.FootnotesHeading";

        /// <summary>
        /// Placeholder shown where an image could not be embedded. Takes the alt text or path as
        /// {0} and the reason as {1}. A missing image is always visible, never a silent gap (FR-10).
        /// </summary>
        public const string ImageUnavailable = "Core.ImageUnavailable";

        /// <summary>Warning raised when raw HTML was removed. Takes the source line as {0}.</summary>
        public const string HtmlBlockDropped = "Core.HtmlBlockDropped";

        /// <summary>Warning raised when content nested past the depth limit. Takes the limit as {0}.</summary>
        public const string NestingTooDeep = "Core.NestingTooDeep";
    }

    /// <summary>
    /// Stable, non-localized <see cref="Diagnostic.Code"/> values raised during conversion. Unlike
    /// the messages, these never change with the UI language, so they are what tests assert on.
    /// </summary>
    public static class DiagnosticCodes
    {
        /// <summary>A raw HTML block was dropped rather than passed through (NFR-4, DESIGN.md §6.4).</summary>
        public const string HtmlBlockDropped = "Html.BlockDropped";

        /// <summary>Content nested deeper than the converter will follow (NFR-8).</summary>
        public const string NestingTooDeep = "Structure.TooDeep";
    }
}
