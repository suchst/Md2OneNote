using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Xml.Linq;

namespace Md2OneNote.SchemaSpike
{
    /// <summary>
    /// Turns a page dump into the specific answers Phase 0 needs.
    /// </summary>
    /// <remarks>
    /// A raw dump of a page carrying every construct runs to thousands of lines, and reading it by
    /// eye is how a wrong quick-style index survives review. This reduces it to the handful of
    /// facts the renderer actually depends on, and states each one next to what the renderer
    /// currently assumes so that a mismatch is impossible to miss.
    /// </remarks>
    internal static class SchemaReport
    {
        /// <summary>What Md2OneNote.Core.Rendering.StyleTable declares today, unverified.</summary>
        private static readonly string[] AssumedStyleNames =
        {
            "PageTitle", "h1", "h2", "h3", "h4", "h5", "h6", "p", "blockquote", "code", "cite"
        };

        /// <summary>The order OneNoteXmlRenderer emits children of one:Page in.</summary>
        private static readonly string[] AssumedPageOrder =
        {
            "QuickStyleDef", "TagDef", "Meta", "Title", "Outline"
        };

        public static string Describe(string pageXml, string hierarchyXml)
        {
            var page = XDocument.Parse(pageXml);
            var report = new StringBuilder();

            report.AppendLine("# Phase 0 schema dump — findings");
            report.AppendLine();
            report.AppendLine(Line("Generated", DateTime.Now.ToString("u", CultureInfo.InvariantCulture)));
            report.AppendLine(Line("Namespace returned", page.Root.Name.NamespaceName));
            report.AppendLine();

            PageOrder(report, page);
            QuickStyles(report, page);
            Tags(report, page);
            Meta(report, page, hierarchyXml);
            Inventory(report, page, "Bullet", "Number", "List");
            Inventory(report, page, "Table", "Column", "Row", "Cell");
            Inventory(report, page, "Image", "Size", "Position", "InkDrawing");
            TextRuns(report, page);
            Nesting(report, page);

            return report.ToString();
        }

        private static void PageOrder(StringBuilder report, XDocument page)
        {
            report.AppendLine("## Order of one:Page children");
            report.AppendLine();

            var actual = page.Root.Elements()
                .Select(e => e.Name.LocalName)
                .Distinct()
                .ToList();

            report.AppendLine("Observed: " + string.Join(" → ", actual.Select(n => "`one:" + n + "`")));
            report.AppendLine();
            report.AppendLine("Renderer emits: " + string.Join(" → ", AssumedPageOrder.Select(n => "`one:" + n + "`")));
            report.AppendLine();

            var shared = actual.Where(n => AssumedPageOrder.Contains(n)).ToList();
            var expected = AssumedPageOrder.Where(n => actual.Contains(n)).ToList();
            report.AppendLine(shared.SequenceEqual(expected)
                ? "**Match** for the elements present in both."
                : "**MISMATCH — the renderer's element order does not follow OneNote's.**");

            report.AppendLine();
            report.AppendLine("Attributes on `one:Page`: " + Attributes(page.Root));
            report.AppendLine();
        }

        private static void QuickStyles(StringBuilder report, XDocument page)
        {
            report.AppendLine("## one:QuickStyleDef");
            report.AppendLine();

            var defs = Elements(page, "QuickStyleDef")
                .OrderBy(e => Number(e, "index"))
                .ToList();

            if (defs.Count == 0)
            {
                report.AppendLine("None present. The page was probably dumped without piBasic content.");
                report.AppendLine();
                return;
            }

            report.AppendLine("| index | name | font | fontSize | fontColor | other |");
            report.AppendLine("| --- | --- | --- | --- | --- | --- |");
            foreach (var def in defs)
            {
                var known = new[] { "index", "name", "font", "fontSize", "fontColor" };
                var other = def.Attributes()
                    .Where(a => !known.Contains(a.Name.LocalName))
                    .Select(a => a.Name.LocalName + "=" + a.Value);

                report.AppendLine(string.Join(" | ",
                    "| " + Value(def, "index"),
                    "`" + Value(def, "name") + "`",
                    Value(def, "font"),
                    Value(def, "fontSize"),
                    Value(def, "fontColor"),
                    string.Join(", ", other) + " |"));
            }

            report.AppendLine();

            // The names are the part we do not get to invent: they are what makes OneNote treat
            // our output as its own styles in the outline view and style picker (FR-8).
            var actualNames = defs.Select(d => Value(d, "name")).ToList();
            var missing = AssumedStyleNames.Where(n => !actualNames.Contains(n)).ToList();

            report.AppendLine(missing.Count == 0
                ? "**Every name the renderer uses appears on a real page.**"
                : "**Names the renderer uses that OneNote did NOT produce: " +
                  string.Join(", ", missing.Select(n => "`" + n + "`")) +
                  "** — either the page lacks that construct, or StyleTable is wrong.");
            report.AppendLine();
        }

        private static void Tags(StringBuilder report, XDocument page)
        {
            report.AppendLine("## one:TagDef and one:Tag");
            report.AppendLine();
            report.AppendLine("Renderer assumes the To Do tag is `type=\"3\" symbol=\"3\"` at index 0.");
            report.AppendLine();

            var defs = Elements(page, "TagDef").ToList();
            if (defs.Count == 0)
            {
                report.AppendLine("No `one:TagDef` on this page — add a checkbox to the source page and rerun.");
                report.AppendLine();
                return;
            }

            report.AppendLine("| index | type | symbol | name | other |");
            report.AppendLine("| --- | --- | --- | --- | --- |");
            foreach (var def in defs.OrderBy(e => Number(e, "index")))
            {
                var known = new[] { "index", "type", "symbol", "name" };
                var other = def.Attributes()
                    .Where(a => !known.Contains(a.Name.LocalName))
                    .Select(a => a.Name.LocalName + "=" + a.Value);

                report.AppendLine(string.Join(" | ",
                    "| " + Value(def, "index"),
                    Value(def, "type"),
                    Value(def, "symbol"),
                    "`" + Value(def, "name") + "`",
                    string.Join(", ", other) + " |"));
            }

            report.AppendLine();
            report.AppendLine("Usages: " + string.Join("; ", Elements(page, "Tag").Select(Attributes).Distinct()));
            report.AppendLine();
        }

        private static void Meta(StringBuilder report, XDocument page, string hierarchyXml)
        {
            report.AppendLine("## one:Meta — does it survive?");
            report.AppendLine();
            report.AppendLine("This decides whether `IPageIndex` is needed at all (DESIGN.md §7.3). " +
                              "If a page's `one:Meta` comes back from `GetHierarchy`, re-import " +
                              "matching (FR-18) can read it straight off the section listing and " +
                              "the index collapses to nothing.");
            report.AppendLine();

            var onPage = Elements(page, "Meta").ToList();
            report.AppendLine("In `GetPageContent`: " + (onPage.Count == 0
                ? "none."
                : string.Join("; ", onPage.Select(m => "`" + Value(m, "name") + "` = `" + Value(m, "content") + "`"))));

            if (string.IsNullOrEmpty(hierarchyXml))
            {
                report.AppendLine();
                report.AppendLine("In `GetHierarchy`: not sampled — rerun with `--meta-probe`.");
                report.AppendLine();
                return;
            }

            var hierarchy = XDocument.Parse(hierarchyXml);
            var inHierarchy = Elements(hierarchy, "Meta").ToList();

            report.AppendLine();
            report.AppendLine("In `GetHierarchy`: " + (inHierarchy.Count == 0
                ? "**none — `IPageIndex` has to exist.**"
                : "**present — " +
                  string.Join("; ", inHierarchy.Select(m => "`" + Value(m, "name") + "` = `" + Value(m, "content") + "`")) +
                  "; `IPageIndex` can be deleted.**"));
            report.AppendLine();
            report.AppendLine("Attributes on `one:Page` nodes in the hierarchy: " +
                              string.Join("; ", Elements(hierarchy, "Page").Select(Attributes).Distinct().Take(3)));
            report.AppendLine();
        }

        private static void TextRuns(StringBuilder report, XDocument page)
        {
            report.AppendLine("## one:T — the inline HTML OneNote itself writes");
            report.AppendLine();
            report.AppendLine("Whatever appears here is what `InlineWriter` is allowed to emit. " +
                              "Superscript, subscript and inline code all depend on it.");
            report.AppendLine();
            report.AppendLine("```html");

            foreach (var text in Elements(page, "T").Take(40))
            {
                var value = text.Value;
                report.AppendLine(value.Length > 200 ? value.Substring(0, 200) + " …" : value);
            }

            report.AppendLine("```");
            report.AppendLine();
        }

        private static void Nesting(StringBuilder report, XDocument page)
        {
            report.AppendLine("## Observed containment");
            report.AppendLine();
            report.AppendLine("| parent | children |");
            report.AppendLine("| --- | --- |");

            var pairs = page.Descendants()
                .Where(e => e.HasElements)
                .GroupBy(e => e.Name.LocalName)
                .OrderBy(g => g.Key);

            foreach (var group in pairs)
            {
                var children = group
                    .SelectMany(e => e.Elements())
                    .Select(e => e.Name.LocalName)
                    .Distinct()
                    .OrderBy(n => n, StringComparer.Ordinal);

                report.AppendLine("| `one:" + group.Key + "` | " +
                                  string.Join(", ", children.Select(c => "`one:" + c + "`")) + " |");
            }

            report.AppendLine();
        }

        private static void Inventory(StringBuilder report, XDocument page, params string[] localNames)
        {
            report.AppendLine("## " + string.Join(", ", localNames.Select(n => "one:" + n)));
            report.AppendLine();

            foreach (var name in localNames)
            {
                var elements = Elements(page, name).ToList();
                if (elements.Count == 0)
                {
                    report.AppendLine("- `one:" + name + "` — absent from this page.");
                    continue;
                }

                report.AppendLine("### one:" + name + " (" + elements.Count + ")");
                report.AppendLine();
                report.AppendLine("| attribute | values seen |");
                report.AppendLine("| --- | --- |");

                var byName = elements
                    .SelectMany(e => e.Attributes())
                    .GroupBy(a => a.Name.LocalName)
                    .OrderBy(g => g.Key, StringComparer.Ordinal);

                foreach (var group in byName)
                {
                    var values = group.Select(a => Truncate(a.Value)).Distinct().Take(8);
                    report.AppendLine("| `" + group.Key + "` | " + string.Join(", ", values) + " |");
                }

                report.AppendLine();
            }

            report.AppendLine();
        }

        private static IEnumerable<XElement> Elements(XDocument document, string localName)
        {
            return document.Descendants().Where(e => e.Name.LocalName == localName);
        }

        private static string Attributes(XElement element)
        {
            return string.Join(" ", element.Attributes()
                .Where(a => !a.IsNamespaceDeclaration)
                .Select(a => a.Name.LocalName + "=\"" + Truncate(a.Value) + "\""));
        }

        private static string Value(XElement element, string name)
        {
            var attribute = element.Attribute(name);
            return attribute == null ? string.Empty : attribute.Value;
        }

        private static int Number(XElement element, string name)
        {
            int parsed;
            return int.TryParse(Value(element, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)
                ? parsed
                : int.MaxValue;
        }

        private static string Truncate(string value)
        {
            // Image data is base64 and would otherwise be the entire report.
            var cleaned = value.Replace("|", "\\|").Replace("\r", " ").Replace("\n", " ");
            return cleaned.Length > 48 ? cleaned.Substring(0, 48) + "…" : cleaned;
        }

        private static string Line(string label, string value)
        {
            return "- **" + label + ":** " + value;
        }
    }
}
