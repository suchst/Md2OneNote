using System;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Text;

namespace Md2OneNote.AddIn.Diagnostics
{
    /// <summary>
    /// The zip a user attaches to a bug report: the log, its rolled-over predecessor when there
    /// is one, and <c>environment.txt</c> (NFR-17). Nothing else; in particular no notebook
    /// content.
    /// </summary>
    internal static class DiagnosticBundle
    {
        public static string SuggestedFileName()
        {
            return "Md2OneNote-diagnostics-" + DateTime.Now.ToString("yyyyMMdd-HHmm", CultureInfo.InvariantCulture) + ".zip";
        }

        public static void Write(string zipPath, string logPath, EnvironmentReport report)
        {
            using (var stream = new FileStream(zipPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var zip = new ZipArchive(stream, ZipArchiveMode.Create))
            {
                var environment = zip.CreateEntry("environment.txt");
                using (var writer = new StreamWriter(environment.Open(), new UTF8Encoding(false)))
                {
                    writer.Write(report.ToString());
                }

                AddIfPresent(zip, logPath);
                AddIfPresent(zip, RolledOverName(logPath));
            }
        }

        private static void AddIfPresent(ZipArchive zip, string path)
        {
            if (string.IsNullOrEmpty(path) || !File.Exists(path))
            {
                return;
            }

            var entry = zip.CreateEntry(Path.GetFileName(path));

            // ReadWrite sharing: the logger may append while this runs, and a report that
            // cannot include the log because the log is in use is no report.
            using (var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))
            using (var target = entry.Open())
            {
                source.CopyTo(target);
            }
        }

        /// <summary>Mirrors <c>FileLogger.Roll</c>: <c>log.txt</c> rolls to <c>log.txt.1</c>.</summary>
        private static string RolledOverName(string logPath)
        {
            return logPath + ".1";
        }
    }
}
