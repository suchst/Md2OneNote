using System.Collections.Generic;
using System.Xml.Linq;

namespace Md2OneNote.Core.Rendering
{
    /// <summary>
    /// The <c>one:QuickStyleDef</c> block every page carries, and the indices that point into it.
    /// </summary>
    /// <remarks>
    /// IMPLEMENTATION.md §5.1 warns that quick-style indices are not guaranteed. That does not
    /// apply here: pages are created blank and filled in one shot, so the renderer authors the
    /// whole definition block and therefore chooses the indices (DESIGN.md §6.1, D3). They are
    /// constants of our own making, and contiguous because nothing forces a gap.
    /// <para>
    /// The <c>name</c> values are the part we do not get to choose — they are what makes OneNote
    /// treat the output as its own styles, so that the outline view and style picker behave as
    /// they do for hand-authored content (FR-8). Confirming them against a real page is a Phase 0
    /// item.
    /// </para>
    /// </remarks>
    internal static class StyleTable
    {
        public const int PageTitle = 0;
        public const int Heading1 = 1;
        public const int Heading6 = 6;
        public const int Body = 7;
        public const int Quote = 8;
        public const int Code = 9;
        public const int Cite = 10;

        /// <summary>Index of the <c>To Do</c> tag definition, used by task list items.</summary>
        public const int TaskTag = 0;

        public static int Heading(int level)
        {
            var index = Heading1 + (level - 1);
            return index > Heading6 ? Heading6 : index;
        }

        public static IEnumerable<XElement> Definitions()
        {
            yield return Def(PageTitle, "PageTitle", "Calibri Light", "20", "#1F497D", false);
            yield return Def(1, "h1", "Calibri Light", "16", "#1E4E79", false);
            yield return Def(2, "h2", "Calibri Light", "14", "#2E74B5", false);
            yield return Def(3, "h3", "Calibri Light", "12", "#5B9BD5", false);
            yield return Def(4, "h4", "Calibri Light", "11", "#2E74B5", true);
            yield return Def(5, "h5", "Calibri Light", "11", "#5B9BD5", false);
            yield return Def(6, "h6", "Calibri Light", "11", "#808080", true);
            yield return Def(Body, "p", "Calibri", "11", "automatic", false);
            yield return Def(Quote, "blockquote", "Calibri", "11", "#595959", true);
            yield return Def(Code, "code", "Consolas", "9", "#000000", false);
            yield return Def(Cite, "cite", "Calibri", "9", "#808080", false);
        }

        public static XElement TagDefinition()
        {
            return new XElement(
                OneNote.Ns + "TagDef",
                new XAttribute("index", TaskTag),
                new XAttribute("type", "3"),
                new XAttribute("symbol", "3"),
                new XAttribute("name", "To Do"),
                new XAttribute("fontColor", "automatic"),
                new XAttribute("highlightColor", "none"));
        }

        private static XElement Def(int index, string name, string font, string size, string color, bool italic)
        {
            var element = new XElement(
                OneNote.Ns + "QuickStyleDef",
                new XAttribute("index", index),
                new XAttribute("name", name),
                new XAttribute("font", font),
                new XAttribute("fontSize", size),
                new XAttribute("fontColor", color));

            if (italic)
            {
                element.Add(new XAttribute("italic", "true"));
            }

            return element;
        }
    }

    /// <summary>
    /// Page geometry. IMPLEMENTATION.md §9.1: a 700 px outline at (36, 86), leaving 660 px of
    /// usable content width that images, tables and code blocks all size themselves against.
    /// </summary>
    internal static class Layout
    {
        public const int OutlineX = 36;
        public const int OutlineY = 86;
        public const int OutlineWidth = 700;
        public const int OutlineHeight = 100;
        public const int ContentWidth = 660;
    }

    internal static class OneNote
    {
        public static readonly XNamespace Ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";
    }
}
