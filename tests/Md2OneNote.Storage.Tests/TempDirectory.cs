using System;
using System.IO;

namespace Md2OneNote.Storage.Tests
{
    /// <summary>
    /// A real directory in the temp folder, removed when the test finishes. These two classes are
    /// the assembly that touches the disk, so testing them against a fake filesystem would be
    /// testing the fake.
    /// </summary>
    internal sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                "md2onenote-tests",
                Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public string Write(string name, byte[] bytes)
        {
            var full = System.IO.Path.Combine(Path, name);
            Directory.CreateDirectory(System.IO.Path.GetDirectoryName(full));
            File.WriteAllBytes(full, bytes);
            return full;
        }

        public string PathTo(string name)
        {
            return System.IO.Path.Combine(Path, name);
        }

        public void Dispose()
        {
            try
            {
                Directory.Delete(Path, true);
            }
            catch (IOException)
            {
                // A leftover temp directory is not worth failing a passing test over.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }
}
