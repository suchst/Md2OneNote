using System;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// Puts the embedded shell on disk so WebView2 can serve it through a virtual host mapping.
    /// </summary>
    /// <remarks>
    /// The folder lives under <c>%LOCALAPPDATA%\Md2OneNote\shell\{version}</c>, never the
    /// install directory (DESIGN.md §8.3: the sandbox needs no write access to Program Files).
    /// Named by the assembly and Mermaid versions for a human, and by a hash of the embedded
    /// files for correctness: a shell edited without a version bump was served stale from the
    /// folder the previous build had extracted (2026-09-14), and version numbers do not change
    /// on every build. An update never serves an old bundle and never has to delete one either.
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
            return assembly.GetName().Version + "-mermaid-" + MermaidVersion(assembly) + "-" + ContentHash(assembly);
        }

        /// <summary>The first 16 hex digits of a SHA-256 over the embedded files, in order.</summary>
        private static string ContentHash(Assembly assembly)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var name in Names)
                {
                    using (var stream = assembly.GetManifestResourceStream(name))
                    {
                        if (stream == null)
                        {
                            throw new InvalidOperationException("Embedded shell resource missing: " + name);
                        }

                        var buffer = new byte[81920];
                        int read;
                        while ((read = stream.Read(buffer, 0, buffer.Length)) > 0)
                        {
                            sha.TransformBlock(buffer, 0, read, null, 0);
                        }
                    }
                }

                sha.TransformFinalBlock(new byte[0], 0, 0);
                return BitConverter.ToString(sha.Hash, 0, 8).Replace("-", string.Empty).ToLowerInvariant();
            }
        }
    }
}
