using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Xml.Linq;

namespace Md2OneNote.SchemaSpike
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            var options = Options.Parse(args);
            if (options == null)
            {
                Usage();
                return 2;
            }

            try
            {
                Run(options);
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine();
                Console.Error.WriteLine(ex.Message);
                return 1;
            }
        }

        private static void Run(Options options)
        {
            Directory.CreateDirectory(options.OutputDirectory);

            Console.WriteLine("Connecting to OneNote…");
            var oneNote = OneNoteApplication.Connect();

            var window = oneNote.CurrentWindow();
            Console.WriteLine("  notebook {0}", window.NotebookId ?? "(none)");
            Console.WriteLine("  section  {0}", window.SectionId ?? "(none)");
            Console.WriteLine("  page     {0}", window.PageId ?? "(none)");
            Console.WriteLine();

            var pageId = options.PageId ?? window.PageId;
            if (string.IsNullOrEmpty(pageId))
            {
                throw new InvalidOperationException(
                    "OneNote is not showing a page. Open the page you hand-authored for this " +
                    "spike (see tools/schema-spike/README.md) and run again.");
            }

            var pageInfo = options.IncludeBinary
                ? OneNoteApplication.PageInfoAll
                : OneNoteApplication.PageInfoBasic;

            // Asking for each schema in turn is how the enum values get confirmed instead of
            // trusted: the namespace that comes back names the schema that was actually used.
            var dumps = new Dictionary<int, string>();
            foreach (var schema in new[]
            {
                OneNoteApplication.Schema2007,
                OneNoteApplication.Schema2010,
                OneNoteApplication.Schema2013
            })
            {
                try
                {
                    var xml = oneNote.GetPageContent(pageId, pageInfo, schema);
                    dumps[schema] = xml;
                    var path = Save(options.OutputDirectory, "page-schema-" + schema + ".xml", xml);
                    Console.WriteLine("schema {0} → {1}", schema, NamespaceOf(xml));
                    Console.WriteLine("          {0}", path);
                }
                catch (Exception ex)
                {
                    Console.WriteLine("schema {0} → refused: {1}", schema, ex.Message);
                }
            }

            if (dumps.Count == 0)
            {
                throw new InvalidOperationException("No schema version produced a page dump.");
            }

            var best = dumps.ContainsKey(OneNoteApplication.Schema2013)
                ? dumps[OneNoteApplication.Schema2013]
                : dumps[OneNoteApplication.Schema2010];

            Console.WriteLine();

            string hierarchyForMeta = null;
            if (!string.IsNullOrEmpty(window.SectionId))
            {
                var hierarchy = oneNote.GetHierarchy(
                    window.SectionId,
                    OneNoteApplication.HierarchyScopePages,
                    OneNoteApplication.Schema2013);

                Console.WriteLine("hierarchy → {0}",
                    Save(options.OutputDirectory, "hierarchy-pages.xml", hierarchy));

                if (options.MetaProbe)
                {
                    hierarchyForMeta = MetaProbe(oneNote, window.SectionId, options);
                }
            }

            var findings = Save(
                options.OutputDirectory,
                "findings.md",
                SchemaReport.Describe(best, hierarchyForMeta),
                prettyPrint: false);

            Console.WriteLine();
            Console.WriteLine("Findings → {0}", findings);
            Console.WriteLine("Copy the confirmed values into docs/page-schema-notes.md.");
        }

        /// <summary>
        /// Writes a real page carrying a <c>one:Meta</c>, then reads the section listing back to
        /// see whether the metadata survives the round trip (DESIGN.md §7.3).
        /// </summary>
        private static string MetaProbe(OneNoteApplication oneNote, string sectionId, Options options)
        {
            Console.WriteLine();
            Console.WriteLine("--meta-probe: creating a page in the section on screen.");

            var pageId = oneNote.CreateNewPage(sectionId, OneNoteApplication.NewPageStyleBlankPageWithTitle);

            const string ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";
            XNamespace one = ns;

            var page = new XElement(
                one + "Page",
                new XAttribute(XNamespace.Xmlns + "one", ns),
                new XAttribute("ID", pageId),
                new XElement(one + "Meta",
                    new XAttribute("name", "Md2OneNote.SourceHash"),
                    new XAttribute("content", "phase0-probe")),
                new XElement(one + "Meta",
                    new XAttribute("name", "Md2OneNote.Source"),
                    new XAttribute("content", @"C:\probe\phase0.md")),
                new XElement(one + "Title",
                    new XElement(one + "OE",
                        new XElement(one + "T", new XCData("Md2OneNote Phase 0 probe — safe to delete")))),
                new XElement(one + "Outline",
                    new XElement(one + "OEChildren",
                        new XElement(one + "OE",
                            new XElement(one + "T", new XCData("Written by tools/schema-spike."))))));

            oneNote.UpdatePageContent(page.ToString(SaveOptions.DisableFormatting), OneNoteApplication.Schema2013);

            var readBack = oneNote.GetPageContent(pageId, OneNoteApplication.PageInfoBasic, OneNoteApplication.Schema2013);
            Save(options.OutputDirectory, "meta-probe-page.xml", readBack);

            var hierarchy = oneNote.GetHierarchy(
                sectionId,
                OneNoteApplication.HierarchyScopePages,
                OneNoteApplication.Schema2013);

            Save(options.OutputDirectory, "meta-probe-hierarchy.xml", hierarchy);

            Console.WriteLine("  probe page created — delete it from OneNote when you are done.");
            return hierarchy;
        }

        private static string Save(string directory, string name, string content, bool prettyPrint = true)
        {
            var path = Path.Combine(directory, name);

            if (prettyPrint)
            {
                try
                {
                    // OneNote returns one enormous line. Indenting costs nothing (text lives in
                    // CDATA, so it is untouched) and makes the dump readable by a person.
                    content = XDocument.Parse(content).ToString();
                }
                catch (System.Xml.XmlException)
                {
                }
            }

            File.WriteAllText(path, content, new UTF8Encoding(false));
            return path;
        }

        private static string NamespaceOf(string xml)
        {
            try
            {
                return XDocument.Parse(xml).Root.Name.NamespaceName;
            }
            catch (System.Xml.XmlException)
            {
                return "(unparseable)";
            }
        }

        private static void Usage()
        {
            Console.WriteLine("SchemaSpike — dumps a real OneNote page so DESIGN.md §5 can be checked");
            Console.WriteLine("             against reality (IMPLEMENTATION.md §11, Phase 0).");
            Console.WriteLine();
            Console.WriteLine("  SchemaSpike [--out <dir>] [--page <id>] [--meta-probe] [--binary]");
            Console.WriteLine();
            Console.WriteLine("  --out         where to write dumps (default: docs/schema-dump)");
            Console.WriteLine("  --page        page id to dump (default: the page on screen)");
            Console.WriteLine("  --meta-probe  CREATES a page in the section on screen to find out");
            Console.WriteLine("                whether one:Meta survives GetHierarchy");
            Console.WriteLine("  --binary      include image data (large; off by default)");
        }

        private sealed class Options
        {
            public string OutputDirectory { get; private set; }

            public string PageId { get; private set; }

            public bool MetaProbe { get; private set; }

            public bool IncludeBinary { get; private set; }

            public static Options Parse(string[] args)
            {
                var options = new Options { OutputDirectory = DefaultOutput() };

                for (var i = 0; i < args.Length; i++)
                {
                    switch (args[i])
                    {
                        case "--out":
                            if (++i == args.Length) return null;
                            options.OutputDirectory = Path.GetFullPath(args[i]);
                            break;
                        case "--page":
                            if (++i == args.Length) return null;
                            options.PageId = args[i];
                            break;
                        case "--meta-probe":
                            options.MetaProbe = true;
                            break;
                        case "--binary":
                            options.IncludeBinary = true;
                            break;
                        default:
                            return null;
                    }
                }

                return options;
            }

            private static string DefaultOutput()
            {
                var directory = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
                while (directory != null && !File.Exists(Path.Combine(directory.FullName, "Md2OneNote.sln")))
                {
                    directory = directory.Parent;
                }

                var root = directory != null ? directory.FullName : Directory.GetCurrentDirectory();
                return Path.Combine(root, "docs", "schema-dump");
            }
        }
    }
}
