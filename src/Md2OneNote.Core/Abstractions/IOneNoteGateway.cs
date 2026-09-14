using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    public sealed class SectionRef
    {
        public SectionRef(string id, string name)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Id = id;
            Name = name ?? string.Empty;
        }

        public string Id { get; }

        public string Name { get; }
    }

    public sealed class PageRef
    {
        public PageRef(string id, string title)
        {
            if (string.IsNullOrEmpty(id)) throw new ArgumentNullException(nameof(id));

            Id = id;
            Title = title ?? string.Empty;
        }

        public string Id { get; }

        public string Title { get; }
    }

    /// <summary>
    /// The boundary to OneNote. Callers never see a COMException: the implementation translates
    /// and applies the retry policy for RPC_E_SERVERCALL_RETRYLATER (DESIGN.md §10).
    /// </summary>
    /// <remarks>
    /// Note what is absent: there is no operation that modifies a page this gateway did not just
    /// create. FR-21 is enforced by the shape of this interface rather than by discipline, so
    /// adding an overwrite path requires deliberately changing the contract.
    /// </remarks>
    public interface IOneNoteGateway
    {
        /// <summary>Throws <see cref="NoActiveSectionException"/> when nothing is being viewed.</summary>
        SectionRef GetActiveSection();

        IReadOnlyList<PageRef> ListPages(string sectionId);

        string GetPageXml(string pageId);

        /// <summary>Creates a blank page and returns its id.</summary>
        string CreatePage(string sectionId);

        /// <summary>
        /// Writes the complete page in one transaction (NFR-1). The implementation stamps
        /// <paramref name="pageId"/> onto the root element, so rendered XML never has to know
        /// which page it will become.
        /// </summary>
        void ReplacePageContent(string pageId, string pageXml);

        /// <summary>
        /// Removes a page this gateway created in the current import whose
        /// <see cref="ReplacePageContent"/> failed. OneNote validates page XML only on write,
        /// so a rejected page necessarily exists by the time it is rejected; deleting it is
        /// what keeps NFR-1 ("a failure leaves no page behind") true. The page goes to the
        /// notebook's recycle bin, not to oblivion. This is the one deletion in the product and
        /// it applies only to a page no user has been able to touch (FR-21).
        /// </summary>
        void DeletePage(string pageId);

        void NavigateTo(string pageId);
    }

    public class OneNoteException : Exception
    {
        public OneNoteException(string message) : base(message)
        {
        }

        public OneNoteException(string message, Exception inner) : base(message, inner)
        {
        }
    }

    /// <summary>No section is currently being viewed, so there is nowhere to put a page (FR-6).</summary>
    public sealed class NoActiveSectionException : OneNoteException
    {
        public NoActiveSectionException(string message) : base(message)
        {
        }
    }

    /// <summary>OneNote stayed busy across every retry attempt.</summary>
    public sealed class OneNoteBusyException : OneNoteException
    {
        public OneNoteBusyException(string message, Exception inner) : base(message, inner)
        {
        }
    }
}
