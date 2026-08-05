using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Md2OneNote.Core;
using Xunit;

namespace Md2OneNote.Application.Tests
{
    public class ImportServiceTests
    {
        private const string PathA = @"C:\notes\a.md";
        private const string PathB = @"C:\notes\b.md";

        private readonly FakeGateway _gateway = new FakeGateway();
        private readonly FakeXmlRenderer _renderer = new FakeXmlRenderer();
        private readonly FakeSourceFileReader _reader = new FakeSourceFileReader();
        private readonly FakePageIndex _index = new FakePageIndex();
        private readonly List<IDiagramRenderer> _diagramRenderers = new List<IDiagramRenderer>();

        private ImportService Build(Func<string, ParsedDocument> parse = null, IAssetSource assets = null)
        {
            var strings = FallbackStringCatalog.Instance;

            return new ImportService(
                _gateway,
                new FakeParser(parse ?? (_ => FakeParser.Document())),
                _renderer,
                _reader,
                new DiagramResolver(_diagramRenderers, strings),
                new AssetResolver(assets ?? new FakeAssetSource(), strings),
                _index,
                new FixedClock(new DateTime(2026, 8, 3, 12, 0, 0, DateTimeKind.Utc)),
                strings);
        }

        private static ImportOptions Options()
        {
            return ImportOptions.CreateDefault("1.0.0-test", ParseOptions.None);
        }

        private static Task<ImportSummary> Run(ImportService service, params string[] paths)
        {
            return service.ImportAsync(paths, Options(), NullImportProgress.Instance, CancellationToken.None);
        }

        [Fact]
        public async Task Creates_a_page_for_a_new_file()
        {
            _reader.Add(PathA, "# Hello");

            var summary = await Run(Build(), PathA);

            Assert.Equal(1, summary.CreatedCount);
            Assert.Equal(FileOutcome.Created, summary.Results[0].Outcome);
            Assert.Single(_gateway.CreatedPageIds);
            Assert.Single(_gateway.WrittenPages);
        }

        [Fact]
        public async Task An_image_that_loaded_with_a_reservation_is_reported_although_the_page_is_fine()
        {
            _reader.Add(PathA, "![x](../shared/logo.png)");
            var document = FakeParser.Document(assets: new[] { AssetRequest.Create("../shared/logo.png", "x") });
            var assets = new FakeAssetSource(_ =>
                AssetOutcome.Success(new byte[] { 9 }, "png", 10, 10, "outside the folder"));

            var summary = await Run(Build(_ => document, assets), PathA);

            var result = summary.Results[0];
            Assert.Equal(FileOutcome.Created, result.Outcome);
            Assert.Contains(result.Warnings, w => w.Message == "outside the folder");
        }

        [Fact]
        public async Task Unchanged_file_is_skipped_without_touching_the_notebook()
        {
            const string text = "# Hello";
            _reader.Add(PathA, text);
            _index.Seed("section-1", PathA, "page-existing", Sha256.OfString(text));

            var summary = await Run(Build(), PathA);

            Assert.Equal(1, summary.SkippedCount);
            Assert.Empty(_gateway.CreatedPageIds);
            Assert.Empty(_gateway.WrittenPages);
        }

        [Fact]
        public async Task Changed_file_creates_a_second_page_and_leaves_the_original_alone()
        {
            _reader.Add(PathA, "# Changed");
            _index.Seed("section-1", PathA, "page-existing", Sha256.OfString("# Original"));

            var summary = await Run(Build(), PathA);

            Assert.Equal(FileOutcome.CreatedSuperseding, summary.Results[0].Outcome);

            // The original page is neither rewritten nor navigated over: the only page written is
            // the new one (FR-19, FR-21).
            Assert.Single(_gateway.WrittenPages);
            Assert.False(_gateway.WrittenPages.ContainsKey("page-existing"));
        }

        [Fact]
        public async Task Superseding_page_title_is_distinguishable_from_the_original()
        {
            _reader.Add(PathA, "# Changed");
            _index.Seed("section-1", PathA, "page-existing", Sha256.OfString("# Original"));

            await Run(Build(_ => FakeParser.Document("Design Notes")), PathA);

            var title = _renderer.Calls[0].Title;
            Assert.StartsWith("Design Notes (imported ", title, StringComparison.Ordinal);
        }

        [Fact]
        public async Task Title_falls_back_to_the_file_name()
        {
            _reader.Add(PathA, "no heading");

            await Run(Build(_ => FakeParser.Document(null)), PathA);

            Assert.Equal("a", _renderer.Calls[0].Title);
        }

        [Fact]
        public async Task A_failing_file_does_not_stop_the_others()
        {
            _reader.Add(PathA, "# A").Add(PathB, "# B");
            _reader.Unreadable.Add(PathA);

            var summary = await Run(Build(), PathA, PathB);

            Assert.Equal(FileOutcome.Failed, summary.Results[0].Outcome);
            Assert.Equal(FailureReason.Unreadable, summary.Results[0].Reason);
            Assert.Equal(FileOutcome.Created, summary.Results[1].Outcome);
            Assert.Equal(1, summary.CreatedCount);
        }

        [Fact]
        public async Task No_active_section_fails_everything_and_creates_nothing()
        {
            _reader.Add(PathA, "# A").Add(PathB, "# B");
            _gateway.HasActiveSection = false;

            var summary = await Run(Build(), PathA, PathB);

            Assert.Equal(2, summary.FailedCount);
            Assert.All(summary.Results, r => Assert.Equal(FailureReason.SectionUnavailable, r.Reason));
            Assert.Empty(_gateway.CreatedPageIds);
        }

        [Fact]
        public async Task A_rendering_failure_creates_no_page_at_all()
        {
            _reader.Add(PathA, "# A");
            _renderer.Throws = new InvalidOperationException("boom");

            var summary = await Run(Build(), PathA);

            Assert.Equal(FailureReason.Internal, summary.Results[0].Reason);

            // NFR-1: everything fallible runs before the notebook is touched, so a failure this
            // late still leaves no empty page behind.
            Assert.Empty(_gateway.CreatedPageIds);
        }

        [Fact]
        public async Task Oversized_generated_page_is_refused_before_creation()
        {
            _reader.Add(PathA, "# A");
            _renderer.Xml = new string('x', 2048);

            var options = new ImportOptions(
                "1.0.0-test",
                ParseOptions.None,
                AssetPolicy.Default,
                new ImportLimits(1024 * 1024, 10, 1024),
                ImportOptions.DefaultSupersedingTitleFormat,
                true);

            var summary = await Build().ImportAsync(
                new[] { PathA }, options, NullImportProgress.Instance, CancellationToken.None);

            Assert.Equal(FailureReason.TooLarge, summary.Results[0].Reason);
            Assert.Empty(_gateway.CreatedPageIds);
        }

        [Fact]
        public async Task Repeated_diagrams_are_rendered_once()
        {
            var mermaid = new FakeDiagramRenderer("mermaid");
            _diagramRenderers.Add(mermaid);
            _reader.Add(PathA, "# A");

            var request = DiagramRequest.Create("mermaid", "graph TD; A-->B;");
            var duplicate = DiagramRequest.Create("mermaid", "graph TD; A-->B;");
            var other = DiagramRequest.Create("mermaid", "graph TD; C-->D;");

            await Run(Build(_ => FakeParser.Document("Doc", new[] { request, duplicate, other })), PathA);

            Assert.Equal(2, mermaid.RenderedKeys.Count);
        }

        [Fact]
        public async Task A_diagram_with_no_renderer_still_produces_a_page()
        {
            _reader.Add(PathA, "# A");
            var request = DiagramRequest.Create("plantuml", "@startuml");

            var summary = await Run(Build(_ => FakeParser.Document("Doc", new[] { request })), PathA);

            Assert.Equal(FileOutcome.Created, summary.Results[0].Outcome);
            Assert.Single(_gateway.WrittenPages);

            var outcome = _renderer.Calls[0].Diagrams[request.Key];
            Assert.False(outcome.Succeeded);
            Assert.Contains("plantuml", outcome.FailureMessage, StringComparison.Ordinal);
        }

        [Fact]
        public async Task A_renderer_that_throws_is_reported_as_a_warning_not_a_failure()
        {
            var mermaid = new FakeDiagramRenderer("mermaid") { Throws = new InvalidOperationException("webview died") };
            _diagramRenderers.Add(mermaid);
            _reader.Add(PathA, "# A");
            var request = DiagramRequest.Create("mermaid", "graph TD;");

            var summary = await Run(Build(_ => FakeParser.Document("Doc", new[] { request })), PathA);

            Assert.Equal(FileOutcome.Created, summary.Results[0].Outcome);
            Assert.Single(summary.Results[0].Warnings);
        }

        [Fact]
        public async Task Diagram_count_over_the_limit_falls_back_instead_of_failing()
        {
            var mermaid = new FakeDiagramRenderer("mermaid");
            _diagramRenderers.Add(mermaid);
            _reader.Add(PathA, "# A");

            var requests = new List<DiagramRequest>();
            for (var i = 0; i < 5; i++)
            {
                requests.Add(DiagramRequest.Create("mermaid", "graph " + i));
            }

            var options = new ImportOptions(
                "1.0.0-test",
                ParseOptions.None,
                AssetPolicy.Default,
                new ImportLimits(1024 * 1024, 2, 1024 * 1024),
                ImportOptions.DefaultSupersedingTitleFormat,
                true);

            var summary = await Build(_ => FakeParser.Document("Doc", requests)).ImportAsync(
                new[] { PathA }, options, NullImportProgress.Instance, CancellationToken.None);

            Assert.Equal(FileOutcome.Created, summary.Results[0].Outcome);
            Assert.Equal(2, mermaid.RenderedKeys.Count);
            Assert.Equal(3, summary.Results[0].Warnings.Count);
        }

        [Fact]
        public async Task Cancellation_stops_the_loop_and_keeps_what_was_created()
        {
            _reader.Add(PathA, "# A").Add(PathB, "# B");
            var cts = new CancellationTokenSource();

            var progress = new CancellingProgress(cts);
            var summary = await Build().ImportAsync(
                new[] { PathA, PathB }, Options(), progress, cts.Token);

            Assert.Equal(FileOutcome.Created, summary.Results[0].Outcome);
            Assert.Equal(FileOutcome.Cancelled, summary.Results[1].Outcome);
            Assert.Single(_gateway.CreatedPageIds);
        }

        [Fact]
        public async Task Successful_import_records_the_page_and_navigates_to_the_first_one()
        {
            _reader.Add(PathA, "# A").Add(PathB, "# B");

            var summary = await Run(Build(), PathA, PathB);

            Assert.Equal(2, _index.Recorded.Count);
            Assert.Single(_gateway.NavigatedTo);
            Assert.Equal(summary.FirstCreatedPageId, _gateway.NavigatedTo[0]);
        }

        [Fact]
        public async Task A_failure_to_record_the_index_does_not_fail_the_import()
        {
            _reader.Add(PathA, "# A");

            var service = new ImportService(
                _gateway,
                new FakeParser(_ => FakeParser.Document()),
                _renderer,
                _reader,
                new DiagramResolver(_diagramRenderers, FallbackStringCatalog.Instance),
                new AssetResolver(new FakeAssetSource(), FallbackStringCatalog.Instance),
                new ThrowingPageIndex(),
                new FixedClock(DateTime.UtcNow),
                FallbackStringCatalog.Instance);

            var summary = await Run(service, PathA);

            Assert.Equal(FileOutcome.Created, summary.Results[0].Outcome);
        }

        [Fact]
        public async Task OneNote_staying_busy_is_reported_as_such()
        {
            _reader.Add(PathA, "# A");
            _gateway.ReplaceThrows = new OneNoteBusyException("busy", new InvalidOperationException());

            var summary = await Run(Build(), PathA);

            Assert.Equal(FailureReason.OneNoteBusy, summary.Results[0].Reason);
        }

        private sealed class CancellingProgress : IImportProgress
        {
            private readonly CancellationTokenSource _cts;

            public CancellingProgress(CancellationTokenSource cts)
            {
                _cts = cts;
            }

            public void FileStarted(int index, int total, string path)
            {
            }

            public void Step(string localizedMessage)
            {
            }

            public void FileFinished(FileResult result)
            {
                _cts.Cancel();
            }
        }

        private sealed class ThrowingPageIndex : IPageIndex
        {
            public PageMatch TryFind(string sectionId, string sourcePath)
            {
                return PageMatch.None;
            }

            public void Record(string sectionId, string sourcePath, string pageId, string sourceHash)
            {
                throw new IOException("index file locked");
            }
        }
    }
}
