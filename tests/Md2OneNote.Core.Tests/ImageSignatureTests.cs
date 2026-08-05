using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    public class ImageSignatureTests
    {
        [Fact]
        public void A_png_header_yields_its_format_and_size()
        {
            var info = ImageSignature.Detect(Png(1280, 720));

            Assert.Equal("png", info.Format);
            Assert.Equal(1280, info.WidthPx);
            Assert.Equal(720, info.HeightPx);
        }

        [Theory]
        [InlineData("GIF87a")]
        [InlineData("GIF89a")]
        public void A_gif_header_yields_its_format_and_size(string magic)
        {
            var info = ImageSignature.Detect(Gif(magic, 640, 480));

            Assert.Equal("gif", info.Format);
            Assert.Equal(640, info.WidthPx);
            Assert.Equal(480, info.HeightPx);
        }

        [Fact]
        public void A_jpeg_frame_header_yields_its_format_and_size()
        {
            var info = ImageSignature.Detect(Jpeg(800, 600));

            Assert.Equal("jpg", info.Format);
            Assert.Equal(800, info.WidthPx);
            Assert.Equal(600, info.HeightPx);
        }

        [Fact]
        public void A_jpeg_with_metadata_segments_before_the_frame_still_reads()
        {
            var info = ImageSignature.Detect(Jpeg(320, 240, withExifSegment: true));

            Assert.Equal(320, info.WidthPx);
            Assert.Equal(240, info.HeightPx);
        }

        [Fact]
        public void An_extension_is_not_evidence_of_content()
        {
            // The whole point: a document can name any file .png.
            var text = System.Text.Encoding.ASCII.GetBytes("BEGIN RSA PRIVATE KEY");

            Assert.Null(ImageSignature.Detect(text));
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        [InlineData(7)]
        [InlineData(23)]
        public void A_truncated_file_yields_nothing_rather_than_throwing(int length)
        {
            var truncated = Png(100, 100).Take(length).ToArray();

            Assert.Null(ImageSignature.Detect(truncated));
        }

        [Fact]
        public void A_null_buffer_yields_nothing()
        {
            Assert.Null(ImageSignature.Detect(null));
        }

        [Fact]
        public void A_png_claiming_zero_dimensions_is_refused()
        {
            Assert.Null(ImageSignature.Detect(Png(0, 100)));
        }

        [Fact]
        public void A_png_claiming_absurd_dimensions_is_refused()
        {
            // A one:Size of two billion is a claim, not an image.
            Assert.Null(ImageSignature.Detect(Png(int.MaxValue, int.MaxValue)));
        }

        [Fact]
        public void A_jpeg_whose_segments_never_terminate_does_not_hang()
        {
            // Every segment claims a length of two, so the walk advances by four and cannot loop.
            var bytes = new List<byte> { 0xFF, 0xD8, 0xFF };
            for (var i = 0; i < 5000; i++)
            {
                bytes.AddRange(new byte[] { 0xFF, 0xE0, 0x00, 0x02 });
            }

            Assert.Null(ImageSignature.Detect(bytes.ToArray()));
        }

        [Fact]
        public void A_jpeg_segment_claiming_an_impossible_length_is_refused()
        {
            var bytes = new byte[] { 0xFF, 0xD8, 0xFF, 0xE0, 0x00, 0x00, 0x00, 0x00 };

            Assert.Null(ImageSignature.Detect(bytes));
        }

        [Fact]
        public void Random_bytes_never_produce_an_image()
        {
            var random = new Random(20260805);
            for (var i = 0; i < 500; i++)
            {
                var bytes = new byte[64];
                random.NextBytes(bytes);

                var info = ImageSignature.Detect(bytes);
                if (info != null)
                {
                    Assert.InRange(info.WidthPx, 1, 60000);
                    Assert.InRange(info.HeightPx, 1, 60000);
                }
            }
        }

        private static byte[] Png(int width, int height)
        {
            var bytes = new List<byte> { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A };
            bytes.AddRange(new byte[] { 0x00, 0x00, 0x00, 0x0D });
            bytes.AddRange(System.Text.Encoding.ASCII.GetBytes("IHDR"));
            bytes.AddRange(BigEndian(width));
            bytes.AddRange(BigEndian(height));
            bytes.AddRange(new byte[] { 0x08, 0x06, 0x00, 0x00, 0x00 });
            return bytes.ToArray();
        }

        private static byte[] Gif(string magic, int width, int height)
        {
            var bytes = new List<byte>(System.Text.Encoding.ASCII.GetBytes(magic));
            bytes.Add((byte)(width & 0xFF));
            bytes.Add((byte)(width >> 8));
            bytes.Add((byte)(height & 0xFF));
            bytes.Add((byte)(height >> 8));
            bytes.AddRange(new byte[] { 0x00, 0x00, 0x00 });
            return bytes.ToArray();
        }

        private static byte[] Jpeg(int width, int height, bool withExifSegment = false)
        {
            var bytes = new List<byte> { 0xFF, 0xD8 };

            if (withExifSegment)
            {
                bytes.AddRange(new byte[] { 0xFF, 0xE1, 0x00, 0x08, 1, 2, 3, 4, 5, 6 });
            }

            bytes.AddRange(new byte[] { 0xFF, 0xC0, 0x00, 0x11, 0x08 });
            bytes.Add((byte)(height >> 8));
            bytes.Add((byte)(height & 0xFF));
            bytes.Add((byte)(width >> 8));
            bytes.Add((byte)(width & 0xFF));
            bytes.AddRange(new byte[] { 0x03, 0x01, 0x22, 0x00 });
            return bytes.ToArray();
        }

        private static byte[] BigEndian(int value)
        {
            return new[]
            {
                (byte)(value >> 24),
                (byte)(value >> 16),
                (byte)(value >> 8),
                (byte)value
            };
        }
    }
}
