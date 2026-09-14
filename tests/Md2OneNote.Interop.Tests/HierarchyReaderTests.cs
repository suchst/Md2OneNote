using System.Linq;
using Md2OneNote.Interop;
using Xunit;

namespace Md2OneNote.Interop.Tests
{
    /// <summary>
    /// Hierarchy XML shaped the way OneNote returns it from
    /// <c>GetHierarchy(_, hsPages, _, xs2013)</c>: notebooks containing sections and section
    /// groups, with the viewed section flagged.
    /// </summary>
    public class HierarchyReaderTests
    {
        private const string Ns = "http://schemas.microsoft.com/office/onenote/2013/onenote";

        private static string Hierarchy(string body)
        {
            return "<one:Notebooks xmlns:one=\"" + Ns + "\">" + body + "</one:Notebooks>";
        }

        [Fact]
        public void FindActiveSection_returns_the_viewed_section()
        {
            var xml = Hierarchy(
                "<one:Notebook name=\"Personal\" ID=\"{nb}\">" +
                "  <one:Section name=\"Archive\" ID=\"{s1}\" />" +
                "  <one:Section name=\"General\" ID=\"{s2}\" isCurrentlyViewed=\"true\" />" +
                "</one:Notebook>");

            var section = HierarchyReader.FindActiveSection(xml);

            Assert.NotNull(section);
            Assert.Equal("{s2}", section.Id);
            Assert.Equal("General", section.Name);
        }

        [Fact]
        public void FindActiveSection_finds_a_section_nested_in_section_groups()
        {
            var xml = Hierarchy(
                "<one:Notebook name=\"Work\" ID=\"{nb}\">" +
                "  <one:SectionGroup name=\"Outer\" ID=\"{g1}\">" +
                "    <one:SectionGroup name=\"Inner\" ID=\"{g2}\">" +
                "      <one:Section name=\"Deep\" ID=\"{s}\" isCurrentlyViewed=\"true\" />" +
                "    </one:SectionGroup>" +
                "  </one:SectionGroup>" +
                "</one:Notebook>");

            Assert.Equal("{s}", HierarchyReader.FindActiveSection(xml).Id);
        }

        [Fact]
        public void FindActiveSection_returns_null_when_nothing_is_viewed()
        {
            var xml = Hierarchy(
                "<one:Notebook name=\"Personal\" ID=\"{nb}\">" +
                "  <one:Section name=\"General\" ID=\"{s}\" />" +
                "</one:Notebook>");

            Assert.Null(HierarchyReader.FindActiveSection(xml));
        }

        [Fact]
        public void FindActiveSection_ignores_a_viewed_section_group()
        {
            // A section group is a folder, not a place pages can go. Treating one as the target
            // would put the import somewhere that cannot hold a page (FR-6).
            var xml = Hierarchy(
                "<one:Notebook name=\"Work\" ID=\"{nb}\" isCurrentlyViewed=\"true\">" +
                "  <one:SectionGroup name=\"Group\" ID=\"{g}\" isCurrentlyViewed=\"true\" />" +
                "</one:Notebook>");

            Assert.Null(HierarchyReader.FindActiveSection(xml));
        }

        [Fact]
        public void FindActiveSection_reads_the_2010_namespace_too()
        {
            // Nothing depends on which schema OneNote answered with.
            var xml =
                "<one:Notebooks xmlns:one=\"http://schemas.microsoft.com/office/onenote/2010/onenote\">" +
                "  <one:Section name=\"General\" ID=\"{s}\" isCurrentlyViewed=\"true\" />" +
                "</one:Notebooks>";

            Assert.Equal("{s}", HierarchyReader.FindActiveSection(xml).Id);
        }

        [Fact]
        public void FindActiveSection_skips_a_viewed_section_that_has_no_id()
        {
            var xml = Hierarchy(
                "<one:Section name=\"Broken\" isCurrentlyViewed=\"true\" />" +
                "<one:Section name=\"Usable\" ID=\"{s}\" isCurrentlyViewed=\"true\" />");

            Assert.Equal("{s}", HierarchyReader.FindActiveSection(xml).Id);
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("<one:Notebooks><unclosed>")]
        public void FindActiveSection_treats_unusable_input_as_no_match(string xml)
        {
            Assert.Null(HierarchyReader.FindActiveSection(xml));
        }

        [Fact]
        public void ReadPages_returns_every_page_in_document_order()
        {
            var xml = Hierarchy(
                "<one:Section name=\"General\" ID=\"{s}\">" +
                "  <one:Page ID=\"{p1}\" name=\"First\" pageLevel=\"1\" />" +
                "  <one:Page ID=\"{p2}\" name=\"Second\" pageLevel=\"2\" />" +
                "</one:Section>");

            var pages = HierarchyReader.ReadPages(xml);

            Assert.Equal(new[] { "{p1}", "{p2}" }, pages.Select(p => p.Id));
            Assert.Equal(new[] { "First", "Second" }, pages.Select(p => p.Title));
        }

        [Fact]
        public void ReadPages_tolerates_a_page_with_no_name()
        {
            var xml = Hierarchy("<one:Page ID=\"{p}\" />");

            var page = Assert.Single(HierarchyReader.ReadPages(xml));
            Assert.Equal("{p}", page.Id);
            Assert.Equal(string.Empty, page.Title);
        }

        [Fact]
        public void ReadPages_returns_empty_when_the_section_has_none()
        {
            var xml = Hierarchy("<one:Section name=\"Empty\" ID=\"{s}\" />");

            Assert.Empty(HierarchyReader.ReadPages(xml));
        }

        [Theory]
        [InlineData("")]
        [InlineData(null)]
        [InlineData("not xml at all")]
        public void ReadPages_treats_unusable_input_as_empty(string xml)
        {
            Assert.Empty(HierarchyReader.ReadPages(xml));
        }
    }
}
