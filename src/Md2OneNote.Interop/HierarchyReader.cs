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
