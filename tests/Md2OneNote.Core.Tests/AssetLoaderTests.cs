using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    public class AssetLoaderTests
    {
        private const string Base = @"C:\notes\project";

        private readonly FakeFiles _files = new FakeFiles();

        private AssetOutcome Load(string rawPath, AssetPolicy policy = null)
        {
            var loader = new AssetLoader(_files, TestCatalog.Instance);
            return loader.Load(AssetRequest.Create(rawPath, "alt"), Base, policy ?? AssetPolicy.Default);
        }

        [Fact]
        public void An_image_under_the_document_loads_with_the_size_from_its_bytes()
        {
            _files.Add(@"C:\notes\project\logo.png", Png(320, 200));

            var outcome = Load("logo.png");

            Assert.True(outcome.Succeeded);
            Assert.Equal("png", outcome.Format);
            Assert.Equal(320, outcome.WidthPx);
            Assert.Equal(200, outcome.HeightPx);
            Assert.Null(outcome.Warning);
        }

        [Fact]
        public void The_reader_is_handed_the_resolved_path_and_the_limit()
        {
            _files.Add(@"C:\notes\project\images\logo.png", Png(10, 10));

            Load("./images/../images/logo.png");

            Assert.Equal(@"C:\notes\project\images\logo.png", Assert.Single(_files.Reads));
            Assert.Equal(AssetPolicy.Default.MaxBytes, _files.LastLimit);
        }

        [Fact]
        public void A_refused_path_is_never_opened()
        {
            // The point of refusing a UNC path is that the handshake does not happen. A rejection
            // reported after the read would be a log entry, not a defence.
            var outcome = Load(@"\\server\share\logo.png");

            Assert.False(outcome.Succeeded);
            Assert.Empty(_files.Reads);
            Assert.Contains(@"\\server\share\logo.png", outcome.FailureMessage);
        }

        [Fact]
        public void An_image_outside_the_document_folder_loads_but_says_so()
        {
            _files.Add(@"C:\notes\shared\logo.png", Png(64, 64));

            var outcome = Load("../shared/logo.png");

            Assert.True(outcome.Succeeded);
            Assert.NotNull(outcome.Warning);
            Assert.Contains("outside document", outcome.Warning);
        }

        [Fact]
        public void A_missing_file_is_a_placeholder_rather_than_a_failed_import()
        {
            var outcome = Load("logo.png");

            Assert.False(outcome.Succeeded);
            Assert.Equal("not found: logo.png", outcome.FailureMessage);
        }

        [Fact]
        public void A_file_that_cannot_be_opened_reports_why()
        {
            _files.Add(@"C:\notes\project\logo.png", Png(10, 10));
            _files.Throw = new UnauthorizedAccessException("access is denied");

            var outcome = Load("logo.png");

            Assert.False(outcome.Succeeded);
            Assert.Contains("access is denied", outcome.FailureMessage);
        }

        [Fact]
        public void A_reader_that_throws_something_unexpected_still_produces_an_outcome()
        {
            // Loading an image is not worth losing the page over, whatever the disk does.
            _files.Throw = new InvalidOperationException("boom");

            var outcome = Load("logo.png");

            Assert.False(outcome.Succeeded);
            Assert.NotNull(outcome.FailureMessage);
        }

        [Fact]
        public void A_file_over_the_limit_is_refused_without_being_held_whole()
        {
            var policy = new AssetPolicy(8, true, AssetPolicy.Default.AllowedExtensions);
            _files.Add(@"C:\notes\project\logo.png", Png(1000, 1000));

            var outcome = Load("logo.png", policy);

            Assert.False(outcome.Succeeded);
            Assert.Equal("too large: logo.png / 8", outcome.FailureMessage);

            // The reader stopped one byte past the limit, which is all it takes to know.
            Assert.Equal(9, _files.LastReadLength);
        }

        [Fact]
        public void A_file_exactly_at_the_limit_is_allowed()
        {
            var bytes = Png(4, 4);
            var policy = new AssetPolicy(bytes.Length, true, AssetPolicy.Default.AllowedExtensions);
            _files.Add(@"C:\notes\project\logo.png", bytes);

            Assert.True(Load("logo.png", policy).Succeeded);
        }

        [Fact]
        public void A_file_whose_content_is_not_an_image_is_refused()
        {
            // The extension said .png. The bytes are what count (DESIGN.md §9).
            _files.Add(@"C:\notes\project\logo.png", System.Text.Encoding.ASCII.GetBytes("#!/bin/sh\nrm -rf /"));

            var outcome = Load("logo.png");

            Assert.False(outcome.Succeeded);
            Assert.Equal("not an image: logo.png", outcome.FailureMessage);
        }

        [Fact]
        public void An_empty_file_is_refused()
        {
            _files.Add(@"C:\notes\project\logo.png", new byte[0]);

            Assert.False(Load("logo.png").Succeeded);
        }

        [Fact]
        public void A_mislabelled_but_genuine_image_embeds_under_its_real_format()
        {
            // A GIF named .png is careless, not hostile, and OneNote has to be told what it is.
            _files.Add(@"C:\notes\project\logo.png", Gif(48, 24));

            var outcome = Load("logo.png");

            Assert.True(outcome.Succeeded);
            Assert.Equal("gif", outcome.Format);
            Assert.Equal(48, outcome.WidthPx);
        }

        [Fact]
        public void A_truncated_image_is_refused_rather_than_embedded_at_zero_size()
        {
            _files.Add(@"C:\notes\project\logo.png", Png(100, 100).Take(12).ToArray());

            Assert.False(Load("logo.png").Succeeded);
        }

        [Fact]
        public void Every_rejection_carries_a_message()
        {
            var paths = new[]
            {
                "https://example.com/logo.png",
                @"C:\secrets\logo.png",
                "notes.txt",
                "",
                "logo.png"
            };

            foreach (var path in paths)
            {
                var outcome = Load(path);

                Assert.False(outcome.Succeeded, path);
                Assert.False(string.IsNullOrWhiteSpace(outcome.FailureMessage), path);
            }
        }

        [Fact]
        public void The_loader_rejects_missing_arguments_rather_than_guessing()
        {
            var loader = new AssetLoader(_files, TestCatalog.Instance);

            Assert.Throws<ArgumentNullException>(() => loader.Load(null, Base, AssetPolicy.Default));
            Assert.Throws<ArgumentNullException>(() => loader.Load(AssetRequest.Create("logo.png", ""), Base, null));
            Assert.Throws<ArgumentNullException>(() => new AssetLoader(null, TestCatalog.Instance));
            Assert.Throws<ArgumentNullException>(() => new AssetLoader(_files, null));
        }

        /// <summary>
        /// Honours the <see cref="IFileBytes"/> contract, including the stop-one-past-the-limit
        /// rule, so the loader is tested against the reader it will actually be given.
        /// </summary>
        private sealed class FakeFiles : IFileBytes
        {
            private readonly Dictionary<string, byte[]> _contents =
                new Dictionary<string, byte[]>(StringComparer.OrdinalIgnoreCase);

            public List<string> Reads { get; } = new List<string>();

            public long LastLimit { get; private set; }

            public int LastReadLength { get; private set; }

            public Exception Throw { get; set; }

            public void Add(string fullPath, byte[] bytes)
            {
                _contents[fullPath] = bytes;
            }

            public byte[] Read(string fullPath, long limitBytes)
            {
                Reads.Add(fullPath);
                LastLimit = limitBytes;

                if (Throw != null)
                {
                    throw Throw;
                }

                byte[] bytes;
                if (!_contents.TryGetValue(fullPath, out bytes))
                {
                    return null;
                }

                if (bytes.LongLength > limitBytes)
                {
                    bytes = bytes.Take((int)limitBytes + 1).ToArray();
                }

                LastReadLength = bytes.Length;
                return bytes;
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

        private static byte[] Gif(int width, int height)
        {
            var bytes = new List<byte>(System.Text.Encoding.ASCII.GetBytes("GIF89a"))
            {
                (byte)(width & 0xFF),
                (byte)(width >> 8),
                (byte)(height & 0xFF),
                (byte)(height >> 8),
                0x00, 0x00, 0x00
            };

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
