using System;
using System.Runtime.InteropServices;

namespace Md2OneNote.Interop
{
    /// <summary>
    /// OneNote's <c>IApplication</c>, declared here from the type library inside
    /// <c>ONENOTE.EXE</c> (resource 3, library <c>{0EA692EE-BB50-4E3C-AEF0-356D91732725}</c> 1.1)
    /// rather than referenced from the primary interop assembly.
    /// </summary>
    /// <remarks>
    /// <b>Calls go through the vtable, and only the vtable works.</b> The <c>Application</c> object
    /// OneNote hands an add-in answers <c>E_FAIL</c> to <c>IDispatch::GetTypeInfo</c>. The
    /// <c>dynamic</c> binder asks for type information before its first call and treats that
    /// failure as fatal (<c>ComRuntimeHelpers.GetITypeInfoFromIDispatch</c>), so every
    /// late-bound call fails with <c>E_FAIL</c> before reaching OneNote — which is what
    /// docs/page-schema-notes.md §10 misread as OneNote refusing calls. Reflection's COM path
    /// needs the same type information through <c>LoadRegTypeLib</c>. A vtable call needs neither:
    /// the proxy is built once from the type library and the call lands directly in slot 7 onward.
    /// This is what the interop assembly does, and what OneMore does.
    /// <para>
    /// <b>Declaration order is vtable order</b> and must not be rearranged, reordered, or gapped.
    /// Every method from slot 7 to 35 is declared even though most are never called, because a
    /// missing one shifts every slot after it. Enumerations are declared as <c>int</c> with the
    /// values as named constants; the wire type is the same.
    /// </para>
    /// </remarks>
    [ComImport]
    [Guid("452AC71A-B655-4967-A208-A4CC39DD7949")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IApplication
    {
        // slot 7
        void GetHierarchy(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrStartNodeID,
            [In] int hsScope,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrHierarchyXmlOut,
            [In] int xsSchema);

        // slot 8
        void UpdateHierarchy(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrChangesXmlIn,
            [In] int xsSchema);

        // slot 9
        void OpenHierarchy(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPath,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrRelativeToObjectID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrObjectID,
            [In] int cftIfNotExist);

        // slot 10
        void DeleteHierarchy(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrObjectID,
            [In] DateTime dateExpectedLastModified,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool deletePermanently);

        // slot 11
        void CreateNewPage(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrSectionID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrPageID,
            [In] int npsNewPageStyle);

        // slot 12
        void CloseNotebook(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrNotebookID,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool force);

        // slot 13
        void GetHierarchyParent(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrObjectID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrParentID);

        // slot 14
        void GetPageContent(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPageID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrPageXmlOut,
            [In] int pageInfoToExport,
            [In] int xsSchema);

        // slot 15
        void UpdatePageContent(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPageChangesXmlIn,
            [In] DateTime dateExpectedLastModified,
            [In] int xsSchema,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool force);

        // slot 16
        void GetBinaryPageContent(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPageID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrCallbackID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrBinaryObjectB64Out);

        // slot 17
        void DeletePageContent(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPageID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrObjectID,
            [In] DateTime dateExpectedLastModified,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool force);

        // slot 18
        void NavigateTo(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrHierarchyObjectID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrObjectID,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool fNewWindow);

        // slot 19
        void NavigateToUrl(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrUrl,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool fNewWindow);

        // slot 20
        void Publish(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrHierarchyID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrTargetFilePath,
            [In] int pfPublishFormat,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrCLSIDofExporter);

        // slot 21
        void OpenPackage(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPathPackage,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPathDest,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrPathOut);

        // slot 22
        void GetHyperlinkToObject(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrHierarchyID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPageContentObjectID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrHyperlinkOut);

        // slot 23
        void FindPages(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrStartNodeID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrSearchString,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrHierarchyXmlOut,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool fIncludeUnindexedPages,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool fDisplay,
            [In] int xsSchema);

        // slot 24
        void FindMeta(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrStartNodeID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrSearchStringName,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrHierarchyXmlOut,
            [In, MarshalAs(UnmanagedType.VariantBool)] bool fIncludeUnindexedPages,
            [In] int xsSchema);

        // slot 25
        void GetSpecialLocation(
            [In] int slToGet,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrSpecialLocationPath);

        // slot 26
        void MergeFiles(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrBaseFile,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrClientFile,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrServerFile,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrTargetFile);

        // slot 27
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object QuickFiling();

        // slot 28
        void SyncHierarchy([In, MarshalAs(UnmanagedType.BStr)] string bstrHierarchyID);

        // slot 29
        void SetFilingLocation(
            [In] int flToSet,
            [In] int fltToSet,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrFilingSectionID);

        // slot 30
        IWindows Windows
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            get;
        }

        // slot 31
        bool Dummy1
        {
            [return: MarshalAs(UnmanagedType.VariantBool)]
            get;
        }

        // slot 32
        void MergeSections(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrSectionSourceId,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrSectionDestinationId);

        // slot 33
        object COMAddIns
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }

        // slot 34
        object LanguageSettings
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }

        // slot 35
        void GetWebHyperlinkToObject(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrHierarchyID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrPageContentObjectID,
            [Out, MarshalAs(UnmanagedType.BStr)] out string pbstrHyperlinkOut);
    }

    /// <summary>OneNote's <c>Windows</c> collection. Same source and same rules as <see cref="IApplication"/>.</summary>
    [ComImport]
    [Guid("6D4B9C3E-CC05-493F-85E2-43D1006DF96A")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IWindows
    {
        // slot 7
        [return: MarshalAs(UnmanagedType.Interface)]
        IWindow get_Item([In] uint index);

        // slot 8
        uint Count { get; }

        // slot 9
        [return: MarshalAs(UnmanagedType.IUnknown)]
        object get_NewEnum();

        // slot 10
        IWindow CurrentWindow
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            get;
        }
    }

    /// <summary>OneNote's <c>Window</c>. Same source and same rules as <see cref="IApplication"/>.</summary>
    [ComImport]
    [Guid("8E8304B8-CBD1-44F8-B0E8-89C625B2002E")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IWindow
    {
        // slot 7
        ulong WindowHandle { get; }

        // slot 8
        string CurrentPageId
        {
            [return: MarshalAs(UnmanagedType.BStr)]
            get;
        }

        // slot 9
        string CurrentSectionId
        {
            [return: MarshalAs(UnmanagedType.BStr)]
            get;
        }

        // slot 10
        string CurrentSectionGroupId
        {
            [return: MarshalAs(UnmanagedType.BStr)]
            get;
        }

        // slot 11
        string CurrentNotebookId
        {
            [return: MarshalAs(UnmanagedType.BStr)]
            get;
        }

        // slot 12
        void NavigateTo(
            [In, MarshalAs(UnmanagedType.BStr)] string bstrHierarchyObjectID,
            [In, MarshalAs(UnmanagedType.BStr)] string bstrObjectID);

        // slots 13, 14
        bool FullPageView
        {
            [return: MarshalAs(UnmanagedType.VariantBool)]
            get;
            [param: MarshalAs(UnmanagedType.VariantBool)]
            set;
        }

        // slots 15, 16
        bool Active
        {
            [return: MarshalAs(UnmanagedType.VariantBool)]
            get;
            [param: MarshalAs(UnmanagedType.VariantBool)]
            set;
        }

        // slots 17, 18
        int DockedLocation { get; set; }

        // slot 19
        IApplication Application
        {
            [return: MarshalAs(UnmanagedType.Interface)]
            get;
        }

        // slot 20
        bool SideNote
        {
            [return: MarshalAs(UnmanagedType.VariantBool)]
            get;
        }

        // slot 21
        void NavigateToUrl([In, MarshalAs(UnmanagedType.BStr)] string bstrUrl);

        // slot 22
        void SetDockedLocation([In] int dockLocation, [In] Point ptMonitor);
    }

    /// <summary>The <c>Point</c> struct <c>Window.SetDockedLocation</c> takes; never used here.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct Point
    {
        public int X;
        public int Y;
    }

    /// <summary>Enumeration values from the type library, as the wire integers.</summary>
    public static class OneNoteEnums
    {
        // HierarchyScope
        public const int HierarchyScopeSelf = 0;
        public const int HierarchyScopeChildren = 1;
        public const int HierarchyScopeNotebooks = 2;
        public const int HierarchyScopeSections = 3;
        public const int HierarchyScopePages = 4;

        // PageInfo
        public const int PageInfoBasic = 0;
        public const int PageInfoBinaryData = 1;
        public const int PageInfoSelection = 2;
        public const int PageInfoAll = 7;

        // XMLSchema
        public const int Schema2007 = 0;
        public const int Schema2010 = 1;
        public const int Schema2013 = 2;

        // NewPageStyle
        public const int NewPageStyleDefault = 0;
        public const int NewPageStyleBlankPageWithTitle = 1;
        public const int NewPageStyleBlankPageNoTitle = 2;
    }
}
