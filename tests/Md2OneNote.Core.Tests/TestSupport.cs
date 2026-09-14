using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Md2OneNote.Core.Parsing;
using Md2OneNote.Core.Rendering;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    /// <summary>
    /// An invariant catalog. Conversion output has to be independent of the UI language (NFR-11),
    /// so pinning the strings here is what lets golden files assert on it at all.
    /// </summary>
    internal sealed class TestCatalog : IStringCatalog
    {
        private static readonly Dictionary<string, string> Values = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            { CoreStringKeys.FootnotesHeading, "NOTES" },
            { CoreStringKeys.ImageUnavailable, "[unavailable: {0} / {1}]" },
            { CoreStringKeys.HtmlBlockDropped, "html removed at line {0}" },
            { CoreStringKeys.NestingTooDeep, "nesting past {0} levels dropped" },

            // Deliberately terse, and every one names the reference: a user has to be able to tell
            // which line of their document a refusal is about.
            { CoreStringKeys.AssetInvalidPath, "invalid path: {0}" },
            { CoreStringKeys.AssetRemoteUrl, "remote url: {0}" },
            { CoreStringKeys.AssetAbsolutePath, "absolute path: {0}" },
            { CoreStringKeys.AssetNetworkPath, "network path: {0}" },
            { CoreStringKeys.AssetUnsupportedType, "unsupported type: {0}" },
            { CoreStringKeys.AssetOutsideDocument, "outside document: {0}" },
            { CoreStringKeys.AssetTooLarge, "too large: {0} / {1}" },
            { CoreStringKeys.AssetNotAnImage, "not an image: {0}" },
            { CoreStringKeys.AssetNotFound, "not found: {0}" },
            { CoreStringKeys.AssetUnreadable, "unreadable: {0} / {1}" }
        };

        public static TestCatalog Instance { get; } = new TestCatalog();

        public string Get(string key)
        {
            string value;
            return Values.TryGetValue(key, out value) ? value : key;
        }
    }

    internal static class Convert
    {
        public static readonly XNamespace One = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        public static readonly DateTime Imported = new DateTime(2026, 8, 4, 9, 30, 0, DateTimeKind.Utc);

        public static ParsedDocument Parse(string markdown, params string[] diagramLanguages)
        {
            var languages = new HashSet<string>(diagramLanguages, StringComparer.OrdinalIgnoreCase);
            return new MarkdownDocumentParser(TestCatalog.Instance).Parse(markdown, new ParseOptions(languages));
        }

        public static XElement Render(
            ParsedDocument parsed,
            Dictionary<string, DiagramOutcome> diagrams = null,
            Dictionary<string, AssetOutcome> assets = null,
            ISyntaxHighlighter highlighter = null)
        {
            var inputs = new RenderInputs(
                string.IsNullOrEmpty(parsed.SuggestedTitle) ? "Test Page" : parsed.SuggestedTitle,
                new PageMetadata(
                    @"C:\notes\test.md",
                    Sha256.OfString("source bytes"),
                    "1.0.0-test",
                    Imported),
                diagrams ?? new Dictionary<string, DiagramOutcome>(),
                assets ?? new Dictionary<string, AssetOutcome>(),
                TestCatalog.Instance);

            var renderer = new OneNoteXmlRenderer(highlighter ?? PlainSyntaxHighlighter.Instance);
            return XElement.Parse(renderer.Render(parsed, inputs));
        }

        public static XElement Page(string markdown, params string[] diagramLanguages)
        {
            return Render(Parse(markdown, diagramLanguages));
        }

        /// <summary>The content of every <c>one:T</c> in the page, in document order.</summary>
        public static string[] Fragments(XElement page)
        {
            return page.Descendants(One + "T").Select(t => t.Value).ToArray();
        }

        /// <summary>The outline's top-level content elements, skipping the title.</summary>
        public static XElement[] Body(XElement page)
        {
            var outline = page.Element(One + "Outline");
            var children = outline == null ? null : outline.Element(One + "OEChildren");
            return children == null ? new XElement[0] : children.Elements().ToArray();
        }

        /// <summary>
        /// The table inside a body paragraph. Asserts the shape OneNote requires — a
        /// <c>one:OE</c> whose only child is the <c>one:Table</c> — so a test that reaches a
        /// table through here has also checked its placement.
        /// </summary>
        public static XElement TableIn(XElement paragraph)
        {
            Assert.Equal("OE", paragraph.Name.LocalName);
            var table = paragraph.Elements().Single();
            Assert.Equal("Table", table.Name.LocalName);
            return table;
        }

        public static string Style(XElement element)
        {
            var attribute = element.Attribute("quickStyleIndex");
            return attribute == null ? null : attribute.Value;
        }
    }
}
