using System.Collections.Generic;
using System.Linq;
using System.Text;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    public class ParserTests
    {
        [Fact]
        public void Front_matter_title_wins_over_a_leading_heading()
        {
            var parsed = Convert.Parse("---\ntitle: From Front Matter\n---\n\n# From Heading\n\nBody.");

            Assert.Equal("From Front Matter", parsed.SuggestedTitle);

            // The heading stays, because it is not the thing that became the title.
            var heading = Assert.IsType<HeadingBlock>(parsed.Body.Blocks[0]);
            Assert.Equal(1, heading.Level);
        }

        [Theory]
        [InlineData("---\ntitle: \"Quoted\"\n---\n\ntext", "Quoted")]
        [InlineData("---\ntitle: 'Single'\n---\n\ntext", "Single")]
        [InlineData("---\nauthor: someone\n---\n\ntext", null)]
        [InlineData("---\ntitle: |\n  block\n---\n\ntext", null)]
        [InlineData("---\nnested:\n  title: Inner\n---\n\ntext", null)]
        public void Front_matter_title_is_read_only_when_it_is_an_unambiguous_scalar(string markdown, string expected)
        {
            Assert.Equal(expected, Convert.Parse(markdown).SuggestedTitle);
        }

        [Fact]
        public void Leading_h1_becomes_the_title_and_leaves_the_body()
        {
            var parsed = Convert.Parse("# The Title\n\nBody text.");

            Assert.Equal("The Title", parsed.SuggestedTitle);
            Assert.Single(parsed.Body.Blocks);
            Assert.IsType<ParagraphBlock>(parsed.Body.Blocks[0]);
        }

        [Fact]
        public void An_h1_that_is_not_first_is_ordinary_content()
        {
            var parsed = Convert.Parse("Intro.\n\n# Later Heading");

            Assert.Null(parsed.SuggestedTitle);
            Assert.Equal(2, parsed.Body.Blocks.Count);
        }

        [Fact]
        public void Front_matter_is_never_rendered_as_a_code_block()
        {
            // YamlFrontMatterBlock derives from CodeBlock, so this is a real regression risk.
            var parsed = Convert.Parse("---\ntitle: T\n---\n\nBody.");

            Assert.Single(parsed.Body.Blocks);
            Assert.IsType<ParagraphBlock>(parsed.Body.Blocks[0]);
        }

        [Fact]
        public void A_registered_fence_language_becomes_a_diagram_request()
        {
            var parsed = Convert.Parse("```mermaid\ngraph TD;\n```", "mermaid");

            var request = Assert.Single(parsed.Diagrams);
            Assert.Equal("mermaid", request.Language);
            Assert.Equal("graph TD;", request.Source.TrimEnd('\n'));

            var block = Assert.IsType<DiagramBlock>(parsed.Body.Blocks[0]);
            Assert.Equal(request.Key, block.Key);
        }

        [Fact]
        public void Identical_diagrams_are_requested_once_but_rendered_at_every_site()
        {
            var parsed = Convert.Parse("```mermaid\nA\n```\n\ntext\n\n```mermaid\nA\n```", "mermaid");

            Assert.Single(parsed.Diagrams);
            Assert.Equal(2, parsed.Body.Blocks.OfType<DiagramBlock>().Count());
        }

        [Fact]
        public void An_unregistered_fence_language_is_a_code_block()
        {
            var parsed = Convert.Parse("```rust\nfn main() {}\n```", "mermaid");

            Assert.Empty(parsed.Diagrams);
            var code = Assert.IsType<CodeBlock>(parsed.Body.Blocks[0]);
            Assert.Equal("rust", code.Language);
        }

        [Fact]
        public void Image_paths_reach_the_request_exactly_as_written()
        {
            var parsed = Convert.Parse("![A diagram](../shared/img.png)");

            var request = Assert.Single(parsed.Assets);
            Assert.Equal("../shared/img.png", request.RawPath);
            Assert.Equal("A diagram", request.AltText);
        }

        [Fact]
        public void An_image_inside_a_paragraph_splits_it_in_place()
        {
            var parsed = Convert.Parse("Before ![alt](a.png) after.");

            Assert.Collection(
                parsed.Body.Blocks,
                b => Assert.Equal("Before ", TextOf(b)),
                b => Assert.IsType<ImageBlock>(b),
                b => Assert.Equal(" after.", TextOf(b)));
        }

        [Fact]
        public void Raw_html_blocks_are_dropped_and_reported()
        {
            var parsed = Convert.Parse("Before\n\n<div onclick=\"x()\">hi</div>\n\nAfter");

            Assert.Equal(2, parsed.Body.Blocks.Count);
            var diagnostic = Assert.Single(parsed.Diagnostics);
            Assert.Equal(DiagnosticCodes.HtmlBlockDropped, diagnostic.Code);
            Assert.Equal(DiagnosticSeverity.Warning, diagnostic.Severity);
        }

        [Fact]
        public void Inline_html_survives_as_text()
        {
            var parsed = Convert.Parse("a <sup>b</sup> c");

            var paragraph = Assert.IsType<ParagraphBlock>(parsed.Body.Blocks[0]);
            Assert.Contains(paragraph.Content.OfType<TextRun>(), r => r.Text == "<sup>");
            Assert.Empty(parsed.Diagnostics);
        }

        [Fact]
        public void Task_list_state_is_read_and_the_checkbox_text_is_not_duplicated()
        {
            var parsed = Convert.Parse("- [ ] todo\n- [x] done\n- plain");

            var list = Assert.IsType<ListBlock>(parsed.Body.Blocks[0]);
            Assert.Equal(TaskState.Unchecked, list.Items[0].Task);
            Assert.Equal(TaskState.Checked, list.Items[1].Task);
            Assert.Equal(TaskState.None, list.Items[2].Task);
            Assert.Equal("todo", TextOf(list.Items[0].Content[0]));
        }

        [Fact]
        public void Ordered_and_nested_lists_keep_their_structure()
        {
            var parsed = Convert.Parse("1. one\n2. two\n   - nested\n");

            var list = Assert.IsType<ListBlock>(parsed.Body.Blocks[0]);
            Assert.Equal(ListKind.Ordered, list.Kind);
            Assert.Equal(2, list.Items.Count);

            var nested = Assert.IsType<ListBlock>(list.Items[1].Content[1]);
            Assert.Equal(ListKind.Bulleted, nested.Kind);
        }

        [Fact]
        public void Footnote_references_are_numbered_and_the_definitions_are_collected()
        {
            var parsed = Convert.Parse("Claim[^a] and another[^b].\n\n[^a]: First note.\n[^b]: Second note.");

            var paragraph = Assert.IsType<ParagraphBlock>(parsed.Body.Blocks[0]);
            var references = paragraph.Content.OfType<FootnoteReferenceRun>().Select(r => r.Number).ToArray();
            Assert.Equal(new[] { 1, 2 }, references);

            var notes = Assert.IsType<FootnotesBlock>(parsed.Body.Blocks[parsed.Body.Blocks.Count - 1]);
            Assert.Equal(new[] { 1, 2 }, notes.Notes.Select(n => n.Number).ToArray());
        }

        [Theory]
        [InlineData("[x](https://example.com)", true)]
        [InlineData("[x](http://example.com)", true)]
        [InlineData("[x](mailto:a@b.c)", true)]
        [InlineData("[x](./design.md)", false)]
        [InlineData("[x](file:///C:/secret.txt)", false)]
        [InlineData("[x](javascript:alert(1))", false)]
        public void Only_schemes_we_are_willing_to_emit_stay_links(string markdown, bool expectLink)
        {
            var parsed = Convert.Parse(markdown);
            var paragraph = Assert.IsType<ParagraphBlock>(parsed.Body.Blocks[0]);

            Assert.Equal(expectLink, paragraph.Content.OfType<HyperlinkRun>().Any());
            Assert.Equal("x", TextOf(paragraph));
        }

        [Fact]
        public void Tables_report_the_widest_row_so_ragged_input_still_renders()
        {
            var parsed = Convert.Parse("| a | b | c |\n|---|---|---|\n| 1 | 2 |\n");

            var table = Assert.IsType<TableBlock>(parsed.Body.Blocks[0]);
            Assert.Equal(3, table.ColumnCount);
            Assert.True(table.HasHeaderRow);

            // Markdig squares the rows off itself. The renderer pads anyway, because TableBlock is
            // a public model that nothing stops a caller from building ragged.
            Assert.All(table.Rows, row => Assert.Equal(3, row.Cells.Count));
        }

        [Fact]
        public void An_empty_document_parses_to_nothing_rather_than_failing()
        {
            var parsed = Convert.Parse(string.Empty);

            Assert.Empty(parsed.Body.Blocks);
            Assert.Null(parsed.SuggestedTitle);
            Assert.Empty(parsed.Diagnostics);
        }

        private static string TextOf(IBlock block)
        {
            var paragraph = block as ParagraphBlock;
            return paragraph == null ? null : TextOf(paragraph);
        }

        private static string TextOf(ParagraphBlock paragraph)
        {
            var builder = new StringBuilder();
            Append(paragraph.Content, builder);
            return builder.ToString();
        }

        private static void Append(IReadOnlyList<IInline> inlines, StringBuilder builder)
        {
            foreach (var inline in inlines)
            {
                var text = inline as TextRun;
                if (text != null)
                {
                    builder.Append(text.Text);
                    continue;
                }

                var styled = inline as StyledRun;
                if (styled != null)
                {
                    Append(styled.Children, builder);
                    continue;
                }

                var link = inline as HyperlinkRun;
                if (link != null)
                {
                    Append(link.Children, builder);
                }
            }
        }
    }
}
