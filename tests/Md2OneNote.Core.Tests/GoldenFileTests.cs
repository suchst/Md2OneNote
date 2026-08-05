using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    /// <summary>
    /// The whole conversion, pinned. NFR-21 asks for exactly this: Markdown to OneNote XML covered
    /// by golden files, with no OneNote, no WebView2 and no UI thread anywhere near it.
    /// </summary>
    /// <remarks>
    /// Diagram and asset outcomes are fabricated — the first of each succeeds and the rest fail —
    /// so that every sample exercises both the embedded and the fallback path (FR-10, FR-14)
    /// without depending on a browser or the file system.
    /// </remarks>
    public class GoldenFileTests
    {
        private static readonly byte[] TinyPng =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
            0x00, 0x00, 0x00, 0x0D, 0x49, 0x48, 0x44, 0x52
        };

        [Theory]
        [InlineData("basic")]
        [InlineData("lists")]
        [InlineData("tables")]
        [InlineData("code")]
        [InlineData("diagrams")]
        [InlineData("kitchen-sink")]
        public void Sample_matches_its_golden_page(string name)
        {
            var markdown = File.ReadAllText(Path.Combine(Directory("Samples"), name + ".md"));
            var parsed = Convert.Parse(markdown, "mermaid", "dot");

            var diagrams = new Dictionary<string, DiagramOutcome>();
            for (var i = 0; i < parsed.Diagrams.Count; i++)
            {
                diagrams[parsed.Diagrams[i].Key] = i == 0
                    ? DiagramOutcome.Success(TinyPng, 720, 480)
                    : DiagramOutcome.Failure("diagram render failed");
            }

            var assets = new Dictionary<string, AssetOutcome>();
            for (var i = 0; i < parsed.Assets.Count; i++)
            {
                assets[parsed.Assets[i].Key] = i == 0
                    ? AssetOutcome.Success(TinyPng, "png", 900, 300)
                    : AssetOutcome.Failure("the file could not be read");
            }

            // Reformatted before comparison: the renderer emits compact XML so that no stray
            // whitespace reaches OneNote, but a golden file has to be reviewable in a diff.
            var actual = Convert.Render(parsed, diagrams, assets).ToString().Replace("\r\n", "\n");
            var goldenPath = Path.Combine(Directory("Golden"), name + ".xml");

            if (!File.Exists(goldenPath))
            {
                File.WriteAllText(goldenPath, actual, new UTF8Encoding(false));
                Assert.Fail("Wrote a new golden file at " + goldenPath + ". Review it, then re-run.");
            }

            var expected = File.ReadAllText(goldenPath).Replace("\r\n", "\n");
            Assert.Equal(expected.TrimEnd('\n'), actual.TrimEnd('\n'));
        }

        /// <summary>
        /// Resolved from the source tree, not the output folder, so that a regenerated golden is a
        /// reviewable change in the repository rather than a file in bin.
        /// </summary>
        private static string Directory(string name, [CallerFilePath] string callerFile = null)
        {
            var path = Path.Combine(Path.GetDirectoryName(callerFile), name);
            System.IO.Directory.CreateDirectory(path);
            return path;
        }
    }
}
