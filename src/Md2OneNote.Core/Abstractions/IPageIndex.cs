using System;

namespace Md2OneNote.Core
{
    /// <summary>
    /// The result of looking for a previously imported page.
    /// </summary>
    public sealed class PageMatch
    {
        private PageMatch(bool found, string pageId, string sourceHash)
        {
            Found = found;
            PageId = pageId;
            SourceHash = sourceHash;
        }

        public bool Found { get; }

        public string PageId { get; }

        /// <summary>The hash recorded on the matched page, used to decide skip versus supersede.</summary>
        public string SourceHash { get; }

        public static PageMatch None { get; } = new PageMatch(false, null, null);

        public static PageMatch Hit(string pageId, string sourceHash)
        {
            if (string.IsNullOrEmpty(pageId)) throw new ArgumentNullException(nameof(pageId));
            if (string.IsNullOrEmpty(sourceHash)) throw new ArgumentNullException(nameof(sourceHash));

            return new PageMatch(true, pageId, sourceHash);
        }
    }

    /// <summary>
    /// Finds pages previously created from a given source file, scoped to one section (FR-18).
    /// </summary>
    /// <remarks>
    /// Implementations are a cache, never an authority: a hit must be confirmed against the live
    /// page before it is trusted, and an unconfirmable hit must degrade to <see cref="PageMatch.None"/>.
    /// The failure mode of a stale index is therefore a duplicate page, never a lost one
    /// (DESIGN.md §7.3).
    /// </remarks>
    public interface IPageIndex
    {
        PageMatch TryFind(string sectionId, string sourcePath);

        void Record(string sectionId, string sourcePath, string pageId, string sourceHash);
    }
}
