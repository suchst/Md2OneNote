using System;
using System.Runtime.InteropServices;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// The two Office extensibility interfaces, declared here rather than referenced from a
    /// primary interop assembly.
    /// </summary>
    /// <remarks>
    /// The PIAs (<c>Extensibility.dll</c>, <c>office.dll</c>) are not reliably present on a
    /// Click-to-Run install, and requiring them would put a deployment dependency on every machine
    /// the add-in reaches — the same version-pinning problem that made the gateway late-bound
    /// (docs/page-schema-notes.md §10). The IIDs and DISPIDs below are the published ones; they are
    /// a contract with Office, so they are stated explicitly rather than left to the CLR's
    /// declaration-order defaults.
    /// <para>
    /// <b>Both are dual, not dispatch-only.</b> Office invokes them through the vtable, and a
    /// <c>InterfaceIsIDispatch</c> declaration gives the COM-callable wrapper only IDispatch's seven
    /// slots with no vtable entries behind them. Activation and <c>QueryInterface</c> both still
    /// succeed, so the mistake is invisible to every late-bound probe; it surfaces only when Office
    /// calls <c>OnConnection</c> through a slot that is not there, which it reports as "a runtime
    /// error occurred during the loading of the COM Add-in". Declaration order below is therefore
    /// vtable order and must not be rearranged.
    /// </para>
    /// <para>
    /// <b>The calls cross a process boundary.</b> OneNote hosts add-ins in a <c>dllhost.exe</c>
    /// surrogate, so these interfaces are marshalled by the type-library marshaller through
    /// Office's own <c>Interface\{IID}</c> registrations, present on any machine with Office. Same
    /// IIDs, same DISPIDs, same vtable order — that is what makes our declarations interchangeable
    /// with the registered ones.
    /// </para>
    /// <para>
    /// <b>These must not be <c>[ComImport]</c>.</b> That attribute means "this interface is defined
    /// in a type library, load its type information from there" — and no type library defines these
    /// for us. COM activation then fails with <c>TYPE_E_ELEMENTNOTFOUND (0x8002802B)</c> before a
    /// single line of add-in code runs, which OneNote sees as a startup failure and answers by
    /// setting <c>LoadBehavior=2</c>. Declared as ordinary managed interfaces, the CLR generates the
    /// dispatch information from metadata and no type library is involved.
    /// </para>
    /// </remarks>
    /// <remarks>
    /// <b>The parameter marshalling is part of the contract too.</b> The type library declares
    /// <c>IDispatch* Application</c> and <c>SAFEARRAY(VARIANT)* custom</c>, and the stub in the
    /// surrogate passes exactly that. The CLR's default for a bare <c>object</c> parameter is a
    /// <c>VARIANT</c> and for <c>System.Array</c> an interface pointer, so without the
    /// <c>MarshalAs</c> attributes below the CLR reads the incoming <c>IDispatch*</c> as a
    /// <c>VARIANT</c> and fails with <c>COR_E_INVALIDOLEVARIANTTYPE (0x80131531)</c> before the
    /// method body runs — no log line, and OneNote reports a runtime error during loading. The
    /// attributes are copied from the primary interop assembly
    /// (<c>Extensibility.dll 7.0.3300.0</c>), verified 2026-09-14.
    /// </remarks>
    [ComVisible(true)]
    [Guid("B65AD801-ABAF-11D0-BB8B-00A0C90F2744")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IDTExtensibility2
    {
        [DispId(1)]
        void OnConnection(
            [In, MarshalAs(UnmanagedType.IDispatch)] object application,
            [In] int connectMode,
            [In, MarshalAs(UnmanagedType.IDispatch)] object addInInst,
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(2)]
        void OnDisconnection(
            [In] int removeMode,
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(3)]
        void OnAddInsUpdate(
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(4)]
        void OnStartupComplete(
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);

        [DispId(5)]
        void OnBeginShutdown(
            [In, MarshalAs(UnmanagedType.SafeArray, SafeArraySubType = VarEnum.VT_VARIANT)] ref Array custom);
    }

    [ComVisible(true)]
    [Guid("000C0396-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IRibbonExtensibility
    {
        // BSTR in, BSTR out — the CLR default for string, stated explicitly because it is the
        // type library's contract, not ours to pick (office.dll 15.0.0.0).
        [DispId(1)]
        [return: MarshalAs(UnmanagedType.BStr)]
        string GetCustomUI([In, MarshalAs(UnmanagedType.BStr)] string ribbonId);
    }

    /// <summary>
    /// The ribbon callbacks named in <c>Ribbon.xml</c>. Ours, not Office's: the GUID is private to
    /// this add-in.
    /// </summary>
    /// <remarks>
    /// Office invokes ribbon callbacks by <em>name</em>, through <c>IDispatch::GetIDsOfNames</c> on
    /// whatever the add-in object answers for <c>IID_IDispatch</c>. With
    /// <c>ClassInterfaceType.None</c> that is the first COM-visible interface the class implements,
    /// <see cref="IDTExtensibility2"/>, which knows no callback names — every click then fails name
    /// lookup and nothing happens, silently. Declaring this interface and naming it the class's
    /// <c>[ComDefaultInterface]</c> makes it the one <c>IDispatch</c> exposes. The parameter is an
    /// <c>IRibbonControl</c>; it arrives through <c>Invoke</c> as a dispatch pointer.
    /// </remarks>
    [ComVisible(true)]
    [Guid("2F28BB01-9DFD-4C1B-8C80-741F5CD64AE8")]
    [InterfaceType(ComInterfaceType.InterfaceIsDual)]
    public interface IRibbonCallbacks
    {
        [DispId(1)]
        void OnImportClicked([In, MarshalAs(UnmanagedType.IDispatch)] object control);
    }

    /// <summary><c>ext_ConnectMode</c>, for logging which way OneNote loaded us.</summary>
    internal static class ConnectMode
    {
        public const int AfterStartup = 0;
        public const int Startup = 1;
        public const int External = 2;
        public const int CommandLine = 3;

        public static string Describe(int mode)
        {
            switch (mode)
            {
                case AfterStartup: return "after startup";
                case Startup: return "at startup";
                case External: return "external";
                case CommandLine: return "command line";
                default: return "mode " + mode.ToString(System.Globalization.CultureInfo.InvariantCulture);
            }
        }
    }
}
