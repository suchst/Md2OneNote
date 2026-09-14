using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Xml.Linq;
using Md2OneNote.Interop;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// Phase 0's unfinished business, run from inside OneNote because that is the only place it can
    /// run (docs/page-schema-notes.md §10).
    /// </summary>
    /// <remarks>
    /// This is scaffolding, not product code, and it is the one place allowed to touch the
    /// <c>Application</c> object directly rather than going through <see cref="Md2OneNote.Interop"/>.
    /// <c>IOneNoteGateway</c> has no "current page" operation on purpose — its shape is what enforces
    /// FR-21 — and a diagnostic is not a reason to widen it. Delete this file with the two
    /// diagnostic ribbon buttons once §8 of the notes is answered.
    /// </remarks>
    internal static class Diagnostics
    {
        private const int PageInfoBinaryData = OneNoteEnums.PageInfoBinaryData;
        private const int PageInfoBasic = OneNoteEnums.PageInfoBasic;
        private const int Schema2013 = OneNoteEnums.Schema2013;

        /// <summary>
        /// Writes the page currently on screen to the desktop, with and without binary data, and
        /// the section listing beside it. Returns the folder written to, or null if no page is open.
        /// </summary>
        public static string DumpCurrentPage(object applicationObject)
        {
            var application = applicationObject as IApplication;
            if (application == null)
            {
                return null;
            }

            var window = application.Windows.CurrentWindow;
            string pageId = window.CurrentPageId;
            string sectionId = window.CurrentSectionId;

            if (string.IsNullOrEmpty(pageId))
            {
                return null;
            }

            var folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
                "Md2OneNote-dump-" + DateTime.Now.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture));

            Directory.CreateDirectory(folder);

            // Without binary first: it is the readable one, and the one worth opening.
            string basic;
            application.GetPageContent(pageId, out basic, PageInfoBasic, Schema2013);
            Save(folder, "page-basic.xml", basic);

            // With binary: the only way to see whether one:Data really is base64 (notes §7).
            string all;
            application.GetPageContent(pageId, out all, PageInfoBinaryData, Schema2013);
            Save(folder, "page-binary.xml", all);

            if (!string.IsNullOrEmpty(sectionId))
            {
                // The question that decides whether IPageIndex ships at all (notes §8): does a
                // section listing carry the one:Meta we stamped, or must each page be fetched?
                string hierarchy;
                application.GetHierarchy(sectionId, OneNoteEnums.HierarchyScopePages, out hierarchy, Schema2013);
                Save(folder, "hierarchy-pages.xml", hierarchy);
            }

            return folder;
        }

        private static void Save(string folder, string name, string xml)
        {
            var path = Path.Combine(folder, name);

            // OneNote returns one enormous line. Indenting costs nothing — the text lives in CDATA
            // and is untouched — and makes the dump readable by a person.
            var content = xml;
            try
            {
                content = XDocument.Parse(xml).ToString();
            }
            catch (System.Xml.XmlException)
            {
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
        }
    }
}
