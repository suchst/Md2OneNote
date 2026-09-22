using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    /// Named by the assembly and library versions for a human, and by a hash of the embedded
    /// files for correctness: a shell edited without a version bump was served stale from the
    /// folder the previous build had extracted (2026-09-14), and version numbers do not change
    /// on every build. An update never serves an old bundle and never has to delete one either.
    /// </remarks>
    internal static class ShellFiles
    {
        public const string HostName = "md2onenote.shell";
        public const string IndexUri = "https://" + HostName + "/shell.html";

        private static readonly string[] Libraries = { "mermaid", "viz", "katex" };

        /// <summary>
        /// Every embedded file the shell is served from, as the path the shell loads it by
        /// (<c>fonts/KaTeX_Main-Regular.woff2</c>), in a fixed order so the content hash is
        /// stable. The version stamps are read by <see cref="LibraryVersion"/> and stay inside
        /// the assembly.
        /// </summary>
        public static IReadOnlyList<string> Names
        {
            get { return NamesOf(typeof(ShellFiles).Assembly); }
        }

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
            foreach (var name in NamesOf(assembly))
            {
                using (var stream = Open(assembly, name))
                {
                    var path = Path.Combine(folder, name.Replace('/', Path.DirectorySeparatorChar));
                    Directory.CreateDirectory(Path.GetDirectoryName(path));
                    using (var file = File.Create(path))
                    {
                        stream.CopyTo(file);
                    }
                }
            }

            File.WriteAllText(marker, DateTime.UtcNow.ToString("o"), new UTF8Encoding(false));
            return folder;
        }

        /// <summary>The version of an embedded library (<c>mermaid</c>, <c>viz</c>, <c>katex</c>) from its stamp.</summary>
        public static string LibraryVersion(Assembly assembly, string library)
        {
            using (var stream = assembly.GetManifestResourceStream(library + ".version.txt"))
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

        internal static IReadOnlyList<string> NamesOf(Assembly assembly)
        {
            return assembly.GetManifestResourceNames()
                .Where(name => !name.EndsWith(".version.txt", StringComparison.Ordinal))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }

        private static string Version(Assembly assembly)
        {
            var builder = new StringBuilder(assembly.GetName().Version.ToString());
            foreach (var library in Libraries)
            {
                builder.Append('-').Append(library).Append('-').Append(LibraryVersion(assembly, library));
            }

            return builder.Append('-').Append(ContentHash(assembly)).ToString();
        }

        /// <summary>The first 16 hex digits of a SHA-256 over the embedded files, in order.</summary>
        private static string ContentHash(Assembly assembly)
        {
            using (var sha = SHA256.Create())
            {
                foreach (var name in NamesOf(assembly))
                {
                    using (var stream = Open(assembly, name))
                    {
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

        private static Stream Open(Assembly assembly, string name)
        {
            var stream = assembly.GetManifestResourceStream(name);
            if (stream == null)
            {
                throw new InvalidOperationException("Embedded shell resource missing: " + name);
            }

            return stream;
        }
    }
}
