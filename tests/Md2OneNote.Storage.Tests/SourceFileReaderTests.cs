using System;
using System.IO;
using System.Text;
using Md2OneNote.Core;
using Xunit;

namespace Md2OneNote.Storage.Tests
{
    public class SourceFileReaderTests
    {
        private readonly SourceFileReader _reader = new SourceFileReader();

        [Fact]
        public void A_file_is_read_as_text_bytes_and_a_base_directory()
        {
            using (var temp = new TempDirectory())
            {
                var bytes = Encoding.UTF8.GetBytes("# Hello\n\ntext\n");
                var path = temp.Write("doc.md", bytes);

                var file = _reader.Read(path, 1024 * 1024);

                Assert.Equal(path, file.Path);
                Assert.Equal(temp.Path, file.BaseDirectory);
                Assert.Equal("# Hello\n\ntext\n", file.Text);
                Assert.Equal(bytes, file.Bytes);
            }
        }

        [Fact]
        public void Relative_image_references_resolve_against_the_document_not_the_process()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.Write(@"chapter\one.md", Encoding.UTF8.GetBytes("![x](logo.png)"));

                var file = _reader.Read(path, 1024);

                Assert.Equal(Path.Combine(temp.Path, "chapter"), file.BaseDirectory);
            }
        }

        [Fact]
        public void The_path_is_made_absolute()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("doc.md", Encoding.UTF8.GetBytes("x"));
                var awkward = Path.Combine(temp.Path, ".", "doc.md");

                Assert.Equal(path, _reader.Read(awkward, 1024).Path);
            }
        }

        [Theory]
        [InlineData("utf8-bom")]
        [InlineData("utf16-le")]
        [InlineData("utf16-be")]
        [InlineData("utf32-le")]
        [InlineData("utf32-be")]
        [InlineData("utf8-plain")]
        public void Text_is_decoded_by_its_byte_order_mark_which_is_then_removed(string encodingName)
        {
            // Left in, the mark becomes a zero-width character ahead of the '#', and a document
            // that opens with a title silently stops having one.
            const string text = "# Título\n\nDécor.\n";

            using (var temp = new TempDirectory())
            {
                var path = temp.Write("doc.md", Encode(encodingName, text));

                var file = _reader.Read(path, 1024 * 1024);

                Assert.Equal(text, file.Text);
                Assert.StartsWith("# ", file.Text);
            }
        }

        [Fact]
        public void The_bytes_kept_for_identity_are_the_file_as_written()
        {
            // The hash is taken over these. Stripping the mark from them would make a page's
            // identity depend on how the text was decoded.
            using (var temp = new TempDirectory())
            {
                var written = Encode("utf8-bom", "# Hi");
                var path = temp.Write("doc.md", written);

                Assert.Equal(written, _reader.Read(path, 1024).Bytes);
            }
        }

        [Fact]
        public void A_file_that_is_not_valid_utf8_imports_with_odd_characters_rather_than_failing()
        {
            // One stray byte in an otherwise fine document is not a reason to lose the file (FR-3).
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("doc.md", new byte[] { 0x23, 0x20, 0x48, 0x69, 0x20, 0xFF, 0x0A });

                var file = _reader.Read(path, 1024);

                Assert.StartsWith("# Hi", file.Text);
            }
        }

        [Fact]
        public void An_empty_file_reads_as_empty_text()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("doc.md", new byte[0]);

                Assert.Equal(string.Empty, _reader.Read(path, 1024).Text);
            }
        }

        [Fact]
        public void A_file_over_the_limit_is_refused()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("doc.md", new byte[4096]);

                Assert.Throws<SourceFileTooLargeException>(() => _reader.Read(path, 1024));
            }
        }

        [Fact]
        public void A_file_exactly_at_the_limit_is_accepted()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("doc.md", new byte[1024]);

                Assert.Equal(1024, _reader.Read(path, 1024).Bytes.Length);
            }
        }

        [Fact]
        public void A_missing_file_throws_so_the_reason_reaches_the_report()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Throws<FileNotFoundException>(() => _reader.Read(temp.PathTo("absent.md"), 1024));
            }
        }

        [Fact]
        public void The_reader_rejects_arguments_it_cannot_honour()
        {
            Assert.Throws<ArgumentNullException>(() => _reader.Read(null, 1024));
            Assert.Throws<ArgumentOutOfRangeException>(() => _reader.Read(@"C:\doc.md", 0));
        }

        private static byte[] Encode(string encodingName, string text)
        {
            switch (encodingName)
            {
                case "utf8-bom":
                    return Prefix(new byte[] { 0xEF, 0xBB, 0xBF }, Encoding.UTF8.GetBytes(text));
                case "utf8-plain":
                    return Encoding.UTF8.GetBytes(text);
                case "utf16-le":
                    return Prefix(new byte[] { 0xFF, 0xFE }, new UnicodeEncoding(false, false).GetBytes(text));
                case "utf16-be":
                    return Prefix(new byte[] { 0xFE, 0xFF }, new UnicodeEncoding(true, false).GetBytes(text));
                case "utf32-le":
                    return Prefix(new byte[] { 0xFF, 0xFE, 0x00, 0x00 }, new UTF32Encoding(false, false).GetBytes(text));
                case "utf32-be":
                    return Prefix(new byte[] { 0x00, 0x00, 0xFE, 0xFF }, new UTF32Encoding(true, false).GetBytes(text));
                default:
                    throw new ArgumentException(encodingName, nameof(encodingName));
            }
        }

        private static byte[] Prefix(byte[] bom, byte[] body)
        {
            var bytes = new byte[bom.Length + body.Length];
            Buffer.BlockCopy(bom, 0, bytes, 0, bom.Length);
            Buffer.BlockCopy(body, 0, bytes, bom.Length, body.Length);
            return bytes;
        }
    }
}
