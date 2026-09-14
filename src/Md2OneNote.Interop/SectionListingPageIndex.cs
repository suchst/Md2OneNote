using System;
using Md2OneNote.Core;

namespace Md2OneNote.Interop
{
    /// <summary>
    /// <see cref="IPageIndex"/> over one <c>GetHierarchy</c> call. OneNote includes each page's
    /// <c>one:Meta</c> entries in a section listing (docs/page-schema-notes.md §8, verified
    /// 2026-09-14), so the pages themselves are the index and nothing needs to be cached.
    /// </summary>
    /// <remarks>
    /// DESIGN.md §7.3 planned a JSON cache under <c>%LOCALAPPDATA%</c> for the case where the
    /// listing carried no metadata. It does, so the cache never existed. This is the "trivial
    /// implementation over that call" the design reserved for this outcome: the listing is the
    /// authority, a hit needs no confirmation, and <see cref="Record"/> has nothing to do because
    /// the page written a moment ago already carries its own metadata.
    /// </remarks>
    public sealed class SectionListingPageIndex : IPageIndex
    {
        private readonly OneNoteGateway _gateway;

        public SectionListingPageIndex(OneNoteGateway gateway)
        {
            if (gateway == null) throw new ArgumentNullException(nameof(gateway));

            _gateway = gateway;
        }

        public PageMatch TryFind(string sectionId, string sourcePath)
        {
            if (string.IsNullOrEmpty(sectionId)) throw new ArgumentNullException(nameof(sectionId));

            return HierarchyReader.FindPageBySource(_gateway.ReadSectionListing(sectionId), sourcePath);
        }

        public void Record(string sectionId, string sourcePath, string pageId, string sourceHash)
        {
            // The page carries its own Md2OneNote.Source and Md2OneNote.Hash; the next TryFind
            // reads them from the listing. There is no second copy to keep in step.
        }
    }
}
