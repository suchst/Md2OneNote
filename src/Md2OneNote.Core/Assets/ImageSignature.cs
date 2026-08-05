using System;

namespace Md2OneNote.Core
{
    /// <summary>What a file actually is, as opposed to what its extension claims.</summary>
    public sealed class ImageInfo
    {
        public ImageInfo(string format, int widthPx, int heightPx)
        {
            Format = format;
            WidthPx = widthPx;
            HeightPx = heightPx;
        }

        /// <summary>"png", "jpg" or "gif" — the value written to <c>one:Image/@format</c>.</summary>
        public string Format { get; }

        public int WidthPx { get; }

        public int HeightPx { get; }
    }

    /// <summary>
    /// Identifies an image and reads its intrinsic size from the bytes alone.
    /// </summary>
    /// <remarks>
    /// Two reasons this exists rather than a call into System.Drawing. First, DESIGN.md §9 refuses
    /// content that does not match a known image signature, and an extension is not evidence —
    /// a document can name anything <c>.png</c>. Second, decoding an untrusted image to learn its
    /// dimensions means handing attacker-chosen bytes to GDI+; reading a header does not.
    /// <para>
    /// Every read is bounds-checked and every loop is bounded. A truncated or malformed file
    /// yields null, which the caller turns into a placeholder (FR-10) — never an exception and
    /// never a hang (NFR-8).
    /// </para>
    /// </remarks>
    public static class ImageSignature
    {
        private static readonly byte[] Png = { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };

        public static ImageInfo Detect(byte[] bytes)
        {
            if (bytes == null)
            {
                return null;
            }

            if (StartsWith(bytes, Png))
            {
                return ReadPng(bytes);
            }

            if (IsGif(bytes))
            {
                return ReadGif(bytes);
            }

            if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
            {
                return ReadJpeg(bytes);
            }

            return null;
        }

        /// <summary>The IHDR chunk is required to come first, so its offsets are fixed.</summary>
        private static ImageInfo ReadPng(byte[] bytes)
        {
            if (bytes.Length < 24 || bytes[12] != 'I' || bytes[13] != 'H' || bytes[14] != 'D' || bytes[15] != 'R')
            {
                return null;
            }

            var width = BigEndianInt32(bytes, 16);
            var height = BigEndianInt32(bytes, 20);
            return Valid(width, height) ? new ImageInfo("png", width, height) : null;
        }

        private static bool IsGif(byte[] bytes)
        {
            return bytes.Length >= 6
                && bytes[0] == 'G' && bytes[1] == 'I' && bytes[2] == 'F'
                && bytes[3] == '8' && (bytes[4] == '7' || bytes[4] == '9') && bytes[5] == 'a';
        }

        private static ImageInfo ReadGif(byte[] bytes)
        {
            if (bytes.Length < 10)
            {
                return null;
            }

            var width = bytes[6] | (bytes[7] << 8);
            var height = bytes[8] | (bytes[9] << 8);
            return Valid(width, height) ? new ImageInfo("gif", width, height) : null;
        }

        /// <summary>
        /// Walks the segment chain to the frame header, which is the only place the dimensions
        /// live. The walk is bounded by the buffer and by any segment that claims a nonsensical
        /// length, so a crafted file cannot spin here.
        /// </summary>
        private static ImageInfo ReadJpeg(byte[] bytes)
        {
            var offset = 2;
            while (offset + 3 < bytes.Length)
            {
                if (bytes[offset] != 0xFF)
                {
                    return null;
                }

                var marker = bytes[offset + 1];

                // Fill bytes: any number of 0xFF may precede a marker.
                if (marker == 0xFF)
                {
                    offset++;
                    continue;
                }

                // Standalone markers carry no payload.
                if (marker == 0x01 || (marker >= 0xD0 && marker <= 0xD9))
                {
                    offset += 2;
                    continue;
                }

                var length = (bytes[offset + 2] << 8) | bytes[offset + 3];
                if (length < 2)
                {
                    return null;
                }

                if (IsFrameHeader(marker))
                {
                    if (offset + 9 >= bytes.Length)
                    {
                        return null;
                    }

                    var height = (bytes[offset + 5] << 8) | bytes[offset + 6];
                    var width = (bytes[offset + 7] << 8) | bytes[offset + 8];
                    return Valid(width, height) ? new ImageInfo("jpg", width, height) : null;
                }

                // Start of scan: entropy-coded data follows and there is no header left to find.
                if (marker == 0xDA)
                {
                    return null;
                }

                offset += 2 + length;
            }

            return null;
        }

        private static bool IsFrameHeader(byte marker)
        {
            // SOF0-SOF15, excluding DHT (C4), JPG (C8) and DAC (CC), which are not frame headers.
            return marker >= 0xC0 && marker <= 0xCF
                && marker != 0xC4 && marker != 0xC8 && marker != 0xCC;
        }

        private static int BigEndianInt32(byte[] bytes, int offset)
        {
            return (bytes[offset] << 24) | (bytes[offset + 1] << 16) | (bytes[offset + 2] << 8) | bytes[offset + 3];
        }

        /// <summary>
        /// Dimensions have to be usable as a <c>one:Size</c>. A zero or negative value would be
        /// rejected by the schema, and an absurd one is a claim no real image makes.
        /// </summary>
        private static bool Valid(int width, int height)
        {
            const int Ceiling = 60000;
            return width > 0 && height > 0 && width <= Ceiling && height <= Ceiling;
        }

        private static bool StartsWith(byte[] bytes, byte[] prefix)
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
