using System;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// Reads the pixel size out of a PNG's header, so a capture can be checked against the
    /// size that was asked for before it is handed on.
    /// </summary>
    /// <remarks>
    /// The check exists because the two ways a capture went wrong today (2026-09-14) both
    /// produced a valid PNG of the wrong size: a diagram squeezed by the viewport, and one cut
    /// off by the screen-sized cap on the host window. OneNote then stretched the picture to
    /// the size it was told, which is the blur the user saw. A wrong size is a failed render.
    /// </remarks>
    public static class PngHeader
    {
        private static readonly byte[] Signature = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        /// <summary>The width and height from the IHDR chunk, or null when this is not a PNG.</summary>
        public static Tuple<int, int> Size(byte[] png)
        {
            if (png == null || png.Length < 24)
            {
                return null;
            }

            for (var i = 0; i < Signature.Length; i++)
            {
                if (png[i] != Signature[i])
                {
                    return null;
                }
            }

            // Bytes 12..15 are the chunk type of the first chunk, which the format requires to be IHDR.
            if (png[12] != 'I' || png[13] != 'H' || png[14] != 'D' || png[15] != 'R')
            {
                return null;
            }

            var width = BigEndian(png, 16);
            var height = BigEndian(png, 20);
            if (width <= 0 || height <= 0)
            {
                return null;
            }

            return Tuple.Create(width, height);
        }

        private static int BigEndian(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }
    }
}
