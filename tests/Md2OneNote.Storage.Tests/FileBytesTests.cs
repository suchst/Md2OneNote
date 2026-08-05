using System;
using System.IO;
using System.Linq;
using Xunit;

namespace Md2OneNote.Storage.Tests
{
    public class FileBytesTests
    {
        private readonly FileBytes _files = new FileBytes();

        [Fact]
        public void A_file_within_the_limit_comes_back_whole()
        {
            using (var temp = new TempDirectory())
            {
                var content = Enumerable.Range(0, 5000).Select(i => (byte)i).ToArray();
                var path = temp.Write("logo.png", content);

                Assert.Equal(content, _files.Read(path, 1024 * 1024));
            }
        }

        [Fact]
        public void A_file_exactly_at_the_limit_comes_back_whole()
        {
            using (var temp = new TempDirectory())
            {
                var content = new byte[64];
                var path = temp.Write("logo.png", content);

                Assert.Equal(64, _files.Read(path, 64).Length);
            }
        }

        [Fact]
        public void A_file_over_the_limit_stops_one_byte_past_it()
        {
            // The caller only needs to know that the limit was exceeded, and reading further to
            // find out by how much is exactly the denial of service the limit exists to prevent.
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("huge.png", new byte[512 * 1024]);

                Assert.Equal(65, _files.Read(path, 64).Length);
            }
        }

        [Fact]
        public void An_empty_file_comes_back_empty_rather_than_missing()
        {
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("empty.png", new byte[0]);

                Assert.Empty(_files.Read(path, 1024));
            }
        }

        [Fact]
        public void A_missing_file_is_null()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Null(_files.Read(temp.PathTo("absent.png"), 1024));
            }
        }

        [Fact]
        public void A_missing_directory_is_null_too()
        {
            using (var temp = new TempDirectory())
            {
                Assert.Null(_files.Read(temp.PathTo(@"no-such-folder\logo.png"), 1024));
            }
        }

        [Fact]
        public void A_file_someone_else_has_open_is_still_readable()
        {
            // An image open in an editor is perfectly readable, and failing on it would be a
            // failure the document did nothing to deserve.
            using (var temp = new TempDirectory())
            {
                var path = temp.Write("open.png", new byte[] { 1, 2, 3 });

                using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.ReadWrite))
                {
                    Assert.Equal(new byte[] { 1, 2, 3 }, _files.Read(path, 1024));
                }
            }
        }

        [Fact]
        public void A_directory_is_not_quietly_reported_as_a_missing_file()
        {
            // Null means "nothing there". A path that exists but cannot be opened is a different
            // answer, and the caller turns the reason into a message the user can act on.
            using (var temp = new TempDirectory())
            {
                Assert.ThrowsAny<Exception>(() => _files.Read(temp.Path, 1024));
            }
        }

        [Fact]
        public void The_reader_rejects_arguments_it_cannot_honour()
        {
            Assert.Throws<ArgumentNullException>(() => _files.Read(null, 1024));
            Assert.Throws<ArgumentNullException>(() => _files.Read(string.Empty, 1024));
            Assert.Throws<ArgumentOutOfRangeException>(() => _files.Read(@"C:\x.png", 0));
            Assert.Throws<ArgumentOutOfRangeException>(() => _files.Read(@"C:\x.png", -1));
        }

        [Fact]
        public void A_stream_that_lies_about_its_length_cannot_make_the_reader_overrun()
        {
            // Length is a claim; the read is what decides. A stream reporting more than it has
            // must not produce a buffer padded with whatever was in memory.
            var stream = new LyingStream(new byte[] { 7, 7, 7 }, claimedLength: 1024 * 1024);

            Assert.Equal(new byte[] { 7, 7, 7 }, FileBytes.ReadCapped(stream, 4096));
        }

        [Fact]
        public void A_stream_returning_partial_reads_is_still_read_to_the_end()
        {
            var content = Enumerable.Range(0, 300).Select(i => (byte)i).ToArray();
            var stream = new DripStream(content, bytesPerRead: 7);

            Assert.Equal(content, FileBytes.ReadCapped(stream, 4096));
        }

        /// <summary>A stream whose <see cref="Length"/> is larger than its content.</summary>
        private sealed class LyingStream : MemoryStream
        {
            private readonly long _claimed;

            public LyingStream(byte[] content, long claimedLength) : base(content, false)
            {
                _claimed = claimedLength;
            }

            public override long Length
            {
                get { return _claimed; }
            }
        }

        /// <summary>A stream that returns less than asked for, as a network-backed one may.</summary>
        private sealed class DripStream : MemoryStream
        {
            private readonly int _bytesPerRead;

            public DripStream(byte[] content, int bytesPerRead) : base(content, false)
            {
                _bytesPerRead = bytesPerRead;
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                return base.Read(buffer, offset, Math.Min(count, _bytesPerRead));
            }
        }
    }
}
