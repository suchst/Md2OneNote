using System;
using System.Collections.Generic;
using System.Xml.Linq;
using Md2OneNote.Core;

namespace Md2OneNote.Interop
{
    /// <summary>
    /// Turns the XML OneNote returns from <c>GetHierarchy</c> into Core's reference types.
    /// </summary>
    /// <remarks>
    /// Deliberately pure and static. Everything else in this assembly needs OneNote running to
    /// execute, which puts it beyond CI; parsing does not, so it is separated out and tested
    /// against captured XML. The interesting failures here — a section group nested three deep,
    /// a page with no name, a notebook with nothing being viewed — are all reachable from a
    /// string fixture.
    /// <para>
    /// Elements are matched on local name only. The namespace differs between the schema versions
    /// OneNote will return, and nothing here depends on which one arrived.
    /// </para>
    /// </remarks>
    internal static class HierarchyReader
    {
        /// <summary>
        /// The section carrying <c>isCurrentlyViewed="true"</c>, or null when nothing is being
        /// viewed. A section group being viewed is not a section and does not count (FR-6).
        /// </summary>
        public static SectionRef FindActiveSection(string hierarchyXml)
        {
            var root = TryParse(hierarchyXml);
            if (root == null)
            {
                return null;
            }

            foreach (var element in root.DescendantsAndSelf())
            {
                if (element.Name.LocalName != "Section" || !IsCurrentlyViewed(element))
                {
                    continue;
                }

                var id = (string)element.Attribute("ID");
                if (!string.IsNullOrEmpty(id))
                {
                    return new SectionRef(id, (string)element.Attribute("name"));
                }
            }

            return null;
        }

        /// <summary>Every page in the hierarchy, in document order.</summary>
        public static IReadOnlyList<PageRef> ReadPages(string hierarchyXml)
        {
            var pages = new List<PageRef>();

            var root = TryParse(hierarchyXml);
            if (root == null)
            {
                return pages;
            }

            foreach (var element in root.DescendantsAndSelf())
            {
                if (element.Name.LocalName != "Page")
                {
                    continue;
                }

                var id = (string)element.Attribute("ID");
                if (!string.IsNullOrEmpty(id))
                {
                    pages.Add(new PageRef(id, (string)element.Attribute("name")));
                }
            }

            return pages;
        }

        /// <summary>
        /// The page most recently imported from <paramref name="sourcePath"/>, found by the
        /// <c>one:Meta</c> entries OneNote includes in a page listing (docs/page-schema-notes.md
        /// §8). <see cref="PageMatch.None"/> when no page carries that source, or when the page
        /// that does carries no hash — an unreadable match must degrade to "not found", never to
        /// a guess (DESIGN.md §7.3).
        /// </summary>
        /// <remarks>
        /// When several pages claim the same source — each superseding import adds one — the
        /// last in document order wins, which is the newest: OneNote lists pages in section
        /// order and new pages are appended. Paths compare case-insensitively, as Windows does.
        /// </remarks>
        public static PageMatch FindPageBySource(string hierarchyXml, string sourcePath)
        {
            if (string.IsNullOrEmpty(sourcePath))
            {
                return PageMatch.None;
            }

            var root = TryParse(hierarchyXml);
            if (root == null)
            {
                return PageMatch.None;
            }

            var match = PageMatch.None;

            foreach (var page in root.DescendantsAndSelf())
            {
                if (page.Name.LocalName != "Page")
                {
                    continue;
                }

                var id = (string)page.Attribute("ID");
                if (string.IsNullOrEmpty(id))
                {
                    continue;
                }

                var source = MetaContent(page, PageMetadata.SourceKey);
                if (!string.Equals(source, sourcePath, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var hash = MetaContent(page, PageMetadata.HashKey);
                match = string.IsNullOrEmpty(hash) ? PageMatch.None : PageMatch.Hit(id, hash);
            }

            return match;
        }

        private static string MetaContent(XElement page, string name)
        {
            foreach (var child in page.Elements())
            {
                if (child.Name.LocalName == "Meta"
                    && string.Equals((string)child.Attribute("name"), name, StringComparison.Ordinal))
                {
                    return (string)child.Attribute("content");
                }
            }

            return null;
        }

        private static bool IsCurrentlyViewed(XElement element)
        {
            var value = (string)element.Attribute("isCurrentlyViewed");
            return string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Malformed XML is treated as "nothing found" rather than an exception. A caller that
        /// cannot find the active section already has a defined answer for that (FR-6), and a
        /// parse failure is not a more actionable outcome than an empty one.
        /// </summary>
        private static XElement TryParse(string xml)
        {
            if (string.IsNullOrEmpty(xml))
            {
                return null;
            }

            try
            {
                return XDocument.Parse(xml).Root;
            }
            catch (System.Xml.XmlException)
            {
                return null;
            }
        }
    }
}
