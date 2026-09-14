using System;
using System.Collections.Generic;
using System.Globalization;
using System.Xml.Linq;

namespace Md2OneNote.Core.Rendering
{
    /// <summary>
    /// Pass 3: the block model plus already-resolved outcomes to a complete OneNote page. Pure and
    /// total — every failure it can meet is encoded in the outcomes it was handed, so it renders a
    /// fallback instead of throwing (FR-13, FR-14, DESIGN.md §3).
    /// </summary>
    public sealed class OneNoteXmlRenderer : IOneNoteXmlRenderer
    {
        private readonly ISyntaxHighlighter _highlighter;

        public OneNoteXmlRenderer()
            : this(ColorCodeSyntaxHighlighter.Instance)
        {
        }

        public OneNoteXmlRenderer(ISyntaxHighlighter highlighter)
        {
            if (highlighter == null) throw new ArgumentNullException(nameof(highlighter));

            _highlighter = highlighter;
        }

        public string Render(ParsedDocument document, RenderInputs inputs)
        {
            if (document == null) throw new ArgumentNullException(nameof(document));
            if (inputs == null) throw new ArgumentNullException(nameof(inputs));

            var context = new RenderContext(inputs, _highlighter);

            var page = new XElement(
                OneNote.Ns + "Page",
                new XAttribute(XNamespace.Xmlns + "one", OneNote.Ns.NamespaceName));

            // TagDef first, then the quick styles. That is the order OneNote writes
            // (docs/page-schema-notes.md §3), the reverse of what DESIGN.md assumed. Whether it
            // *rejects* the other order is still unknown and only a write can settle it; matching
            // what OneNote produces is the cheaper bet either way.
            page.Add(StyleTable.TagDefinition());

            foreach (var definition in StyleTable.Definitions())
            {
                page.Add(definition);
            }

            foreach (var meta in MetadataOf(inputs.Metadata))
            {
                page.Add(meta);
            }

            page.Add(new XElement(
                OneNote.Ns + "Title",
                Paragraph(StyleTable.PageTitle, XmlText.Sanitize(inputs.Title))));

            var content = new XElement(OneNote.Ns + "OEChildren");
            WriteBlocks(document.Body.Blocks, content, context);

            page.Add(new XElement(
                OneNote.Ns + "Outline",
                new XElement(
                    OneNote.Ns + "Position",
                    new XAttribute("x", Layout.OutlineX),
                    new XAttribute("y", Layout.OutlineY),
                    new XAttribute("z", 0)),
                new XElement(
                    OneNote.Ns + "Size",
                    new XAttribute("width", Layout.OutlineWidth),
                    new XAttribute("height", Layout.OutlineHeight)),
                content));

            // No page id is stamped here: the id is an interop concern, and leaving it out means
            // the whole conversion can succeed before anything exists in the notebook (NFR-1).
            return page.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>FR-17, DESIGN.md §7.1: what makes a page recognizable on a later import.</summary>
        private static IEnumerable<XElement> MetadataOf(PageMetadata metadata)
        {
            yield return Meta(PageMetadata.SourceKey, metadata.SourcePath);
            yield return Meta(PageMetadata.HashKey, metadata.SourceHash);
            yield return Meta(PageMetadata.VersionKey, metadata.ToolVersion);
            yield return Meta(PageMetadata.ImportedKey, metadata.ImportedUtc.ToUniversalTime()
                .ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture));
        }

        private static XElement Meta(string name, string content)
        {
            return new XElement(
                OneNote.Ns + "Meta",
                new XAttribute("name", name),
                new XAttribute("content", XmlText.Sanitize(content ?? string.Empty)));
        }

        private static void WriteBlocks(IReadOnlyList<IBlock> blocks, XElement container, RenderContext context)
        {
            if (!context.Descend())
            {
                return;
            }

            try
            {
                for (var i = 0; i < blocks.Count; i++)
                {
                    WriteBlock(blocks[i], container, context);
                }
            }
            finally
            {
                context.Ascend();
            }
        }

        private static void WriteBlock(IBlock block, XElement container, RenderContext context)
        {
            var heading = block as HeadingBlock;
            if (heading != null)
            {
                container.Add(Paragraph(StyleTable.Heading(heading.Level), InlineWriter.Write(heading.Content)));
                return;
            }

            var paragraph = block as ParagraphBlock;
            if (paragraph != null)
            {
                container.Add(Paragraph(context.ParagraphStyle, InlineWriter.Write(paragraph.Content)));
                return;
            }

            var quote = block as QuoteBlock;
            if (quote != null)
            {
                // Rendered flat, in the blockquote style, rather than as an OE that holds only
                // OEChildren. A quote reads the same either way, and nothing in the output depends
                // on a nesting shape Phase 0 has not yet confirmed OneNote accepts.
                using (context.EnterStyle(StyleTable.Quote))
                {
                    WriteBlocks(quote.Children, container, context);
                }

                return;
            }

            var list = block as ListBlock;
            if (list != null)
            {
                WriteList(list, container, context);
                return;
            }

            var diagram = block as DiagramBlock;
            if (diagram != null)
            {
                WriteDiagram(diagram, container, context);
                return;
            }

            var code = block as CodeBlock;
            if (code != null)
            {
                container.Add(CodeTable(code.Language, code.Code, context));
                return;
            }

            var image = block as ImageBlock;
            if (image != null)
            {
                WriteImage(image, container, context);
                return;
            }

            var table = block as TableBlock;
            if (table != null)
            {
                container.Add(WriteTable(table, context));
                return;
            }

            if (block is ThematicBreakBlock)
            {
                container.Add(ThematicBreak());
                return;
            }

            var footnotes = block as FootnotesBlock;
            if (footnotes != null)
            {
                WriteFootnotes(footnotes, container, context);
            }
        }

        private static void WriteList(ListBlock list, XElement container, RenderContext context)
        {
            using (context.EnterList())
            {
                WriteListItems(list, container, context);
            }
        }

        private static void WriteListItems(ListBlock list, XElement container, RenderContext context)
        {
            // Both the numbering style and the bullet glyph are chosen by nesting depth, because
            // that is what OneNote keys them on (docs/page-schema-notes.md §4). The previous
            // monotonic-counter approach gave the second ordered list on a page a different
            // numbering style, which was never the intent.
            var depth = context.ListDepth;
            var sequence = StyleTable.NumberSequence(depth);
            var bullet = StyleTable.Bullet(depth);

            for (var i = 0; i < list.Items.Count; i++)
            {
                var item = list.Items[i];
                var oe = new XElement(OneNote.Ns + "OE", new XAttribute("quickStyleIndex", context.ParagraphStyle));

                if (item.Task != TaskState.None)
                {
                    // A checkbox replaces the bullet, which is how OneNote renders its own To Do
                    // items rather than showing both.
                    oe.Add(new XElement(
                        OneNote.Ns + "Tag",
                        new XAttribute("index", StyleTable.TaskTag),
                        new XAttribute("completed", item.Task == TaskState.Checked ? "true" : "false"),
                        new XAttribute("disabled", "false")));
                }
                else if (list.Kind == ListKind.Ordered)
                {
                    oe.Add(new XElement(
                        OneNote.Ns + "List",
                        new XElement(
                            OneNote.Ns + "Number",
                            new XAttribute("numberSequence", sequence),
                            new XAttribute("numberFormat", "##."),
                            new XAttribute("font", "Calibri"),
                            new XAttribute("fontSize", "11"))));
                }
                else
                {
                    oe.Add(new XElement(
                        OneNote.Ns + "List",
                        new XElement(
                            OneNote.Ns + "Bullet",
                            new XAttribute("bullet", bullet),
                            new XAttribute("fontSize", "11"))));
                }

                // The item's own text belongs on the bulleted line; everything after it — nested
                // lists, extra paragraphs, code — hangs beneath it.
                var start = 0;
                var lead = item.Content.Count > 0 ? item.Content[0] as ParagraphBlock : null;
                if (lead != null)
                {
                    start = 1;
                }

                oe.Add(Text(lead == null ? string.Empty : InlineWriter.Write(lead.Content)));

                if (item.Content.Count > start && context.Descend())
                {
                    var children = new XElement(OneNote.Ns + "OEChildren");
                    try
                    {
                        for (var j = start; j < item.Content.Count; j++)
                        {
                            WriteBlock(item.Content[j], children, context);
                        }
                    }
                    finally
                    {
                        context.Ascend();
                    }

                    if (children.HasElements)
                    {
                        oe.Add(children);
                    }
                }

                container.Add(oe);
            }
        }

        private static void WriteDiagram(DiagramBlock diagram, XElement container, RenderContext context)
        {
            DiagramOutcome outcome;
            var known = context.Inputs.Diagrams.TryGetValue(diagram.Key, out outcome) && outcome != null;

            if (known && outcome.Succeeded && outcome.Png != null)
            {
                // Captured at 2x, so half the pixel size is the natural size (DESIGN.md §8.1),
                // and like any image it is fitted to the content column, never enlarged: a
                // five-participant sequence diagram is 1236 points wide at natural size, twice
                // the text. Fitted, the 2x capture still leaves it sharp when the user enlarges it.
                var width = outcome.WidthPx / 2.0;
                var height = outcome.HeightPx / 2.0;
                var fit = width > Layout.ContentWidth ? Layout.ContentWidth / width : 1.0;
                container.Add(Image("png", outcome.Png, width * fit, height * fit));
                return;
            }

            // FR-14: the source survives as a code block, with the reason under it. The import
            // still produced a page, and the user can see both what failed and why.
            container.Add(CodeTable(diagram.Language, diagram.Source, context));

            if (known && !string.IsNullOrEmpty(outcome.FailureMessage))
            {
                container.Add(Paragraph(StyleTable.Cite, XmlText.Sanitize(outcome.FailureMessage)));
            }
        }

        private static void WriteImage(ImageBlock image, XElement container, RenderContext context)
        {
            AssetOutcome outcome;
            var known = context.Inputs.Assets.TryGetValue(image.Key, out outcome) && outcome != null;

            if (known && outcome.Succeeded && outcome.Bytes != null)
            {
                var scale = Fit(outcome.WidthPx);
                container.Add(Image(outcome.Format, outcome.Bytes, outcome.WidthPx * scale, outcome.HeightPx * scale));
                return;
            }

            // FR-10: a missing image is a visible placeholder, never a silent gap.
            var description = string.IsNullOrEmpty(image.AltText) ? image.RawPath : image.AltText;
            var reason = known && !string.IsNullOrEmpty(outcome.FailureMessage)
                ? outcome.FailureMessage
                : string.Empty;

            container.Add(Paragraph(StyleTable.Cite, XmlText.Sanitize(string.Format(
                CultureInfo.CurrentCulture,
                context.Inputs.Strings.Get(CoreStringKeys.ImageUnavailable),
                description,
                reason))));
        }

        /// <summary>IMPLEMENTATION.md §9.1: nothing is enlarged; anything too wide is scaled down.</summary>
        private static double Fit(int widthPx)
        {
            return widthPx > Layout.ContentWidth ? Layout.ContentWidth / (double)widthPx : 1.0;
        }

        private static XElement Image(string format, byte[] bytes, double width, double height)
        {
            return new XElement(
                OneNote.Ns + "OE",
                new XElement(
                    OneNote.Ns + "Image",
                    new XAttribute("format", string.IsNullOrEmpty(format) ? "png" : format),
                    new XElement(
                        OneNote.Ns + "Size",
                        new XAttribute("width", Number(width < 1 ? 1 : width)),
                        new XAttribute("height", Number(height < 1 ? 1 : height)),
                        new XAttribute("isSetByUser", "true")),
                    new XElement(OneNote.Ns + "Data", Convert.ToBase64String(bytes))));
        }

        /// <summary>
        /// IMPLEMENTATION.md §5.2: a code block is a single shaded cell with one <c>one:OE</c> per
        /// source line, because OneNote has no code element and a paragraph per line is what keeps
        /// the lines from being reflowed.
        /// </summary>
        private static XElement CodeTable(string language, string code, RenderContext context)
        {
            var cell = new XElement(OneNote.Ns + "OEChildren");

            foreach (var line in context.HighlightLines(language, code))
            {
                cell.Add(new XElement(
                    OneNote.Ns + "OE",
                    new XAttribute("quickStyleIndex", StyleTable.Code),
                    Text(InlineWriter.WriteCodeLine(line))));
            }

            if (!cell.HasElements)
            {
                cell.Add(Paragraph(StyleTable.Code, string.Empty));
            }

            return SingleCellTable(cell, "#F2F2F2");
        }

        /// <summary>
        /// A thematic break is vertical space, nothing more. OneNote has no horizontal rule, and
        /// the shaded one-cell table that stood in for one showed up as a small grey box under
        /// whatever preceded it (first real import, 2026-09-14) — worse than no rule at all.
        /// The section boundary the author meant is still there as a blank line.
        /// </summary>
        private static XElement ThematicBreak()
        {
            return Paragraph(StyleTable.Body, string.Empty);
        }

        /// <summary>
        /// A table is paragraph content, never a paragraph: OneNote's content model puts
        /// <c>one:Table</c> inside <c>one:OE</c>, and rejects the page outright when it is placed
        /// directly under <c>one:OEChildren</c> — "Element Table is unexpected according to
        /// content model of parent element OEChildren. Expecting: OE, HTMLBlock" (first real
        /// import, 2026-09-14). The golden files had enshrined the wrong shape, which is why an
        /// OneNote-validated sample matters more than any number of them.
        /// </summary>
        private static XElement InParagraph(XElement table)
        {
            return new XElement(OneNote.Ns + "OE", table);
        }

        private static XElement SingleCellTable(XElement cellContent, string shading)
        {
            return InParagraph(new XElement(
                OneNote.Ns + "Table",
                new XAttribute("bordersVisible", "false"),
                new XElement(
                    OneNote.Ns + "Columns",
                    new XElement(
                        OneNote.Ns + "Column",
                        new XAttribute("index", 0),
                        new XAttribute("width", Layout.ContentWidth))),
                new XElement(
                    OneNote.Ns + "Row",
                    new XElement(
                        OneNote.Ns + "Cell",
                        new XAttribute("shadingColor", shading),
                        cellContent))));
        }

        private static XElement WriteTable(TableBlock table, RenderContext context)
        {
            var element = new XElement(
                OneNote.Ns + "Table",
                new XAttribute("bordersVisible", "true"),
                new XAttribute("hasHeaderRow", table.HasHeaderRow ? "true" : "false"));

            // Even distribution, per IMPLEMENTATION.md §9.1 — deliberately not clever.
            var columns = new XElement(OneNote.Ns + "Columns");
            var width = Layout.ContentWidth / table.ColumnCount;
            for (var i = 0; i < table.ColumnCount; i++)
            {
                columns.Add(new XElement(
                    OneNote.Ns + "Column",
                    new XAttribute("index", i),
                    new XAttribute("width", width)));
            }

            element.Add(columns);

            for (var r = 0; r < table.Rows.Count; r++)
            {
                var row = new XElement(OneNote.Ns + "Row");

                // Short rows are padded: the schema needs every row to have the same cell count,
                // and a ragged Markdown table is perfectly legal input.
                for (var c = 0; c < table.ColumnCount; c++)
                {
                    var children = new XElement(OneNote.Ns + "OEChildren");
                    if (c < table.Rows[r].Cells.Count)
                    {
                        WriteBlocks(table.Rows[r].Cells[c].Content, children, context);
                    }

                    if (!children.HasElements)
                    {
                        children.Add(Paragraph(context.ParagraphStyle, string.Empty));
                    }

                    row.Add(new XElement(OneNote.Ns + "Cell", children));
                }

                element.Add(row);
            }

            return InParagraph(element);
        }

        private static void WriteFootnotes(FootnotesBlock footnotes, XElement container, RenderContext context)
        {
            // The heading comes from the catalog, not from a literal "Notes" (NFR-11).
            container.Add(Paragraph(
                StyleTable.Heading(2),
                XmlText.Sanitize(context.Inputs.Strings.Get(CoreStringKeys.FootnotesHeading))));

            using (context.EnterStyle(StyleTable.Cite))
            {
                for (var i = 0; i < footnotes.Notes.Count; i++)
                {
                    WriteBlocks(Numbered(footnotes.Notes[i]), container, context);
                }
            }
        }

        /// <summary>
        /// Puts the note's number at the head of its first paragraph, so that the definition can be
        /// matched to the <c>[n]</c> left behind at the reference.
        /// </summary>
        private static IReadOnlyList<IBlock> Numbered(FootnoteDefinition note)
        {
            var marker = new TextRun("[" + note.Number.ToString(CultureInfo.InvariantCulture) + "] ");
            var blocks = new List<IBlock>(note.Content);

            var lead = blocks.Count > 0 ? blocks[0] as ParagraphBlock : null;
            if (lead == null)
            {
                blocks.Insert(0, new ParagraphBlock(new IInline[] { marker }));
                return blocks;
            }

            var inlines = new List<IInline> { marker };
            inlines.AddRange(lead.Content);
            blocks[0] = new ParagraphBlock(inlines);
            return blocks;
        }

        private static XElement Paragraph(int quickStyleIndex, string fragment)
        {
            return new XElement(
                OneNote.Ns + "OE",
                new XAttribute("quickStyleIndex", quickStyleIndex),
                Text(fragment));
        }

        private static XElement Text(string fragment)
        {
            return new XElement(OneNote.Ns + "T", new XCData(fragment ?? string.Empty));
        }

        private static string Number(double value)
        {
            return Math.Round(value, 1).ToString("0.#", CultureInfo.InvariantCulture);
        }
    }
}
