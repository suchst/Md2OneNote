using System;
using System.Collections.Generic;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    /// <summary>
    /// Keys for every string this layer can put in front of a user. Centralized so that adding a
    /// message without a translation is visible (NFR-11).
    /// </summary>
    public static class StringKeys
    {
        public const string ProgressReading = "Progress.Reading";
        public const string ProgressParsing = "Progress.Parsing";
        public const string ProgressDiagrams = "Progress.Diagrams";
        public const string ProgressImages = "Progress.Images";
        public const string ProgressCreatingPage = "Progress.CreatingPage";
        public const string ProgressWriting = "Progress.Writing";

        public const string DiagramNoRenderer = "Diagram.NoRenderer";
        public const string DiagramLimitExceeded = "Diagram.LimitExceeded";
        public const string DiagramRendererFaulted = "Diagram.RendererFaulted";

        public const string FailureUnreadable = "Failure.Unreadable";
        public const string FailureTooLarge = "Failure.TooLarge";
        public const string FailureParseError = "Failure.ParseError";
        public const string FailureSectionUnavailable = "Failure.SectionUnavailable";
        public const string FailureOneNoteBusy = "Failure.OneNoteBusy";
        public const string FailureInternal = "Failure.Internal";
        public const string FailurePageTooLarge = "Failure.PageTooLarge";

        public const string SkippedUnchanged = "Skipped.Unchanged";
        public const string UntitledDocument = "Document.Untitled";
    }

    /// <summary>
    /// Built-in English strings, used when no localized catalog is supplied. Never throws and
    /// never returns null: an unknown key yields the key itself, which is ugly in the UI but
    /// cannot fail an import.
    /// </summary>
    public sealed class FallbackStringCatalog : IStringCatalog
    {
        private static readonly Dictionary<string, string> Strings = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { StringKeys.ProgressReading, "Reading file..." },
            { StringKeys.ProgressParsing, "Converting Markdown..." },
            { StringKeys.ProgressDiagrams, "Rendering diagrams..." },
            { StringKeys.ProgressImages, "Loading images..." },
            { StringKeys.ProgressCreatingPage, "Creating page..." },
            { StringKeys.ProgressWriting, "Writing page content..." },

            { StringKeys.DiagramNoRenderer, "No renderer is available for '{0}' diagrams." },
            { StringKeys.DiagramLimitExceeded, "This document exceeds the limit of {0} diagrams; the remainder are shown as code." },
            { StringKeys.DiagramRendererFaulted, "The diagram renderer failed: {0}" },

            { StringKeys.FailureUnreadable, "The file could not be read: {0}" },
            { StringKeys.FailureTooLarge, "The file is larger than the {0} byte limit." },
            { StringKeys.FailureParseError, "The Markdown could not be converted: {0}" },
            { StringKeys.FailureSectionUnavailable, "No OneNote section is currently open, so there is nowhere to create pages." },
            { StringKeys.FailureOneNoteBusy, "OneNote stayed busy and did not accept the change." },
            { StringKeys.FailureInternal, "An unexpected error occurred: {0}" },
            { StringKeys.FailurePageTooLarge, "The generated page is larger than the {0} byte limit and was not created." },

            { StringKeys.SkippedUnchanged, "Unchanged since the last import." },
            { StringKeys.UntitledDocument, "Untitled" },

            // Emitted by Core, into page content and conversion diagnostics.
            { CoreStringKeys.FootnotesHeading, "Notes" },
            { CoreStringKeys.ImageUnavailable, "[Image unavailable: {0} — {1}]" },
            { CoreStringKeys.HtmlBlockDropped, "Embedded HTML on line {0} was removed for safety." },
            { CoreStringKeys.NestingTooDeep, "Content nested more than {0} levels deep was not converted." },

            { CoreStringKeys.AssetInvalidPath, "'{0}' is not a usable file path." },
            { CoreStringKeys.AssetRemoteUrl, "'{0}' is a web address; only local images are embedded." },
            { CoreStringKeys.AssetAbsolutePath, "'{0}' is an absolute path and was not embedded." },
            { CoreStringKeys.AssetNetworkPath, "'{0}' is a network path and was not embedded." },
            { CoreStringKeys.AssetUnsupportedType, "'{0}' is not a supported image type." },
            { CoreStringKeys.AssetOutsideDocument, "'{0}' is outside the document's folder." },
            { CoreStringKeys.AssetTooLarge, "'{0}' is larger than the {1} byte limit." },
            { CoreStringKeys.AssetNotAnImage, "'{0}' is not a readable image." },
            { CoreStringKeys.AssetNotFound, "'{0}' was not found." },
            { CoreStringKeys.AssetUnreadable, "'{0}' could not be read: {1}" }
        };

        public static FallbackStringCatalog Instance { get; } = new FallbackStringCatalog();

        public string Get(string key)
        {
            if (string.IsNullOrEmpty(key))
            {
                return string.Empty;
            }

            string value;
            return Strings.TryGetValue(key, out value) ? value : key;
        }
    }
}
