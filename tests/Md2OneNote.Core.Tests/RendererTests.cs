using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Md2OneNote.Core.Rendering;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    public class RendererTests
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47 };

        [Fact]
        public void The_page_carries_its_own_style_table_and_the_metadata_a_re_import_needs()
        {
            var page = Convert.Page("text");

            Assert.Equal(11, page.Elements(Convert.One + "QuickStyleDef").Count());
            Assert.Single(page.Elements(Convert.One + "TagDef"));

            var meta = page.Elements(Convert.One + "Meta")
                .ToDictionary(e => e.Attribute("name").Value, e => e.Attribute("content").Value);

            Assert.Equal(@"C:\notes\test.md", meta[PageMetadata.SourceKey]);
            Assert.StartsWith("sha256:", meta[PageMetadata.HashKey]);
            Assert.Equal("1.0.0-test", meta[PageMetadata.VersionKey]);
            Assert.Equal("2026-08-04T09:30:00Z", meta[PageMetadata.ImportedKey]);
        }

        [Fact]
        public void The_title_is_a_page_title_styled_oe_inside_one_Title()
        {
            var page = Convert.Page("# Hello World\n\nbody");

            var title = page.Element(Convert.One + "Title");
            var oe = title.Element(Convert.One + "OE");

            Assert.Equal("0", Convert.Style(oe));
            Assert.Equal("Hello World", oe.Element(Convert.One + "T").Value);
        }

        [Theory]
        [InlineData("# h", "1")]
        [InlineData("## h", "2")]
        [InlineData("###### h", "6")]
        public void Headings_map_to_their_own_quick_style(string markdown, string expected)
        {
            // A leading h1 would become the page title, so each case is preceded by body text.
            var page = Convert.Page("intro\n\n" + markdown);

            Assert.Equal(expected, Convert.Style(Convert.Body(page)[1]));
        }

        [Fact]
        public void Inline_formatting_becomes_the_span_markup_OneNote_understands()
        {
            var page = Convert.Page("**b** *i* ~~s~~ `c` [t](https://e.com)");
            var fragment = Convert.Fragments(page).Last();

            Assert.Contains("<span style='font-weight:bold'>b</span>", fragment);
            Assert.Contains("<span style='font-style:italic'>i</span>", fragment);
            Assert.Contains("<span style='text-decoration:line-through'>s</span>", fragment);
            Assert.Contains("<span style='font-family:Consolas;background-color:#F2F2F2'>c</span>", fragment);
            Assert.Contains("<a href=\"https://e.com\">t</a>", fragment);
        }

        [Fact]
        public void User_text_is_escaped_once_and_cannot_close_the_cdata_section()
        {
            var page = Convert.Page("a <b & c> d ]]> e");
            var fragment = Convert.Fragments(page).Last();

            Assert.Contains("&lt;b &amp; c&gt;", fragment);
            Assert.Contains("]]&gt;", fragment);
            Assert.DoesNotContain("&amp;lt;", fragment);
        }

        [Fact]
        public void A_url_is_escaped_for_the_attribute_it_lands_in()
        {
            var page = Convert.Page("[t](https://e.com/?a=1&b=\"2\")");
            var fragment = Convert.Fragments(page).Last();

            Assert.Contains("href=\"https://e.com/?a=1&amp;b=&quot;2&quot;\"", fragment);
        }

        [Fact]
        public void Characters_XML_cannot_represent_are_dropped_rather_than_breaking_the_page()
        {
            // A page that will not parse is a failed import, and hostile input is expected (NFR-4).
            var page = Convert.Page("before\u0007after");

            Assert.Equal("beforeafter", Convert.Fragments(page).Last());
        }

        [Fact]
        public void Separate_ordered_lists_get_separate_number_sequences()
        {
            var page = Convert.Page("1. a\n2. b\n\ntext\n\n1. c\n2. d");

            var sequences = page.Descendants(Convert.One + "Number")
                .Select(n => n.Attribute("numberSequence").Value)
                .Distinct()
                .ToArray();

            Assert.Equal(2, sequences.Length);
        }

        [Fact]
        public void A_nested_list_hangs_beneath_its_parent_item()
        {
            var page = Convert.Page("- one\n  - nested\n");

            var item = Convert.Body(page)[0];
            var children = item.Element(Convert.One + "OEChildren");

            Assert.NotNull(children);
            Assert.Equal("nested", children.Descendants(Convert.One + "T").Single().Value);

            // The parent's own text stays on the parent, ahead of the children.
            Assert.Equal("one", item.Element(Convert.One + "T").Value);
        }

        [Fact]
        public void Task_items_get_a_tag_instead_of_a_bullet()
        {
            var page = Convert.Page("- [x] done\n- plain");
            var items = Convert.Body(page);

            var tag = items[0].Element(Convert.One + "Tag");
            Assert.Equal("true", tag.Attribute("completed").Value);
            Assert.Null(items[0].Element(Convert.One + "List"));

            Assert.NotNull(items[1].Element(Convert.One + "List"));
        }

        [Fact]
        public void A_code_block_is_a_shaded_cell_with_one_line_per_row_and_indentation_preserved()
        {
            var page = Convert.Page("```python\ndef main():\n    pass\n```");

            var table = Convert.Body(page)[0];
            Assert.Equal("Table", table.Name.LocalName);
            Assert.Equal("#F2F2F2", table.Descendants(Convert.One + "Cell").Single().Attribute("shadingColor").Value);

            var lines = table.Descendants(Convert.One + "T").Select(t => t.Value).ToArray();
            Assert.Equal(2, lines.Length);
            Assert.Equal("def main():", lines[0]);
            Assert.Equal("&nbsp;&nbsp;&nbsp;&nbsp;pass", lines[1]);
        }

        [Fact]
        public void A_table_pads_short_rows_and_declares_its_header()
        {
            var page = Convert.Page("| a | b | c |\n|---|---|---|\n| 1 | 2 |\n");

            var table = Convert.Body(page)[0];
            Assert.Equal("true", table.Attribute("hasHeaderRow").Value);
            Assert.Equal(3, table.Element(Convert.One + "Columns").Elements().Count());

            foreach (var row in table.Elements(Convert.One + "Row"))
            {
                Assert.Equal(3, row.Elements(Convert.One + "Cell").Count());
            }
        }

        [Fact]
        public void A_rendered_diagram_is_embedded_at_half_the_captured_size()
        {
            var parsed = Convert.Parse("```mermaid\ngraph TD;\n```", "mermaid");
            var outcomes = new Dictionary<string, DiagramOutcome>
            {
                { parsed.Diagrams[0].Key, DiagramOutcome.Success(Png, 800, 600) }
            };

            var page = Convert.Render(parsed, outcomes);
            var image = page.Descendants(Convert.One + "Image").Single();
            var size = image.Element(Convert.One + "Size");

            Assert.Equal("png", image.Attribute("format").Value);
            Assert.Equal("400", size.Attribute("width").Value);
            Assert.Equal("300", size.Attribute("height").Value);
            Assert.Equal(System.Convert.ToBase64String(Png), image.Element(Convert.One + "Data").Value);
        }

        [Fact]
        public void A_failed_diagram_falls_back_to_its_source_plus_the_reason()
        {
            var parsed = Convert.Parse("```mermaid\ngraph TD;\n```", "mermaid");
            var outcomes = new Dictionary<string, DiagramOutcome>
            {
                { parsed.Diagrams[0].Key, DiagramOutcome.Failure("renderer timed out") }
            };

            var page = Convert.Render(parsed, outcomes);
            var body = Convert.Body(page);

            Assert.Equal("Table", body[0].Name.LocalName);
            Assert.Contains("graph TD;", body[0].Descendants(Convert.One + "T").First().Value);
            Assert.Equal("renderer timed out", body[1].Element(Convert.One + "T").Value);
            Assert.Equal("10", Convert.Style(body[1]));
        }

        [Fact]
        public void An_unresolved_diagram_still_renders_its_source()
        {
            // The outcome dictionary is empty: a bug upstream, but never a lost code fence.
            var page = Convert.Page("```mermaid\ngraph TD;\n```", "mermaid");

            Assert.Equal("Table", Convert.Body(page)[0].Name.LocalName);
        }

        [Fact]
        public void An_embedded_image_is_scaled_down_to_the_content_width_but_never_up()
        {
            var parsed = Convert.Parse("![wide](wide.png)\n\n![small](small.png)");
            var assets = new Dictionary<string, AssetOutcome>
            {
                { parsed.Assets[0].Key, AssetOutcome.Success(Png, "png", 1320, 660) },
                { parsed.Assets[1].Key, AssetOutcome.Success(Png, "jpg", 100, 50) }
            };

            var page = Convert.Render(parsed, null, assets);
            var sizes = page.Descendants(Convert.One + "Size")
                .Where(s => s.Parent.Name.LocalName == "Image")
                .ToArray();

            Assert.Equal("660", sizes[0].Attribute("width").Value);
            Assert.Equal("330", sizes[0].Attribute("height").Value);
            Assert.Equal("100", sizes[1].Attribute("width").Value);
        }

        [Fact]
        public void A_missing_image_leaves_a_visible_placeholder()
        {
            var parsed = Convert.Parse("![The chart](chart.png)");
            var assets = new Dictionary<string, AssetOutcome>
            {
                { parsed.Assets[0].Key, AssetOutcome.Failure("file not found") }
            };

            var page = Convert.Render(parsed, null, assets);

            Assert.Empty(page.Descendants(Convert.One + "Image"));
            Assert.Equal("[unavailable: The chart / file not found]", Convert.Fragments(page).Last());
        }

        [Fact]
        public void Block_quotes_take_the_quote_style()
        {
            var page = Convert.Page("> quoted\n\nplain");
            var body = Convert.Body(page);

            Assert.Equal("8", Convert.Style(body[0]));
            Assert.Equal("7", Convert.Style(body[1]));
        }

        [Fact]
        public void Footnotes_are_numbered_at_both_ends_under_a_localized_heading()
        {
            var page = Convert.Page("Claim[^a].\n\n[^a]: The note.");
            var fragments = Convert.Fragments(page);

            Assert.Contains(fragments, f => f == "Claim[1].");
            Assert.Contains(fragments, f => f == "NOTES");
            Assert.Contains(fragments, f => f == "[1] The note.");
        }

        [Fact]
        public void Rendering_is_deterministic()
        {
            const string markdown = "# T\n\n- a\n- b\n\n| x | y |\n|---|---|\n| 1 | 2 |\n\n```cs\nvar x = 1;\n```";

            var first = Convert.Render(Convert.Parse(markdown)).ToString();
            var second = Convert.Render(Convert.Parse(markdown)).ToString();

            Assert.Equal(first, second);
        }

        [Fact]
        public void Rendering_an_empty_document_still_produces_a_well_formed_page()
        {
            var page = Convert.Page(string.Empty);

            Assert.Equal("Page", page.Name.LocalName);
            Assert.NotNull(page.Element(Convert.One + "Outline"));
            Assert.Empty(Convert.Body(page));
        }

        [Fact]
        public void The_renderer_rejects_missing_arguments_rather_than_producing_half_a_page()
        {
            var renderer = new OneNoteXmlRenderer(PlainSyntaxHighlighter.Instance);

            Assert.Throws<ArgumentNullException>(() => renderer.Render(null, null));
        }
    }
}
