using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Md2OneNote.Core;

namespace Md2OneNote.Application.Tests
{
    internal sealed class FakeGateway : IOneNoteGateway
    {
        private int _nextPage = 1;

        public bool HasActiveSection { get; set; } = true;

        public Exception CreatePageThrows { get; set; }

        public Exception ReplaceThrows { get; set; }

        public List<string> CreatedPageIds { get; } = new List<string>();

        public Dictionary<string, string> WrittenPages { get; } = new Dictionary<string, string>(StringComparer.Ordinal);

        public List<string> NavigatedTo { get; } = new List<string>();

        public SectionRef GetActiveSection()
        {
            if (!HasActiveSection)
            {
                throw new NoActiveSectionException("no section");
            }

            return new SectionRef("section-1", "Test Section");
        }

        public IReadOnlyList<PageRef> ListPages(string sectionId)
        {
            return new PageRef[0];
        }

        public string GetPageXml(string pageId)
        {
            return "<one:Page/>";
        }

        public string CreatePage(string sectionId)
        {
            if (CreatePageThrows != null) throw CreatePageThrows;

            var id = "page-" + _nextPage.ToString(CultureInfo.InvariantCulture);
            _nextPage++;
            CreatedPageIds.Add(id);
            return id;
        }

        public void ReplacePageContent(string pageId, string pageXml)
        {
            if (ReplaceThrows != null) throw ReplaceThrows;

            WrittenPages[pageId] = pageXml;
        }

        public void NavigateTo(string pageId)
        {
            NavigatedTo.Add(pageId);
        }

        public List<string> DeletedPageIds { get; } = new List<string>();

        public Exception DeleteThrows { get; set; }

        public void DeletePage(string pageId)
        {
            if (DeleteThrows != null) throw DeleteThrows;

            DeletedPageIds.Add(pageId);
        }
    }

    internal sealed class FakeSourceFileReader : ISourceFileReader
    {
        private readonly Dictionary<string, string> _files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        public HashSet<string> Unreadable { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FakeSourceFileReader Add(string path, string text)
        {
            _files[path] = text;
            return this;
        }

        public SourceFile Read(string path, long maxBytes)
        {
            if (Unreadable.Contains(path))
            {
                throw new IOException("cannot read " + path);
            }

            string text;
            if (!_files.TryGetValue(path, out text))
            {
                throw new FileNotFoundException("missing", path);
            }

            var bytes = Encoding.UTF8.GetBytes(text);
            if (bytes.LongLength > maxBytes)
            {
                throw new SourceFileTooLargeException("too large");
            }

            return new SourceFile(path, Path.GetDirectoryName(path) ?? string.Empty, text, bytes);
        }
    }

    internal sealed class FakeParser : IMarkdownDocumentParser
    {
        private readonly Func<string, ParsedDocument> _parse;

        public FakeParser(Func<string, ParsedDocument> parse)
        {
            _parse = parse;
        }

        public ParsedDocument Parse(string markdown, ParseOptions options)
        {
            return _parse(markdown);
        }

        public static ParsedDocument Document(
            string title = "Doc",
            IReadOnlyList<DiagramRequest> diagrams = null,
            IReadOnlyList<AssetRequest> assets = null)
        {
            return new ParsedDocument(
                title,
                DocumentModel.Empty,
                diagrams ?? new DiagramRequest[0],
                assets ?? new AssetRequest[0],
                new Diagnostic[0]);
        }
    }

    internal sealed class FakeXmlRenderer : IOneNoteXmlRenderer
    {
        public Exception Throws { get; set; }

        public string Xml { get; set; } = "<one:Page><one:Title/></one:Page>";

        public List<RenderInputs> Calls { get; } = new List<RenderInputs>();

        public string Render(ParsedDocument document, RenderInputs inputs)
        {
            if (Throws != null) throw Throws;

            Calls.Add(inputs);
            return Xml;
        }
    }

    internal sealed class FakeDiagramRenderer : IDiagramRenderer
    {
        private readonly string _language;

        public FakeDiagramRenderer(string language)
        {
            _language = language;
        }

        public Exception Throws { get; set; }

        public List<string> RenderedKeys { get; } = new List<string>();

        public bool CanRender(string language)
        {
            return string.Equals(language, _language, StringComparison.OrdinalIgnoreCase);
        }

        public Task<DiagramOutcome> RenderAsync(DiagramRequest request, CancellationToken ct)
        {
            if (Throws != null) throw Throws;

            RenderedKeys.Add(request.Key);
            return Task.FromResult(DiagramOutcome.Success(new byte[] { 1, 2, 3 }, 100, 50));
        }
    }

    internal sealed class FakeAssetSource : IAssetSource
    {
        private readonly Func<AssetRequest, AssetOutcome> _load;

        public FakeAssetSource(Func<AssetRequest, AssetOutcome> load = null)
        {
            _load = load ?? (_ => AssetOutcome.Success(new byte[] { 9 }, "png", 10, 10));
        }

        public AssetOutcome Load(AssetRequest request, string baseDirectory, AssetPolicy policy)
        {
            return _load(request);
        }
    }

    internal sealed class FakePageIndex : IPageIndex
    {
        private readonly Dictionary<string, PageMatch> _entries = new Dictionary<string, PageMatch>(StringComparer.OrdinalIgnoreCase);

        public List<string> Recorded { get; } = new List<string>();

        public FakePageIndex Seed(string sectionId, string sourcePath, string pageId, string hash)
        {
            _entries[Key(sectionId, sourcePath)] = PageMatch.Hit(pageId, hash);
            return this;
        }

        public PageMatch TryFind(string sectionId, string sourcePath)
        {
            PageMatch match;
            return _entries.TryGetValue(Key(sectionId, sourcePath), out match) ? match : PageMatch.None;
        }

        public void Record(string sectionId, string sourcePath, string pageId, string sourceHash)
        {
            Recorded.Add(sourcePath);
            _entries[Key(sectionId, sourcePath)] = PageMatch.Hit(pageId, sourceHash);
        }

        private static string Key(string sectionId, string sourcePath)
        {
            return sectionId + "|" + sourcePath;
        }
    }

    internal sealed class FixedClock : IClock
    {
        public FixedClock(DateTime utcNow)
        {
            UtcNow = utcNow;
        }

        public DateTime UtcNow { get; }
    }

    internal sealed class RecordingProgress : IImportProgress
    {
        public List<string> Started { get; } = new List<string>();

        public List<FileResult> Finished { get; } = new List<FileResult>();

        public void FileStarted(int index, int total, string path)
        {
            Started.Add(path);
        }

        public void Step(string localizedMessage)
        {
        }

        public void FileFinished(FileResult result)
        {
            Finished.Add(result);
        }
    }
}
