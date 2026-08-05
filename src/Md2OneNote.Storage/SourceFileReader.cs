using System;
using System.Globalization;
using System.IO;
using System.Text;
using Md2OneNote.Core;

namespace Md2OneNote.Storage
{
    /// <summary>
    /// Reads a Markdown file into memory: raw bytes for identity, decoded text for conversion,
    /// and the directory that relative image references resolve against.
    /// </summary>
    /// <remarks>
    /// The size limit is enforced while reading rather than after. A file larger than the limit
    /// is refused having cost the limit, not its own size (NFR-8).
    /// </remarks>
    public sealed class SourceFileReader : ISourceFileReader
    {
        public SourceFile Read(string path, long maxBytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));

            // Resolving here means the page records an unambiguous path and relative images
            // resolve against the document, never against wherever OneNote was started from.
            var fullPath = Path.GetFullPath(path);

            byte[] bytes;
            using (var stream = new FileStream(
                fullPath,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                8192,
                FileOptions.SequentialScan))
            {
                bytes = FileBytes.ReadCapped(stream, maxBytes);
            }

            if (bytes.LongLength > maxBytes)
            {
                // Not localized: ImportService catches this and produces the message the user
                // actually sees. This text is for a log.
                throw new SourceFileTooLargeException(string.Format(
                    CultureInfo.InvariantCulture,
                    "'{0}' is larger than the {1} byte limit.",
                    fullPath,
                    maxBytes));
            }

            return new SourceFile(fullPath, Path.GetDirectoryName(fullPath), Decode(bytes), bytes);
        }

        /// <summary>
        /// Decodes the file's text, honouring a byte order mark and otherwise assuming UTF-8.
        /// </summary>
        /// <remarks>
        /// The fallback matters on Windows: plenty of Markdown is still written in the system's
        /// ANSI code page, and a file with one stray byte in it should import with one odd
        /// character, not fail outright (FR-3).
        /// </remarks>
        internal static string Decode(byte[] bytes)
        {
            if (bytes.Length == 0)
            {
                return string.Empty;
            }

            int bomLength;
            var declared = DetectByteOrderMark(bytes, out bomLength);
            if (declared != null)
            {
                // Decoded past the mark: left in, it becomes a zero-width character ahead of the
                // first heading, and a document that opens with "# Title" silently stops being a
                // title at all.
                return declared.GetString(bytes, bomLength, bytes.Length - bomLength);
            }

            try
            {
                return new UTF8Encoding(false, true).GetString(bytes);
            }
            catch (DecoderFallbackException)
            {
                return Encoding.Default.GetString(bytes);
            }
        }

        private static Encoding DetectByteOrderMark(byte[] bytes, out int bomLength)
        {
            // UTF-32 LE is tested before UTF-16 LE: its mark starts with the shorter one's.
            if (StartsWith(bytes, 0xFF, 0xFE, 0x00, 0x00))
            {
                bomLength = 4;
                return new UTF32Encoding(false, true);
            }

            if (StartsWith(bytes, 0x00, 0x00, 0xFE, 0xFF))
            {
                bomLength = 4;
                return new UTF32Encoding(true, true);
            }

            if (StartsWith(bytes, 0xEF, 0xBB, 0xBF))
            {
                bomLength = 3;
                return new UTF8Encoding(false);
            }

            if (StartsWith(bytes, 0xFF, 0xFE))
            {
                bomLength = 2;
                return new UnicodeEncoding(false, false);
            }

            if (StartsWith(bytes, 0xFE, 0xFF))
            {
                bomLength = 2;
                return new UnicodeEncoding(true, false);
            }

            bomLength = 0;
            return null;
        }

        private static bool StartsWith(byte[] bytes, params byte[] prefix)
        {
            if (bytes.Length < prefix.Length)
            {
                return false;
            }

            for (var i = 0; i < prefix.Length; i++)
            {
                if (bytes[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }
    }
}
