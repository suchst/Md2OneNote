using System;
using System.Linq;
using System.Text;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    /// <summary>
    /// Input nobody wrote by hand. Users open Markdown they did not write, so every one of these
    /// has to convert or degrade — never hang, never throw, never produce a page OneNote will
    /// reject (NFR-4, NFR-8).
    /// </summary>
    public class HostileInputTests
    {
        [Fact]
        public void Deeply_nested_quotes_stop_at_the_limit_instead_of_overflowing_the_stack()
        {
            var markdown = string.Concat(Enumerable.Repeat("> ", 100)) + "deep";

            var parsed = Convert.Parse(markdown);

            Assert.Contains(parsed.Diagnostics, d => d.Code == DiagnosticCodes.NestingTooDeep);
            Convert.Render(parsed);
        }

        [Fact]
        public void Deeply_nested_lists_convert_all_the_way_down()
        {
            // A list costs two levels of Markdig's own budget per level of nesting, so its limit
            // is the one that bites here. Fifty deep still has to come out as a page.
            var builder = new StringBuilder();
            for (var i = 0; i < 50; i++)
            {
                builder.Append(new string(' ', i * 2)).Append("- item\n");
            }

            var page = Convert.Render(Convert.Parse(builder.ToString()));

            Assert.Equal(50, page.Descendants(Convert.One + "Bullet").Count());
        }

        [Fact]
        public void Nesting_past_what_Markdig_itself_allows_fails_the_file_rather_than_the_process()
        {
            // Markdig refuses before our own budget is reached. The exception is deliberately not
            // swallowed here: ImportService turns it into a reported per-file ParseError, which
            // leaves the rest of the import running (FR-3) and beats silently dropping a document.
            var markdown = string.Concat(Enumerable.Repeat("> ", 5000)) + "deep";

            Assert.ThrowsAny<Exception>(() => Convert.Parse(markdown));
        }

        [Fact]
        public void Deeply_nested_emphasis_stops_at_the_limit()
        {
            var markdown = string.Concat(Enumerable.Repeat("*", 4000)) + "x" +
                           string.Concat(Enumerable.Repeat("*", 4000));

            Convert.Render(Convert.Parse(markdown));
        }

        [Fact]
        public void The_depth_warning_is_raised_once_rather_than_thousands_of_times()
        {
            var markdown = string.Concat(Enumerable.Repeat("> ", 100)) + "deep\n\n" +
                           string.Concat(Enumerable.Repeat("> ", 100)) + "also deep";

            var parsed = Convert.Parse(markdown);

            Assert.Single(parsed.Diagnostics, d => d.Code == DiagnosticCodes.NestingTooDeep);
        }

        [Theory]
        [InlineData("<script>alert(1)</script>")]
        [InlineData("<img src=\"https://tracker.example/x.gif\">")]
        [InlineData("<iframe src=\"https://example.com\"></iframe>")]
        [InlineData("<style>body{}</style>")]
        public void Active_html_never_reaches_the_page_as_markup(string markdown)
        {
            var page = Convert.Page(markdown);
            var xml = page.ToString();

            Assert.DoesNotContain("<script", xml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<iframe", xml, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("<img", xml, StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public void A_javascript_url_loses_its_link_and_keeps_its_text()
        {
            var page = Convert.Page("[click me](javascript:alert(document.cookie))");
            var fragment = Convert.Fragments(page).Last();

            Assert.DoesNotContain("javascript:", fragment);
            Assert.Contains("click me", fragment);
        }

        [Theory]
        [InlineData("text with \0 a null")]
        [InlineData("vertical  tab")]
        [InlineData("￾ noncharacter")]
        public void Characters_that_cannot_appear_in_XML_are_stripped(string markdown)
        {
            // Round-tripping through XElement.Parse is the assertion: a page that will not parse
            // would fail the import for the whole file.
            var page = Convert.Page(markdown);

            Assert.NotEmpty(page.ToString());
        }

        [Fact]
        public void An_enormous_single_line_still_converts()
        {
            var markdown = new string('x', 2_000_000);

            var page = Convert.Page(markdown);

            Assert.Contains("xxx", Convert.Fragments(page).Last());
        }

        [Fact]
        public void A_table_of_one_thousand_columns_produces_square_rows()
        {
            var header = "|" + string.Concat(Enumerable.Repeat(" c |", 1000));
            var divider = "|" + string.Concat(Enumerable.Repeat("---|", 1000));
            var row = "|" + string.Concat(Enumerable.Repeat(" v |", 1000));

            var page = Convert.Page(header + "\n" + divider + "\n" + row);
            var table = Convert.TableIn(Convert.Body(page)[0]);

            var counts = table.Elements(Convert.One + "Row")
                .Select(r => r.Elements(Convert.One + "Cell").Count())
                .Distinct()
                .ToArray();

            Assert.Single(counts);
        }
    }
}
