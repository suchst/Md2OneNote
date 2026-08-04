using System;

namespace Md2OneNote.Core
{
    /// <summary>
    /// A Markdown file that has been read into memory.
    /// </summary>
    public sealed class SourceFile
    {
        public SourceFile(string path, string baseDirectory, string text, byte[] bytes)
        {
            if (string.IsNullOrEmpty(path)) throw new ArgumentNullException(nameof(path));
            if (text == null) throw new ArgumentNullException(nameof(text));
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));

            Path = path;
            BaseDirectory = baseDirectory ?? string.Empty;
            Text = text;
            Bytes = bytes;
        }

        public string Path { get; }

        /// <summary>Directory that relative image references resolve against.</summary>
        public string BaseDirectory { get; }

        public string Text { get; }

        /// <summary>Raw bytes, hashed for page identity. Hashing the bytes rather than the decoded
        /// text keeps the identity stable across encoding quirks.</summary>
        public byte[] Bytes { get; }
    }

    public interface ISourceFileReader
    {
        /// <summary>
        /// Reads a file, refusing anything over <paramref name="maxBytes"/>.
        /// Throws <see cref="SourceFileTooLargeException"/> when the limit is exceeded, and the
        /// usual IO exceptions when the file cannot be read.
        /// </summary>
        SourceFile Read(string path, long maxBytes);
    }

    public sealed class SourceFileTooLargeException : Exception
    {
        public SourceFileTooLargeException(string message) : base(message)
        {
        }
    }
}
