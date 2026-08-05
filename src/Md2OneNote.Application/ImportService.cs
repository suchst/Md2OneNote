using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    /// <summary>
    /// Orchestrates an import: for each file, decide, convert, resolve, and create (DESIGN.md §4).
    /// </summary>
    /// <remarks>
    /// Two properties matter more than anything else here.
    /// <para>
    /// Files are isolated. A failure on one produces a <see cref="FileResult"/> and the loop
    /// continues, so one bad document cannot cost the user the other fourteen (FR-3).
    /// </para>
    /// <para>
    /// Everything fallible happens before anything is created. Reading, hashing, parsing,
    /// diagram rendering, image loading, XML generation and the size check all complete before
    /// the first call into the notebook, so a failure mid-build leaves no page behind (NFR-1).
    /// </para>
    /// </remarks>
    public sealed class ImportService
    {
        private readonly IOneNoteGateway _gateway;
        private readonly IMarkdownDocumentParser _parser;
        private readonly IOneNoteXmlRenderer _renderer;
        private readonly ISourceFileReader _reader;
        private readonly DiagramResolver _diagrams;
        private readonly AssetResolver _assets;
        private readonly IPageIndex _index;
        private readonly IClock _clock;
        private readonly IStringCatalog _strings;

        public ImportService(
            IOneNoteGateway gateway,
            IMarkdownDocumentParser parser,
            IOneNoteXmlRenderer renderer,
            ISourceFileReader reader,
            DiagramResolver diagrams,
            AssetResolver assets,
            IPageIndex index,
            IClock clock,
            IStringCatalog strings)
        {
            if (gateway == null) throw new ArgumentNullException(nameof(gateway));
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            if (renderer == null) throw new ArgumentNullException(nameof(renderer));
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            if (diagrams == null) throw new ArgumentNullException(nameof(diagrams));
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (index == null) throw new ArgumentNullException(nameof(index));
            if (clock == null) throw new ArgumentNullException(nameof(clock));
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            _gateway = gateway;
            _parser = parser;
            _renderer = renderer;
            _reader = reader;
            _diagrams = diagrams;
            _assets = assets;
            _index = index;
            _clock = clock;
            _strings = strings;
        }

        public async Task<ImportSummary> ImportAsync(
            IReadOnlyList<string> filePaths,
            ImportOptions options,
            IImportProgress progress,
            CancellationToken ct)
        {
            if (filePaths == null) throw new ArgumentNullException(nameof(filePaths));
            if (options == null) throw new ArgumentNullException(nameof(options));
            if (progress == null) throw new ArgumentNullException(nameof(progress));

            var results = new List<FileResult>(filePaths.Count);

            SectionRef section;
            try
            {
                section = _gateway.GetActiveSection();
            }
            catch (NoActiveSectionException)
            {
                // Nothing is being viewed, so there is no defensible place to put pages. Refuse
                // the whole import rather than guessing a fallback location (FR-6).
                var message = _strings.Get(StringKeys.FailureSectionUnavailable);
                foreach (var path in filePaths)
                {
                    results.Add(FileResult.Failed(path, FailureReason.SectionUnavailable, message));
                }

                return new ImportSummary(results);
            }

            for (var i = 0; i < filePaths.Count; i++)
            {
                var path = filePaths[i];

                if (ct.IsCancellationRequested)
                {
                    results.Add(FileResult.Cancelled(path));
                    continue;
                }

                progress.FileStarted(i, filePaths.Count, path);

                FileResult result;
                try
                {
                    result = await ImportOneAsync(path, section, options, progress, ct).ConfigureAwait(true);
                }
                catch (OperationCanceledException)
                {
                    result = FileResult.Cancelled(path);
                }

                results.Add(result);
                progress.FileFinished(result);
            }

            var summary = new ImportSummary(results);
            NavigateIfRequested(summary, options);
            return summary;
        }

        private async Task<FileResult> ImportOneAsync(
            string path,
            SectionRef section,
            ImportOptions options,
            IImportProgress progress,
            CancellationToken ct)
        {
            SourceFile file;
            try
            {
                progress.Step(_strings.Get(StringKeys.ProgressReading));
                file = _reader.Read(path, options.Limits.MaxSourceBytes);
            }
            catch (SourceFileTooLargeException)
            {
                return FileResult.Failed(path, FailureReason.TooLarge, Format(
                    StringKeys.FailureTooLarge, options.Limits.MaxSourceBytes));
            }
            catch (Exception ex) when (IsFileAccessFailure(ex))
            {
                return FileResult.Failed(path, FailureReason.Unreadable, Format(
                    StringKeys.FailureUnreadable, ex.Message));
            }

            var hash = Sha256.OfBytes(file.Bytes);

            var match = _index.TryFind(section.Id, path);
            var decision = ImportDecider.Decide(match, hash);
            if (decision == ImportDecision.Skip)
            {
                return FileResult.Skipped(path, match.PageId, _strings.Get(StringKeys.SkippedUnchanged));
            }

            ParsedDocument parsed;
            try
            {
                progress.Step(_strings.Get(StringKeys.ProgressParsing));
                parsed = _parser.Parse(file.Text, options.ParseOptions);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return FileResult.Failed(path, FailureReason.ParseError, Format(
                    StringKeys.FailureParseError, ex.Message));
            }

            var diagrams = await _diagrams
                .ResolveAsync(parsed.Diagrams, options.Limits.MaxDiagramsPerDocument, progress, ct)
                .ConfigureAwait(true);

            var assets = _assets.Resolve(parsed.Assets, file.BaseDirectory, options.AssetPolicy, progress, ct);

            ct.ThrowIfCancellationRequested();

            var title = ResolveTitle(parsed.SuggestedTitle, path, decision, options);
            var metadata = new PageMetadata(path, hash, options.ToolVersion, _clock.UtcNow);
            var inputs = new RenderInputs(title, metadata, diagrams, assets, _strings);

            string xml;
            try
            {
                xml = _renderer.Render(parsed, inputs);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The renderer is contractually total — every failure it can encounter is already
                // encoded in the outcomes it was handed. Reaching here is a bug, not a bad
                // document, but it still must not cost the user the rest of the import.
                return FileResult.Failed(path, FailureReason.Internal, Format(
                    StringKeys.FailureInternal, ex.Message));
            }

            var pageBytes = Encoding.UTF8.GetByteCount(xml);
            if (pageBytes > options.Limits.MaxPageBytes)
            {
                return FileResult.Failed(path, FailureReason.TooLarge, Format(
                    StringKeys.FailurePageTooLarge, options.Limits.MaxPageBytes));
            }

            // Everything that can fail has now succeeded. From here on the notebook is touched.
            string pageId;
            try
            {
                progress.Step(_strings.Get(StringKeys.ProgressCreatingPage));
                pageId = _gateway.CreatePage(section.Id);

                progress.Step(_strings.Get(StringKeys.ProgressWriting));
                _gateway.ReplacePageContent(pageId, xml);
            }
            catch (OneNoteBusyException)
            {
                return FileResult.Failed(path, FailureReason.OneNoteBusy,
                    _strings.Get(StringKeys.FailureOneNoteBusy));
            }
            catch (OneNoteException ex)
            {
                return FileResult.Failed(path, FailureReason.Internal, Format(
                    StringKeys.FailureInternal, ex.Message));
            }

            RecordQuietly(section.Id, path, pageId, hash);

            var warnings = CollectWarnings(parsed, diagrams, assets);

            return decision == ImportDecision.CreateSuperseding
                ? FileResult.CreatedSuperseding(path, pageId, warnings)
                : FileResult.Created(path, pageId, warnings);
        }

        private string ResolveTitle(string suggested, string path, ImportDecision decision, ImportOptions options)
        {
            var title = suggested;
            if (string.IsNullOrWhiteSpace(title))
            {
                title = SafeFileNameWithoutExtension(path);
            }

            if (string.IsNullOrWhiteSpace(title))
            {
                title = _strings.Get(StringKeys.UntitledDocument);
            }

            if (decision != ImportDecision.CreateSuperseding)
            {
                return title;
            }

            // FR-20: a page that supersedes an earlier import has to be tellable apart from it.
            return string.Format(
                CultureInfo.CurrentCulture,
                options.SupersedingTitleFormat,
                title,
                _clock.UtcNow.ToLocalTime());
        }

        private static string SafeFileNameWithoutExtension(string path)
        {
            try
            {
                return Path.GetFileNameWithoutExtension(path);
            }
            catch (ArgumentException)
            {
                return null;
            }
        }

        private void RecordQuietly(string sectionId, string path, string pageId, string hash)
        {
            try
            {
                _index.Record(sectionId, path, pageId, hash);
            }
            catch (Exception)
            {
                // The index is a cache, never an authority. Failing to write it costs a duplicate
                // page on the next import, which is not worth failing a successful one over.
            }
        }

        private void NavigateIfRequested(ImportSummary summary, ImportOptions options)
        {
            if (!options.NavigateToFirstCreatedPage)
            {
                return;
            }

            var pageId = summary.FirstCreatedPageId;
            if (string.IsNullOrEmpty(pageId))
            {
                return;
            }

            try
            {
                _gateway.NavigateTo(pageId);
            }
            catch (OneNoteException)
            {
                // Navigation is a convenience. The pages exist either way.
            }
        }

        private static IReadOnlyList<Diagnostic> CollectWarnings(
            ParsedDocument parsed,
            IReadOnlyDictionary<string, DiagramOutcome> diagrams,
            IReadOnlyDictionary<string, AssetOutcome> assets)
        {
            var warnings = new List<Diagnostic>(parsed.Diagnostics);

            warnings.AddRange(diagrams.Values
                .Where(d => !d.Succeeded)
                .Select(d => Diagnostic.Warning("Diagram.Failed", d.FailureMessage)));

            warnings.AddRange(assets.Values
                .Where(a => !a.Succeeded)
                .Select(a => Diagnostic.Warning("Asset.Failed", a.FailureMessage)));

            // An image that loaded from outside the document's folder is on the page and looks
            // right, so the report is the only place the user can learn it came from elsewhere.
            warnings.AddRange(assets.Values
                .Where(a => a.Succeeded && !string.IsNullOrEmpty(a.Warning))
                .Select(a => Diagnostic.Warning("Asset.Warning", a.Warning)));

            return warnings;
        }

        private static bool IsFileAccessFailure(Exception ex)
        {
            return ex is IOException
                || ex is UnauthorizedAccessException
                || ex is System.Security.SecurityException
                || ex is ArgumentException
                || ex is NotSupportedException;
        }

        private string Format(string key, object argument)
        {
            return string.Format(CultureInfo.CurrentCulture, _strings.Get(key), argument);
        }
    }
}
