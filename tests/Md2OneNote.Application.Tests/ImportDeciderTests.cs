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

        [Fact]
        public void Identical_hash_supersedes_when_the_user_asked_for_it_again()
        {
            // Still never "overwrite": a second copy the user asked for is a superseding page.
            var match = PageMatch.Hit("page-1", "sha256:aaa");

            var decision = ImportDecider.Decide(match, "sha256:aaa", reimportUnchanged: true);

            Assert.Equal(ImportDecision.CreateSuperseding, decision);
        }

        [Fact]
        public void The_flag_changes_nothing_for_a_file_never_imported_before()
        {
            Assert.Equal(ImportDecision.Create, ImportDecider.Decide(PageMatch.None, "sha256:aaa", reimportUnchanged: true));
        }
    }
}
