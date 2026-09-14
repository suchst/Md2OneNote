using System;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Threading;

namespace Md2OneNote.SchemaSpike
{
    /// <summary>
    /// OneNote reached through IDispatch, with no interop assembly and no type library.
    /// </summary>
    /// <remarks>
    /// Late binding is not laziness. Md2OneNote.Interop has to work against whatever OneNote build
    /// the user has, and shipping a version-pinned interop assembly makes that a deployment
    /// problem. Proving the whole surface we need can be driven late-bound is therefore one of the
    /// answers Phase 0 is meant to produce, so the spike is written the way the real gateway would
    /// have to be.
    /// <para>
    /// <b>Binding goes through <c>dynamic</c>, never <c>Type.InvokeMember</c>.</b> This is a Phase 0
    /// finding, not a style preference. Reflection's COM path calls <c>LoadRegTypeLib</c>, and a
    /// stock Office x64 Click-to-Run install registers the OneNote type library under
    /// <c>TypeLib\{0EA692EE-…}\1.1\0\Win32</c> only, pointing at a resource inside the 64-bit
    /// <c>ONENOTE.EXE</c>. There is no <c>Win64</c> key, so a 64-bit client finds no registration,
    /// and a 32-bit client cannot load a type library out of a 64-bit image. Both bitnesses fail
    /// with <c>TYPE_E_LIBNOTREGISTERED (0x8002801D)</c> on the first member access.
    /// </para>
    /// <para>
    /// The <c>dynamic</c> binder resolves members with <c>IDispatch::GetIDsOfNames</c> and never
    /// touches the type library, so it is unaffected. Md2OneNote.Interop must bind the same way.
    /// </para>
    /// <para>
    /// The enum values below are copied from the OneNote 15.0 type library. Nothing here trusts
    /// them: the spike calls with each schema value in turn and reports which namespace came back,
    /// which is how they get confirmed rather than assumed.
    /// </para>
    /// </remarks>
    internal sealed class OneNoteApplication
    {
        public const int HierarchyScopeSelf = 0;
        public const int HierarchyScopeChildren = 1;
        public const int HierarchyScopeNotebooks = 2;
        public const int HierarchyScopeSections = 3;
        public const int HierarchyScopePages = 4;

        public const int PageInfoBasic = 0;
        public const int PageInfoBinaryData = 1;
        public const int PageInfoSelection = 2;
        public const int PageInfoBinaryDataSelection = 3;
        public const int PageInfoAll = 4;

        public const int Schema2007 = 0;
        public const int Schema2010 = 1;
        public const int Schema2013 = 2;

        public const int NewPageStyleDefault = 0;
        public const int NewPageStyleBlankPageWithTitle = 1;
        public const int NewPageStyleBlankPageNoTitle = 2;

        // OneNote returns these while it is mid-operation. They are not failures; they are "ask
        // again". A gateway that does not handle them fails at random under normal use.
        private const int RpcCallRejected = unchecked((int)0x80010001);
        private const int RpcServerCallRetryLater = unchecked((int)0x8001010A);
        private const int MaxAttempts = 8;

        private const int ServerExecFailure = unchecked((int)0x80080005);

        private readonly dynamic _application;

        private OneNoteApplication(object application)
        {
            _application = application;
        }

        public static OneNoteApplication Connect()
        {
            var type = Type.GetTypeFromProgID("OneNote.Application", false);
            if (type == null)
            {
                throw new InvalidOperationException(
                    "The ProgID 'OneNote.Application' is not registered on this machine. The COM " +
                    "API belongs to OneNote desktop (2016 / Microsoft 365); the Store app of the " +
                    "same name does not expose it.");
            }

            try
            {
                return new OneNoteApplication(Activator.CreateInstance(type));
            }
            catch (COMException ex)
            {
                if (ex.HResult == ServerExecFailure && IsElevated())
                {
                    throw new InvalidOperationException(
                        "OneNote will not attach to an elevated process. COM refuses to hand a " +
                        "medium-integrity server to a high-integrity client, and it cannot start " +
                        "a second elevated OneNote either, so activation fails with " +
                        "CO_E_SERVER_EXEC_FAILURE.\r\n\r\n" +
                        "Run this from an ordinary, non-administrator shell.", ex);
                }

                throw new InvalidOperationException(
                    "OneNote is registered but would not start: " + ex.Message, ex);
            }
        }

        /// <summary>The notebook, section and page currently on screen, or nulls if none is.</summary>
        public WindowState CurrentWindow()
        {
            dynamic window;
            try
            {
                window = _application.Windows.CurrentWindow;
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "OneNote has no open window to read a current page from: " + ex.Message, ex);
            }

            return new WindowState(
                (string)window.CurrentNotebookId,
                (string)window.CurrentSectionId,
                (string)window.CurrentPageId);
        }

        public string GetHierarchy(string startNodeId, int scope, int schema)
        {
            string xml = null;
            Retry("GetHierarchy", () => _application.GetHierarchy(startNodeId, scope, out xml, schema));
            return xml;
        }

        public string GetPageContent(string pageId, int pageInfo, int schema)
        {
            string xml = null;
            Retry("GetPageContent", () => _application.GetPageContent(pageId, out xml, pageInfo, schema));
            return xml;
        }

        public string CreateNewPage(string sectionId, int pageStyle)
        {
            string pageId = null;
            Retry("CreateNewPage", () => _application.CreateNewPage(sectionId, out pageId, pageStyle));
            return pageId;
        }

        public void UpdatePageContent(string pageXml, int schema)
        {
            // DateTime.MinValue means "do not check whether the page changed under me". The real
            // gateway will pass it too: it only ever writes pages it has just created.
            Retry("UpdatePageContent",
                () => _application.UpdatePageContent(pageXml, DateTime.MinValue, schema, false));
        }

        /// <summary>
        /// Runs a late-bound call, retrying the "ask again" HRESULTs OneNote raises while it is
        /// mid-operation. Unlike the reflection path, a dynamic call surfaces the
        /// <see cref="COMException"/> directly rather than wrapping it.
        /// </summary>
        private void Retry(string name, Action call)
        {
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    call();
                    return;
                }
                catch (COMException ex)
                {
                    if (IsBusy(ex) && attempt < MaxAttempts)
                    {
                        Console.Error.WriteLine(
                            "  OneNote is busy; retrying {0} ({1}/{2})", name, attempt, MaxAttempts);
                        Thread.Sleep(150 * attempt);
                        continue;
                    }

                    throw;
                }
            }
        }

        private static bool IsElevated()
        {
            using (var identity = WindowsIdentity.GetCurrent())
            {
                return new WindowsPrincipal(identity).IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        private static bool IsBusy(Exception exception)
        {
            var com = exception as COMException;
            return com != null
                && (com.HResult == RpcServerCallRetryLater || com.HResult == RpcCallRejected);
        }

        internal sealed class WindowState
        {
            public WindowState(string notebookId, string sectionId, string pageId)
            {
                NotebookId = notebookId;
                SectionId = sectionId;
                PageId = pageId;
            }

            public string NotebookId { get; }

            public string SectionId { get; }

            public string PageId { get; }
        }
    }
}
