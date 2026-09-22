using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Footnotes;
using Markdig.Extensions.Mathematics;
using Markdig.Extensions.TaskLists;
using Markdig.Extensions.Yaml;
using Md = Markdig.Syntax;
using MdInline = Markdig.Syntax.Inlines;
using MdTable = Markdig.Extensions.Tables;

namespace Md2OneNote.Core.Parsing
{
    /// <summary>
    /// Pass 1: Markdown text to a block model plus the requests the document implies. Pure — no
    /// file is opened, no diagram is rendered, no path is resolved (NFR-21, DESIGN.md §3).
    /// </summary>
    public sealed class MarkdownDocumentParser : IMarkdownDocumentParser
    {
        private static readonly MarkdownPipeline Pipeline = BuildPipeline();

        private readonly IStringCatalog _strings;

        /// <param name="strings">
        /// Used for diagnostic messages. Taken through the constructor rather than
        /// <see cref="ParseOptions"/> so the interface stays as DESIGN.md §5.1 specifies it.
        /// </param>
        public MarkdownDocumentParser(IStringCatalog strings)
        {
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            _strings = strings;
        }

        public ParsedDocument Parse(string markdown, ParseOptions options)
        {
            if (markdown == null) throw new ArgumentNullException(nameof(markdown));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var document = Markdown.Parse(markdown, Pipeline);
            var builder = new DocumentBuilder(options, _strings);

            var title = FrontMatter.ReadTitle(document);
            var blocks = builder.ConvertBlocks(document);

            if (string.IsNullOrWhiteSpace(title))
            {
                title = TakeLeadingHeading(blocks);
            }

            return new ParsedDocument(
                title,
                new DocumentModel(blocks),
                builder.Diagrams,
                builder.Assets,
                builder.Diagnostics);
        }

        /// <summary>
        /// FR-7: CommonMark plus the GFM set, and nothing else. The extension list is explicit
        /// rather than <c>UseAdvancedExtensions</c> so that adding syntax is a decision, not a
        /// side effect of a package upgrade.
        /// </summary>
        private static MarkdownPipeline BuildPipeline()
        {
            var builder = new MarkdownPipelineBuilder()
                .UsePipeTables()
                .UseTaskLists()
                .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
                .UseAutoLinks()
                .UseFootnotes()
                .UseYamlFrontMatter();

            // $$ blocks become "math" diagrams (FR-12). Only the block parser: inline $…$ stays
            // literal text, because OneNote cannot place a picture inside a line of text.
            builder.BlockParsers.AddIfNotAlready(new MathBlockParser());

            return builder.Build();
        }

        /// <summary>
        /// FR-15: a leading H1 becomes the page title and stops being body content, so the title
        /// does not appear twice. Only a heading in first position qualifies.
        /// </summary>
        private static string TakeLeadingHeading(List<IBlock> blocks)
        {
            if (blocks.Count == 0)
            {
                return null;
            }

            var heading = blocks[0] as HeadingBlock;
            if (heading == null || heading.Level != 1)
            {
                return null;
            }

            var text = PlainText.Of(heading.Content);
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            blocks.RemoveAt(0);
            return text.Trim();
        }
    }

    /// <summary>
    /// Walks the Markdig AST once, accumulating blocks, requests and diagnostics. One instance per
    /// document, so the deduplication maps and the footnote numbering are per-document state.
    /// </summary>
    internal sealed class DocumentBuilder
    {
        /// <summary>
        /// How far the converter will follow nested structure. Every level of quoting, list or
        /// table nesting costs stack frames here and again in the renderer, and a document nested
        /// thousands deep is a plausible hostile input. A stack overflow cannot be caught and would
        /// take OneNote down with it, so the limit is enforced rather than hoped for
        /// (NFR-8, NFR-2). Sixty-four is far past anything a person writes.
        /// </summary>
        private const int MaxNestingDepth = 64;

        /// <summary>The fence language a <c>$$</c> block stands for; the same as a <c>```math</c> fence.</summary>
        private const string MathLanguage = "math";

        private readonly ParseOptions _options;
        private readonly IStringCatalog _strings;

        private int _depth;
        private bool _depthReported;

        private readonly List<DiagramRequest> _diagrams = new List<DiagramRequest>();
        private readonly HashSet<string> _diagramKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<AssetRequest> _assets = new List<AssetRequest>();
        private readonly HashSet<string> _assetKeys = new HashSet<string>(StringComparer.Ordinal);
        private readonly List<Diagnostic> _diagnostics = new List<Diagnostic>();

        private readonly Dictionary<Footnote, int> _footnoteNumbers = new Dictionary<Footnote, int>();

        public DocumentBuilder(ParseOptions options, IStringCatalog strings)
        {
            _options = options;
            _strings = strings;
        }

        public IReadOnlyList<DiagramRequest> Diagrams { get { return _diagrams; } }

        public IReadOnlyList<AssetRequest> Assets { get { return _assets; } }

        public IReadOnlyList<Diagnostic> Diagnostics { get { return _diagnostics; } }

        public List<IBlock> ConvertBlocks(Md.ContainerBlock container)
        {
            // Footnotes are numbered before the body is walked, because references to them are
            // encountered first but the definitions live in a group at the end of the document.
            NumberFootnotes(container);

            var blocks = new List<IBlock>();
            foreach (var child in container)
            {
                ConvertBlock(child, blocks);
            }

            return blocks;
        }

        private void NumberFootnotes(Md.ContainerBlock container)
        {
            foreach (var child in container)
            {
                var group = child as FootnoteGroup;
                if (group == null)
                {
                    continue;
                }

                foreach (var note in group)
                {
                    var footnote = note as Footnote;
                    if (footnote != null && !_footnoteNumbers.ContainsKey(footnote))
                    {
                        _footnoteNumbers.Add(footnote, _footnoteNumbers.Count + 1);
                    }
                }
            }
        }

        private void ConvertBlock(Md.Block block, List<IBlock> output)
        {
            // Front matter is metadata, not content. It derives from CodeBlock, so it has to be
            // tested before the code-block case or it renders as a code listing.
            if (block is YamlFrontMatterBlock)
            {
                return;
            }

            var heading = block as Md.HeadingBlock;
            if (heading != null)
            {
                var level = heading.Level < 1 ? 1 : (heading.Level > 6 ? 6 : heading.Level);
                var lifted = new List<IBlock>();
                output.Add(new HeadingBlock(level, ConvertInlines(heading.Inline, lifted.Add)));
                output.AddRange(lifted);
                return;
            }

            var paragraph = block as Md.ParagraphBlock;
            if (paragraph != null)
            {
                ConvertParagraph(paragraph, output);
                return;
            }

            var quote = block as Md.QuoteBlock;
            if (quote != null)
            {
                output.Add(new QuoteBlock(ConvertChildren(quote)));
                return;
            }

            var list = block as Md.ListBlock;
            if (list != null)
            {
                output.Add(ConvertList(list));
                return;
            }

            // A $$ block is a math fence in all but syntax. It derives from FencedCodeBlock and
            // has no info string, so it is tested first or it would become a code block with no
            // language.
            var math = block as MathBlock;
            if (math != null)
            {
                output.Add(ConvertFence(MathLanguage, LinesOf(math)));
                return;
            }

            var fenced = block as Md.FencedCodeBlock;
            if (fenced != null)
            {
                output.Add(ConvertFence(FirstWord(fenced.Info), LinesOf(fenced)));
                return;
            }

            var code = block as Md.CodeBlock;
            if (code != null)
            {
                output.Add(new CodeBlock(string.Empty, LinesOf(code)));
                return;
            }

            var table = block as MdTable.Table;
            if (table != null)
            {
                var converted = ConvertTable(table);
                if (converted != null)
                {
                    output.Add(converted);
                }

                return;
            }

            if (block is Md.ThematicBreakBlock)
            {
                output.Add(ThematicBreakBlock.Instance);
                return;
            }

            var html = block as Md.HtmlBlock;
            if (html != null)
            {
                // DESIGN.md §6.4: dropped, never passed through. The diagnostic is what keeps the
                // removal visible to the user rather than silent (NFR-4).
                _diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.HtmlBlockDropped,
                    Format(CoreStringKeys.HtmlBlockDropped, html.Line + 1)));
                return;
            }

            var footnotes = block as FootnoteGroup;
            if (footnotes != null)
            {
                var converted = ConvertFootnotes(footnotes);
                if (converted != null)
                {
                    output.Add(converted);
                }

                return;
            }

            // Link reference definitions and anything an extension adds later carry no content of
            // their own. Unknown input is skipped, never thrown on (FR-13).
            var nested = block as Md.ContainerBlock;
            if (nested != null && !(block is Md.LinkReferenceDefinitionGroup))
            {
                output.AddRange(ConvertChildren(nested));
            }
        }

        private List<IBlock> ConvertChildren(Md.ContainerBlock container)
        {
            var blocks = new List<IBlock>();
            if (!Descend())
            {
                return blocks;
            }

            try
            {
                foreach (var child in container)
                {
                    ConvertBlock(child, blocks);
                }
            }
            finally
            {
                _depth--;
            }

            return blocks;
        }

        /// <summary>
        /// Takes one level of the nesting budget, or reports that the document has run out of it.
        /// The warning is raised once: a pathological document would otherwise produce thousands.
        /// </summary>
        private bool Descend()
        {
            if (_depth < MaxNestingDepth)
            {
                _depth++;
                return true;
            }

            if (!_depthReported)
            {
                _depthReported = true;
                _diagnostics.Add(Diagnostic.Warning(
                    DiagnosticCodes.NestingTooDeep,
                    Format(CoreStringKeys.NestingTooDeep, MaxNestingDepth)));
            }

            return false;
        }

        /// <summary>
        /// A paragraph can produce more than one block, because an image inside it has to become a
        /// block of its own. The paragraph is split at each image so that text before and after it
        /// stays on the correct side.
        /// </summary>
        private void ConvertParagraph(Md.ParagraphBlock paragraph, List<IBlock> output)
        {
            if (paragraph.Inline == null)
            {
                return;
            }

            var pending = new List<IInline>();
            Action<ImageBlock> emit = image =>
            {
                Flush(pending, output);
                output.Add(image);
            };

            foreach (var child in paragraph.Inline)
            {
                ConvertInline(child, pending, emit);
            }

            Flush(pending, output);
        }

        private static void Flush(List<IInline> pending, List<IBlock> output)
        {
            if (pending.Count == 0)
            {
                return;
            }

            output.Add(new ParagraphBlock(new List<IInline>(pending)));
            pending.Clear();
        }

        private ListBlock ConvertList(Md.ListBlock list)
        {
            var items = new List<ListItem>();
            foreach (var child in list)
            {
                var item = child as Md.ListItemBlock;
                if (item == null)
                {
                    continue;
                }

                var task = DetectTask(item);
                var content = ConvertChildren(item);
                if (task != TaskState.None)
                {
                    TrimLeadingSpace(content);
                }

                items.Add(new ListItem(task, content));
            }

            return new ListBlock(list.IsOrdered ? ListKind.Ordered : ListKind.Bulleted, items);
        }

        /// <summary>
        /// A GFM checkbox is an inline at the head of the item's first paragraph, not a property of
        /// the item, so it has to be read before the paragraph is converted.
        /// </summary>
        private static TaskState DetectTask(Md.ListItemBlock item)
        {
            if (item.Count == 0)
            {
                return TaskState.None;
            }

            var paragraph = item[0] as Md.ParagraphBlock;
            if (paragraph == null || paragraph.Inline == null)
            {
                return TaskState.None;
            }

            var task = paragraph.Inline.FirstChild as TaskList;
            if (task == null)
            {
                return TaskState.None;
            }

            return task.Checked ? TaskState.Checked : TaskState.Unchecked;
        }

        /// <summary>Removes the space the checkbox left behind once the checkbox itself is gone.</summary>
        private static void TrimLeadingSpace(List<IBlock> content)
        {
            if (content.Count == 0)
            {
                return;
            }

            var paragraph = content[0] as ParagraphBlock;
            if (paragraph == null || paragraph.Content.Count == 0)
            {
                return;
            }

            var first = paragraph.Content[0] as TextRun;
            if (first == null)
            {
                return;
            }

            var trimmed = first.Text.TrimStart();
            if (trimmed == first.Text)
            {
                return;
            }

            var inlines = new List<IInline>(paragraph.Content);
            if (trimmed.Length == 0)
            {
                inlines.RemoveAt(0);
            }
            else
            {
                inlines[0] = new TextRun(trimmed);
            }

            content[0] = new ParagraphBlock(inlines);
        }

        private IBlock ConvertFence(string language, string source)
        {
            if (language.Length > 0 && _options.DiagramLanguages.Contains(language))
            {
                var request = DiagramRequest.Create(language, source);
                if (_diagramKeys.Add(request.Key))
                {
                    _diagrams.Add(request);
                }

                return new DiagramBlock(request.Key, language, source);
            }

            // No registered renderer, so it is a code block. This is the fallback FR-13 requires,
            // and it is reached by ordinary control flow rather than by catching anything.
            return new CodeBlock(language, source);
        }

        private TableBlock ConvertTable(MdTable.Table table)
        {
            var rows = new List<TableRow>();
            var columnCount = 0;

            foreach (var child in table)
            {
                var row = child as MdTable.TableRow;
                if (row == null)
                {
                    continue;
                }

                var cells = new List<TableCell>();
                foreach (var cellChild in row)
                {
                    var cell = cellChild as MdTable.TableCell;
                    if (cell != null)
                    {
                        cells.Add(new TableCell(ConvertChildren(cell)));
                    }
                }

                if (cells.Count > columnCount)
                {
                    columnCount = cells.Count;
                }

                rows.Add(new TableRow(row.IsHeader, cells));
            }

            return columnCount == 0 ? null : new TableBlock(columnCount, rows);
        }

        private FootnotesBlock ConvertFootnotes(FootnoteGroup group)
        {
            var notes = new List<FootnoteDefinition>();
            foreach (var child in group)
            {
                var footnote = child as Footnote;
                if (footnote == null)
                {
                    continue;
                }

                int number;
                if (!_footnoteNumbers.TryGetValue(footnote, out number))
                {
                    number = notes.Count + 1;
                }

                notes.Add(new FootnoteDefinition(number, ConvertChildren(footnote)));
            }

            return notes.Count == 0 ? null : new FootnotesBlock(notes);
        }

        /// <param name="emit">
        /// Receives images found in the sequence. OneNote cannot place a <c>one:Image</c> inside a
        /// <c>one:T</c>, so an image written inline becomes a block of its own; where that block
        /// lands is the caller's decision.
        /// </param>
        private List<IInline> ConvertInlines(MdInline.ContainerInline container, Action<ImageBlock> emit)
        {
            var inlines = new List<IInline>();
            if (container == null || !Descend())
            {
                return inlines;
            }

            try
            {
                foreach (var child in container)
                {
                    ConvertInline(child, inlines, emit);
                }
            }
            finally
            {
                _depth--;
            }

            return inlines;
        }

        private void ConvertInline(MdInline.Inline inline, List<IInline> output, Action<ImageBlock> emit)
        {
            var literal = inline as MdInline.LiteralInline;
            if (literal != null)
            {
                var text = literal.Content.ToString();
                if (text.Length > 0)
                {
                    output.Add(new TextRun(text));
                }

                return;
            }

            var emphasis = inline as MdInline.EmphasisInline;
            if (emphasis != null)
            {
                var children = ConvertInlines(emphasis, emit);
                if (children.Count > 0)
                {
                    output.Add(new StyledRun(StyleOf(emphasis), children));
                }

                return;
            }

            var code = inline as MdInline.CodeInline;
            if (code != null)
            {
                output.Add(new CodeRun(code.Content ?? string.Empty));
                return;
            }

            var link = inline as MdInline.LinkInline;
            if (link != null)
            {
                ConvertLink(link, output, emit);
                return;
            }

            var autolink = inline as MdInline.AutolinkInline;
            if (autolink != null)
            {
                var url = autolink.IsEmail ? "mailto:" + autolink.Url : autolink.Url;
                AddLink(url, new List<IInline> { new TextRun(autolink.Url) }, output);
                return;
            }

            var lineBreak = inline as MdInline.LineBreakInline;
            if (lineBreak != null)
            {
                output.Add(lineBreak.IsHard ? LineBreakRun.Hard : LineBreakRun.Soft);
                return;
            }

            var html = inline as MdInline.HtmlInline;
            if (html != null)
            {
                // Escaped into literal text rather than dropped: someone writing <sup> meant to
                // write something, and escaped text cannot execute or fetch (DESIGN.md §6.4).
                output.Add(new TextRun(html.Tag ?? string.Empty));
                return;
            }

            var entity = inline as MdInline.HtmlEntityInline;
            if (entity != null)
            {
                output.Add(new TextRun(entity.Transcoded.ToString()));
                return;
            }

            var footnoteLink = inline as FootnoteLink;
            if (footnoteLink != null)
            {
                // Back-links are Markdig's own navigation affordance inside the definition; they
                // have no meaning on a OneNote page.
                if (footnoteLink.IsBackLink || footnoteLink.Footnote == null)
                {
                    return;
                }

                int number;
                if (_footnoteNumbers.TryGetValue(footnoteLink.Footnote, out number))
                {
                    output.Add(new FootnoteReferenceRun(number));
                }

                return;
            }

            // Task-list checkboxes are consumed by the list writer; anything else unrecognized
            // still contributes its children rather than disappearing.
            var nested = inline as MdInline.ContainerInline;
            if (nested != null)
            {
                output.AddRange(ConvertInlines(nested, emit));
            }
        }

        private void ConvertLink(MdInline.LinkInline link, List<IInline> output, Action<ImageBlock> emit)
        {
            if (link.IsImage)
            {
                var request = AssetRequest.Create(link.Url ?? string.Empty, PlainText.Of(link));
                if (_assetKeys.Add(request.Key))
                {
                    _assets.Add(request);
                }

                emit(new ImageBlock(request.Key, request.RawPath, request.AltText));
                return;
            }

            var children = ConvertInlines(link, emit);
            if (children.Count == 0)
            {
                children.Add(new TextRun(link.Url ?? string.Empty));
            }

            AddLink(link.Url, children, output);
        }

        /// <summary>
        /// FR-16: <c>http(s)</c> and <c>mailto</c> become live hyperlinks. Everything else keeps
        /// its text and loses the link — a <c>file://</c> target only resolves on the machine that
        /// imported it (OQ-4), and refusing every other scheme is also what keeps <c>javascript:</c>
        /// out of the page (NFR-4).
        /// </summary>
        private static void AddLink(string url, List<IInline> children, List<IInline> output)
        {
            if (IsHyperlinkable(url))
            {
                output.Add(new HyperlinkRun(url, children));
                return;
            }

            output.AddRange(children);
        }

        private static bool IsHyperlinkable(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return false;
            }

            return url.StartsWith("http://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || url.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase);
        }

        private static RunStyle StyleOf(MdInline.EmphasisInline emphasis)
        {
            if (emphasis.DelimiterChar == '~')
            {
                return RunStyle.Strikethrough;
            }

            return emphasis.DelimiterCount >= 2 ? RunStyle.Bold : RunStyle.Italic;
        }

        private static string LinesOf(Md.LeafBlock block)
        {
            return block.Lines.Count == 0 ? string.Empty : block.Lines.ToString();
        }

        private static string FirstWord(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return string.Empty;
            }

            var trimmed = value.Trim();
            var space = trimmed.IndexOfAny(new[] { ' ', '\t' });
            return space < 0 ? trimmed : trimmed.Substring(0, space);
        }

        private string Format(string key, object argument)
        {
            return string.Format(CultureInfo.CurrentCulture, _strings.Get(key), argument);
        }
    }

    /// <summary>
    /// Flattens inline content to unstyled text, for the places that need a string rather than a
    /// fragment: the page title and image alt text.
    /// </summary>
    internal static class PlainText
    {
        public static string Of(IReadOnlyList<IInline> inlines)
        {
            var builder = new StringBuilder();
            Append(inlines, builder);
            return builder.ToString();
        }

        public static string Of(MdInline.ContainerInline container)
        {
            var builder = new StringBuilder();
            if (container != null)
            {
                foreach (var child in container)
                {
                    Append(child, builder);
                }
            }

            return builder.ToString();
        }

        private static void Append(IReadOnlyList<IInline> inlines, StringBuilder builder)
        {
            for (var i = 0; i < inlines.Count; i++)
            {
                var text = inlines[i] as TextRun;
                if (text != null)
                {
                    builder.Append(text.Text);
                    continue;
                }

                var code = inlines[i] as CodeRun;
                if (code != null)
                {
                    builder.Append(code.Text);
                    continue;
                }

                var styled = inlines[i] as StyledRun;
                if (styled != null)
                {
                    Append(styled.Children, builder);
                    continue;
                }

                var link = inlines[i] as HyperlinkRun;
                if (link != null)
                {
                    Append(link.Children, builder);
                    continue;
                }

                if (inlines[i] is LineBreakRun)
                {
                    builder.Append(' ');
                }
            }
        }

        private static void Append(MdInline.Inline inline, StringBuilder builder)
        {
            var literal = inline as MdInline.LiteralInline;
            if (literal != null)
            {
                builder.Append(literal.Content.ToString());
                return;
            }

            var code = inline as MdInline.CodeInline;
            if (code != null)
            {
                builder.Append(code.Content);
                return;
            }

            if (inline is MdInline.LineBreakInline)
            {
                builder.Append(' ');
                return;
            }

            var container = inline as MdInline.ContainerInline;
            if (container != null)
            {
                foreach (var child in container)
                {
                    Append(child, builder);
                }
            }
        }
    }
}
