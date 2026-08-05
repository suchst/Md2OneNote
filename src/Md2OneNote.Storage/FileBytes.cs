using System;
using System.IO;
using Md2OneNote.Core;

namespace Md2OneNote.Storage
{
    /// <summary>
    /// Reads image bytes off the local disk. The path has already been through
    /// <see cref="AssetPathPolicy"/> by the time it arrives here — this class asks no questions
    /// about it and answers none, which is what keeps the policy in one place (DESIGN.md §9).
    /// </summary>
    public sealed class FileBytes : IFileBytes
    {
        private const int ChunkBytes = 8192;

        public byte[] Read(string fullPath, long limitBytes)
        {
            if (string.IsNullOrEmpty(fullPath)) throw new ArgumentNullException(nameof(fullPath));
            if (limitBytes <= 0) throw new ArgumentOutOfRangeException(nameof(limitBytes));

            try
            {
                // Sharing is deliberately wide. An image the user happens to have open in an
                // editor is still perfectly readable, and refusing it would be a failure the
                // document did nothing to deserve.
                using (var stream = new FileStream(
                    fullPath,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete,
                    ChunkBytes,
                    FileOptions.SequentialScan))
                {
                    return ReadCapped(stream, limitBytes);
                }
            }
            catch (FileNotFoundException)
            {
                return null;
            }
            catch (DirectoryNotFoundException)
            {
                return null;
            }
        }

        /// <summary>
        /// Reads at most <paramref name="limitBytes"/> + 1 bytes, allocating no more than that.
        /// </summary>
        /// <remarks>
        /// The cap is on what is allocated, not just on what is returned. A four-gigabyte file
        /// named <c>.png</c> costs the limit and then stops, so the refusal cannot itself be the
        /// denial of service (NFR-8). The extra byte is what tells "exactly at the limit" from
        /// "over it".
        /// </remarks>
        internal static byte[] ReadCapped(Stream stream, long limitBytes)
        {
            var ceiling = limitBytes == long.MaxValue ? long.MaxValue : limitBytes + 1;
            var chunk = new byte[ChunkBytes];

            using (var accumulated = new MemoryStream(InitialCapacity(stream, ceiling)))
            {
                while (accumulated.Length < ceiling)
                {
                    var wanted = (int)Math.Min(chunk.Length, ceiling - accumulated.Length);
                    var read = stream.Read(chunk, 0, wanted);
                    if (read <= 0)
                    {
                        break;
                    }

                    accumulated.Write(chunk, 0, read);
                }

                return accumulated.ToArray();
            }
        }

        /// <summary>
        /// Sizes the buffer up front to avoid repeated doubling, but never beyond the ceiling —
        /// a reported length is a claim, and the read below is what decides how much is kept.
        /// </summary>
        private static int InitialCapacity(Stream stream, long ceiling)
        {
            long length;
            try
            {
                length = stream.CanSeek ? stream.Length : 0;
            }
            catch (NotSupportedException)
            {
                length = 0;
            }
            catch (IOException)
            {
                length = 0;
            }

            var capacity = Math.Min(length > 0 ? length : ChunkBytes, ceiling);
            return (int)Math.Min(capacity, int.MaxValue);
        }
    }
}
