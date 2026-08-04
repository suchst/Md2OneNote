using Md2OneNote.Core;
using Xunit;

namespace Md2OneNote.Application.Tests
{
    /// <summary>
    /// The FR-19 decision matrix. Small, but it is the rule that keeps the product from
    /// destroying user work, so it is pinned exhaustively.
    /// </summary>
    public class ImportDeciderTests
    {
        [Fact]
        public void No_previous_import_creates_a_page()
        {
            var decision = ImportDecider.Decide(PageMatch.None, "sha256:aaa");

            Assert.Equal(ImportDecision.Create, decision);
        }

        [Fact]
        public void Identical_hash_skips()
        {
            var match = PageMatch.Hit("page-1", "sha256:aaa");

            var decision = ImportDecider.Decide(match, "sha256:aaa");

            Assert.Equal(ImportDecision.Skip, decision);
        }

        [Fact]
        public void Changed_hash_supersedes_rather_than_overwrites()
        {
            var match = PageMatch.Hit("page-1", "sha256:aaa");

            var decision = ImportDecider.Decide(match, "sha256:bbb");

            Assert.Equal(ImportDecision.CreateSuperseding, decision);
        }
    }
}
