using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Threading;
using Md2OneNote.AddIn.Logging;
using Md2OneNote.Application;
using Md2OneNote.Core;
using Md2OneNote.Core.Parsing;
using Md2OneNote.Core.Rendering;
using Md2OneNote.Diagrams;
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
        /// <summary>Runs one import and returns the report to show the user.</summary>
        /// <param name="askReimport">
        /// Asked once, with the number of files skipped as unchanged, when there were any; true
        /// runs those files again as superseding pages. Null never asks.
        /// </param>
        /// <param name="ui">
        /// The progress window, or null for none; the log always receives progress as well.
        /// </param>
        /// <param name="cancellation">
        /// The user's Cancel. Pages already created are kept and reported (DESIGN.md §10).
        /// </param>
        public static string Run(
            OneNoteGateway gateway,
            string[] paths,
            string toolVersion,
            FileLogger log,
            Func<int, bool> askReimport,
            IImportProgress ui,
            CancellationToken cancellation)
        {
            var strings = FallbackStringCatalog.Instance;

            // The diagram renderer lives for one import (DESIGN.md §8.1) and only exists when the
            // WebView2 runtime does (§8.4). Without it, "mermaid" is not a diagram language at
            // all as far as the parser is concerned, so every fence stays a code block and the
            // report says why.
            var renderers = new List<IDiagramRenderer>();
            var languages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            WebViewDiagramRenderer browser = null;
            string note = null;

            var runtime = WebViewDiagramRenderer.AvailableRuntimeVersion();
            if (runtime != null)
            {
                browser = new WebViewDiagramRenderer();
                renderers.Add(browser);
                languages.Add("mermaid");
                log.Info("Diagram rendering via WebView2 runtime " + runtime);
            }
            else
            {
                note = "Diagram rendering is unavailable: the Microsoft Edge WebView2 runtime is not installed. "
                    + "Diagrams were imported as code.";
                log.Info("WebView2 runtime not found; diagrams fall back to code blocks.");
            }

            try
            {
                var service = new ImportService(
                    gateway,
                    new MarkdownDocumentParser(strings),
                    new OneNoteXmlRenderer(),
                    new SourceFileReader(),
                    new DiagramResolver(renderers, strings),
                    new AssetResolver(new AssetLoader(new FileBytes(), strings), strings),
                    new SectionListingPageIndex(gateway),
                    SystemClock.Instance,
                    strings);

                var options = ImportOptions.CreateDefault(toolVersion, new ParseOptions(languages));
                IImportProgress progress = new LoggingProgress(log);
                if (ui != null)
                {
                    progress = new FanOutProgress(progress, ui);
                }

                var summary = service
                    .ImportAsync(paths, options, progress, cancellation)
                    .GetAwaiter()
                    .GetResult();

                // Unchanged files were skipped (FR-19). The skip is cheap — nothing was parsed or
                // rendered — so asking afterwards costs nothing, and the second pass is just the
                // skipped files with the flag on. Not after a Cancel: the user asked to stop.
                var skipped = SkippedPaths(summary);
                if (skipped.Length > 0 && !cancellation.IsCancellationRequested
                    && askReimport != null && askReimport(skipped.Length))
                {
                    log.Info("Re-importing " + skipped.Length + " unchanged file(s) at the user's request.");
                    var again = service
                        .ImportAsync(skipped, options.WithReimportUnchanged(true), progress, cancellation)
                        .GetAwaiter()
                        .GetResult();
                    summary = Merge(summary, again);
                }

                return note == null ? Describe(summary) : Describe(summary) + "\r\n\r\n" + note;
            }
            finally
            {
                if (browser != null)
                {
                    browser.Dispose();
                }
            }
        }

        private static string[] SkippedPaths(ImportSummary summary)
        {
            var paths = new List<string>();
            foreach (var result in summary.Results)
            {
                if (result.Outcome == FileOutcome.Skipped)
                {
                    paths.Add(result.SourcePath);
                }
            }

            return paths.ToArray();
        }

        /// <summary>The first pass, with each skipped file's result replaced by its second-pass result.</summary>
        private static ImportSummary Merge(ImportSummary first, ImportSummary second)
        {
            var byPath = new Dictionary<string, FileResult>(StringComparer.OrdinalIgnoreCase);
            foreach (var result in second.Results)
            {
                byPath[result.SourcePath] = result;
            }

            var merged = new List<FileResult>();
            foreach (var result in first.Results)
            {
                FileResult replacement;
                merged.Add(result.Outcome == FileOutcome.Skipped && byPath.TryGetValue(result.SourcePath, out replacement)
                    ? replacement
                    : result);
            }

            return new ImportSummary(merged);
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

        /// <summary>Delivers each progress call to several sinks; each is guarded on its own.</summary>
        private sealed class FanOutProgress : IImportProgress
        {
            private readonly IImportProgress[] _sinks;

            public FanOutProgress(params IImportProgress[] sinks)
            {
                _sinks = sinks;
            }

            public void FileStarted(int index, int total, string path)
            {
                Each(s => s.FileStarted(index, total, path));
            }

            public void Step(string localizedMessage)
            {
                Each(s => s.Step(localizedMessage));
            }

            public void FileFinished(FileResult result)
            {
                Each(s => s.FileFinished(result));
            }

            private void Each(Action<IImportProgress> call)
            {
                foreach (var sink in _sinks)
                {
                    try
                    {
                        call(sink);
                    }
                    catch (Exception)
                    {
                        // The contract: a progress sink never fails an import.
                    }
                }
            }
        }

        /// <summary>
        /// Progress in the log, always: it is what a bug report carries, and a logger that
        /// swallows its own failures is a sink that can promise never to throw.
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
