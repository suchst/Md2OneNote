using System;
using System.Globalization;
using System.Text;
using System.Threading;
using Md2OneNote.AddIn.Logging;
using Md2OneNote.Application;
using Md2OneNote.Core;
using Md2OneNote.Core.Parsing;
using Md2OneNote.Core.Rendering;
using Md2OneNote.Interop;
using Md2OneNote.Storage;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// The composition root: the one place that knows which concrete parser, renderer, reader,
    /// asset source and index make up an import (DESIGN.md §4).
    /// </summary>
    /// <remarks>
    /// <b>The import runs synchronously on the ribbon callback's thread.</b> That thread is the
    /// surrogate's STA, the only apartment the <c>Application</c> proxy may be called from, and
    /// with no diagram renderers registered <c>ImportAsync</c> never actually yields — every
    /// await completes synchronously. When Phase 4 adds WebView2 rendering this needs a message
    /// pump and marshalling back to this thread (IMPLEMENTATION.md §13); until then blocking is
    /// both correct and the simplest thing that is.
    /// </remarks>
    internal static class ImportComposition
    {
        public static ImportSummary Run(OneNoteGateway gateway, string[] paths, string toolVersion, FileLogger log)
        {
            var strings = FallbackStringCatalog.Instance;

            var service = new ImportService(
                gateway,
                new MarkdownDocumentParser(strings),
                new OneNoteXmlRenderer(),
                new SourceFileReader(),
                new DiagramResolver(new IDiagramRenderer[0], strings),
                new AssetResolver(new AssetLoader(new FileBytes(), strings), strings),
                new SectionListingPageIndex(gateway),
                SystemClock.Instance,
                strings);

            var options = ImportOptions.CreateDefault(toolVersion, ParseOptions.None);

            return service
                .ImportAsync(paths, options, new LoggingProgress(log), CancellationToken.None)
                .GetAwaiter()
                .GetResult();
        }

        /// <summary>The end-of-import report as one message box (FR-4).</summary>
        public static string Describe(ImportSummary summary)
        {
            var text = new StringBuilder();
            text.AppendFormat(
                CultureInfo.CurrentCulture,
                "Created {0}, skipped {1}, failed {2}.",
                summary.CreatedCount,
                summary.SkippedCount,
                summary.FailedCount);

            foreach (var result in summary.Results)
            {
                if (result.Outcome == FileOutcome.Created && result.Warnings.Count == 0)
                {
                    continue;
                }

                text.AppendLine().AppendLine();
                text.Append(System.IO.Path.GetFileName(result.SourcePath)).Append(": ");
                text.Append(Outcome(result));

                foreach (var warning in result.Warnings)
                {
                    text.AppendLine().Append("  - ").Append(warning.Message);
                }
            }

            return text.ToString();
        }

        private static string Outcome(FileResult result)
        {
            switch (result.Outcome)
            {
                case FileOutcome.Created: return "created";
                case FileOutcome.CreatedSuperseding: return "created (supersedes an earlier import)";
                case FileOutcome.Skipped: return "skipped, " + result.Message;
                case FileOutcome.Failed: return "failed, " + result.Message;
                case FileOutcome.Cancelled: return "cancelled";
                default: return result.Outcome.ToString();
            }
        }

        /// <summary>
        /// Progress goes to the log for now. A modeless progress form is IMPLEMENTATION.md §9.3's
        /// ask and arrives with multi-file polish; the contract says never throw, and a logger
        /// that swallows its own failures is the one sink that can promise that today.
        /// </summary>
        private sealed class LoggingProgress : IImportProgress
        {
            private readonly FileLogger _log;

            public LoggingProgress(FileLogger log)
            {
                _log = log;
            }

            public void FileStarted(int index, int total, string path)
            {
                _log.Info(string.Format(CultureInfo.InvariantCulture, "Import {0}/{1}: {2}", index + 1, total, path));
            }

            public void Step(string localizedMessage)
            {
                _log.Info("  " + localizedMessage);
            }

            public void FileFinished(FileResult result)
            {
                _log.Info("  -> " + result.Outcome + (result.Message == null ? string.Empty : ": " + result.Message));
            }
        }
    }
}
