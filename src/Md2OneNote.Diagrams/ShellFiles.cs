using System;
using System.IO;
using System.Reflection;
using System.Text;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// Puts the embedded shell on disk so WebView2 can serve it through a virtual host mapping.
    /// </summary>
    /// <remarks>
    /// The folder lives under <c>%LOCALAPPDATA%\Md2OneNote\shell\{version}</c>, never the
    /// install directory (DESIGN.md §8.3: the sandbox needs no write access to Program Files).
    /// Versioned by the add-in's own assembly version plus the Mermaid version, so an update
    /// never serves a stale bundle and never has to delete one either.
    /// </remarks>
    internal static class ShellFiles
    {
        public const string HostName = "md2onenote.shell";
        public const string IndexUri = "https://" + HostName + "/shell.html";

        private static readonly string[] Names = { "shell.html", "mermaid.min.js" };

        public static string RootFolder
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "Md2OneNote");
            }
        }

        /// <summary>Extracts the shell if this version is not already on disk; returns its folder.</summary>
        public static string Ensure()
        {
            var assembly = typeof(ShellFiles).Assembly;
            var folder = Path.Combine(RootFolder, "shell", Version(assembly));
            var marker = Path.Combine(folder, ".complete");

            if (File.Exists(marker))
            {
                return folder;
            }

            Directory.CreateDirectory(folder);
            foreach (var name in Names)
            {
                using (var stream = assembly.GetManifestResourceStream(name))
                {
                    if (stream == null)
                    {
                        throw new InvalidOperationException("Embedded shell resource missing: " + name);
                    }

                    using (var file = File.Create(Path.Combine(folder, name)))
                    {
                        stream.CopyTo(file);
                    }
                }
            }

            File.WriteAllText(marker, DateTime.UtcNow.ToString("o"), new UTF8Encoding(false));
            return folder;
        }

        public static string MermaidVersion(Assembly assembly)
        {
            using (var stream = assembly.GetManifestResourceStream("mermaid.version.txt"))
            {
                if (stream == null)
                {
                    return "unknown";
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd().Trim();
                }
            }
        }

        private static string Version(Assembly assembly)
        {
            return assembly.GetName().Version + "-mermaid-" + MermaidVersion(assembly);
        }
    }
}
