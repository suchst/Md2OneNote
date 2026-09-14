using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using System.Xml.Linq;
using Md2OneNote.Core;

namespace Md2OneNote.Interop
{
    /// <summary>
    /// The only class in the product that talks to OneNote. Implements <see cref="IOneNoteGateway"/>
    /// over the COM <c>Application</c> object, applying the retry policy and translating every
    /// COM failure into a Core exception (DESIGN.md §10).
    /// </summary>
    /// <remarks>
    /// <b>Binding is early, through the <see cref="IApplication"/> vtable.</b> Neither late-bound
    /// route works against OneNote's <c>Application</c> object: it answers <c>E_FAIL</c> to
    /// <c>IDispatch::GetTypeInfo</c>, which the <c>dynamic</c> binder requires before its first
    /// call, and reflection's COM path needs the same type information via <c>LoadRegTypeLib</c>.
    /// Both fail before OneNote is ever asked anything. See <see cref="IApplication"/> for the
    /// full account; it replaces an earlier reading that blamed OneNote for refusing the calls.
    /// <para>
    /// The <c>Application</c> object is supplied by the caller — OneNote hands it to the add-in in
    /// <c>OnConnection</c>. This class never activates OneNote itself (FR-21 is enforced by shape:
    /// no operation here can reach a page the gateway did not create).
    /// </para>
    /// </remarks>
    public sealed class OneNoteGateway : IOneNoteGateway
    {
        private const int HierarchyScopePages = OneNoteEnums.HierarchyScopePages;
        private const int PageInfoBasic = OneNoteEnums.PageInfoBasic;
        private const int Schema2013 = OneNoteEnums.Schema2013;
        private const int NewPageStyleBlankPageWithTitle = OneNoteEnums.NewPageStyleBlankPageWithTitle;

        // "Ask again", not failure. OneNote raises these whenever it is mid-operation, and a
        // gateway that does not absorb them fails at random under ordinary use (DESIGN.md §10).
        private const int RpcServerCallRetryLater = unchecked((int)0x8001010A);
        private const int RpcCallRejected = unchecked((int)0x80010001);

        private const int MaxAttempts = 3;
        private const int BackoffMilliseconds = 300;

        private readonly IApplication _application;

        /// <param name="application">
        /// OneNote's <c>Application</c> object, as handed to the add-in by the host.
        /// </param>
        public OneNoteGateway(object application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));

            // The cast is a QueryInterface for IApplication. Across the surrogate boundary that
            // builds the type-library proxy, so a broken type library registration surfaces here,
            // once, as InvalidCastException — not later as a mysterious failed call.
            _application = application as IApplication;
            if (_application == null)
            {
                throw new OneNoteException(
                    "The object OneNote supplied does not expose IApplication; the OneNote type "
                    + "library may not be registered for this process.");
            }
        }

        public SectionRef GetActiveSection()
        {
            var section = HierarchyReader.FindActiveSection(GetHierarchy(null));
            if (section == null)
            {
                throw new NoActiveSectionException(
                    "No OneNote section is currently being viewed, so there is nowhere to create pages.");
            }

            return section;
        }

        public IReadOnlyList<PageRef> ListPages(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId)) throw new ArgumentNullException(nameof(sectionId));

            return HierarchyReader.ReadPages(GetHierarchy(sectionId));
        }

        public string GetPageXml(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) throw new ArgumentNullException(nameof(pageId));

            string xml = null;
            Call("GetPageContent",
                () => _application.GetPageContent(pageId, out xml, PageInfoBasic, Schema2013));
            return xml;
        }

        public string CreatePage(string sectionId)
        {
            if (string.IsNullOrEmpty(sectionId)) throw new ArgumentNullException(nameof(sectionId));

            string pageId = null;
            Call("CreateNewPage",
                () => _application.CreateNewPage(sectionId, out pageId, NewPageStyleBlankPageWithTitle));

            if (string.IsNullOrEmpty(pageId))
            {
                throw new OneNoteException("OneNote created a page but returned no page id.");
            }

            return pageId;
        }

        public void ReplacePageContent(string pageId, string pageXml)
        {
            if (string.IsNullOrEmpty(pageId)) throw new ArgumentNullException(nameof(pageId));
            if (pageXml == null) throw new ArgumentNullException(nameof(pageXml));

            var stamped = StampPageId(pageId, pageXml);

            // DateTime.MinValue skips the concurrency check and force overwrites. Both are safe
            // here and only here: this page was created moments ago by CreatePage and no user has
            // been able to reach it, which is the only situation the gateway can write at all
            // (FR-21). There is deliberately no operation that writes a page it did not create.
            Call("UpdatePageContent",
                () => _application.UpdatePageContent(stamped, DateTime.MinValue, Schema2013, true));
        }

        public void NavigateTo(string pageId)
        {
            if (string.IsNullOrEmpty(pageId)) throw new ArgumentNullException(nameof(pageId));

            Call("NavigateTo", () => _application.NavigateTo(pageId, string.Empty, false));
        }

        private string GetHierarchy(string startNodeId)
        {
            string xml = null;
            Call("GetHierarchy",
                () => _application.GetHierarchy(startNodeId, HierarchyScopePages, out xml, Schema2013));
            return xml;
        }

        /// <summary>
        /// Puts the created page's id on the root element. The renderer produces page XML without
        /// knowing which page it will become, so this is where the two are joined.
        /// </summary>
        private static string StampPageId(string pageId, string pageXml)
        {
            XDocument document;
            try
            {
                document = XDocument.Parse(pageXml);
            }
            catch (System.Xml.XmlException ex)
            {
                throw new OneNoteException(
                    "The rendered page was not well-formed XML and cannot be submitted.", ex);
            }

            document.Root.SetAttributeValue("ID", pageId);
            return document.ToString(SaveOptions.DisableFormatting);
        }

        /// <summary>
        /// Runs one call, absorbing the "busy" HRESULTs and translating everything else.
        /// Callers never see a <see cref="COMException"/>.
        /// </summary>
        private static void Call(string operation, Action call)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    call();
                    return;
                }
                catch (COMException ex) when (IsBusy(ex) && attempt < MaxAttempts)
                {
                    Thread.Sleep(BackoffMilliseconds);
                }
                catch (COMException ex) when (IsBusy(ex))
                {
                    throw new OneNoteBusyException(
                        Describe(operation, "OneNote stayed busy across every attempt"), ex);
                }
                catch (COMException ex)
                {
                    throw new OneNoteException(Describe(operation, ex.Message), ex);
                }
            }
        }

        private static bool IsBusy(COMException exception)
        {
            return exception.HResult == RpcServerCallRetryLater
                || exception.HResult == RpcCallRejected;
        }

        private static string Describe(string operation, string detail)
        {
            return string.Format(CultureInfo.InvariantCulture, "OneNote {0} failed: {1}", operation, detail);
        }
    }
}
