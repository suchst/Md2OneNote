using System;
using System.Globalization;
using System.Reflection;
using System.Runtime.ExceptionServices;
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

        private readonly object _application;

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
            object window;
            try
            {
                window = Property(Property(_application, "Windows"), "CurrentWindow");
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException(
                    "OneNote has no open window to read a current page from: " + ex.Message, ex);
            }

            return new WindowState(
                Property(window, "CurrentNotebookId") as string,
                Property(window, "CurrentSectionId") as string,
                Property(window, "CurrentPageId") as string);
        }

        public string GetHierarchy(string startNodeId, int scope, int schema)
        {
            var args = new object[] { startNodeId, scope, null, schema };
            Invoke("GetHierarchy", args, 2);
            return (string)args[2];
        }

        public string GetPageContent(string pageId, int pageInfo, int schema)
        {
            var args = new object[] { pageId, null, pageInfo, schema };
            Invoke("GetPageContent", args, 1);
            return (string)args[1];
        }

        public string CreateNewPage(string sectionId, int pageStyle)
        {
            var args = new object[] { sectionId, null, pageStyle };
            Invoke("CreateNewPage", args, 1);
            return (string)args[1];
        }

        public void UpdatePageContent(string pageXml, int schema)
        {
            // DateTime.MinValue means "do not check whether the page changed under me". The real
            // gateway will pass it too: it only ever writes pages it has just created.
            Invoke("UpdatePageContent", new object[] { pageXml, DateTime.MinValue, schema, false }, -1);
        }

        private void Invoke(string name, object[] args, int byRefIndex)
        {
            ParameterModifier[] modifiers = null;
            if (byRefIndex >= 0)
            {
                // Without this the out parameter is marshalled by value and comes back null,
                // which looks exactly like OneNote returning nothing.
                var modifier = new ParameterModifier(args.Length);
                modifier[byRefIndex] = true;
                modifiers = new[] { modifier };
            }

            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    _application.GetType().InvokeMember(
                        name,
                        BindingFlags.InvokeMethod,
                        null,
                        _application,
                        args,
                        modifiers,
                        CultureInfo.InvariantCulture,
                        null);
                    return;
                }
                catch (TargetInvocationException ex)
                {
                    if (IsBusy(ex.InnerException) && attempt < MaxAttempts)
                    {
                        Console.Error.WriteLine(
                            "  OneNote is busy; retrying {0} ({1}/{2})", name, attempt, MaxAttempts);
                        Thread.Sleep(150 * attempt);
                        continue;
                    }

                    ExceptionDispatchInfo.Capture(ex.InnerException ?? ex).Throw();
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

        private static object Property(object target, string name)
        {
            return target.GetType().InvokeMember(name, BindingFlags.GetProperty, null, target, null);
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
