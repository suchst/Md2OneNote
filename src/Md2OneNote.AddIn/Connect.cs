using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows.Forms;
using Md2OneNote.AddIn.Diagnostics;
using Md2OneNote.AddIn.Logging;
using Md2OneNote.Core;
using Md2OneNote.Interop;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// The COM add-in itself. OneNote instantiates this class, hands it the <c>Application</c>
    /// object, and asks it for ribbon markup.
    /// </summary>
    /// <remarks>
    /// <b>Every entry point is wrapped.</b> An exception escaping into OneNote costs the user more
    /// than the failed operation did: OneNote resets <c>LoadBehavior</c> to 2 and adds the add-in to
    /// <c>Resiliency\DisabledItems</c>, after which it never loads again and cannot repair itself
    /// (NFR-2, NFR-3). Nothing here is allowed to throw, including the logging.
    /// </remarks>
    [ComVisible(true)]
    [Guid("04185F61-8636-4BF0-BCB3-941EE717E8C8")]
    [ProgId("Md2OneNote.AddIn")]
    [ClassInterface(ClassInterfaceType.None)]
    // The interface IDispatch exposes, and therefore the one Office resolves ribbon callback names
    // against (see IRibbonCallbacks). OneNote reaches the other two by QueryInterface.
    [ComDefaultInterface(typeof(IRibbonCallbacks))]
    public sealed class Connect : IDTExtensibility2, IRibbonExtensibility, IRibbonCallbacks
    {
        private readonly FileLogger _log = FileLogger.Default();
        private readonly ImportHost _imports;
        private object _application;
        private OneNoteGateway _gateway;

        private const string AlreadyRunning =
            "An import is already running. Wait for it to finish, or cancel it in its progress window.";

        /// <summary>
        /// Runs before the constructor and before any method is JIT-compiled, which is the only
        /// window in which the assembly resolver can be installed usefully.
        /// </summary>
        /// <remarks>
        /// It also marks the earliest point our code can record. When OneNote reports "a runtime
        /// error occurred during the loading of the COM Add-in", whether this line reaches the log
        /// says whether the failure was before our code ran or inside it — the two have nothing in
        /// common as far as diagnosis goes.
        /// <para>
        /// The line names the host process. OneNote loads add-ins out of process, in a
        /// <c>dllhost.exe</c> COM surrogate; a line that says <c>ONENOTE</c>, or a probe's shell,
        /// is a load that did not come from OneNote.
        /// </para>
        /// </remarks>
        static Connect()
        {
            try
            {
                AssemblyResolver.Install();
                FileLogger.Default().Info("Type initialized; assembly resolver installed; " + Host());
            }
            catch (Exception)
            {
                // A throwing static constructor makes the type permanently unusable in this
                // AppDomain, which is a far worse outcome than losing a log line.
            }
        }

        public Connect()
        {
            _imports = new ImportHost(_log);
            _log.Info("Constructed.");
        }

        public string GetCustomUI(string ribbonId)
        {
            try
            {
                _log.Info("GetCustomUI(" + ribbonId + ")");
                return LoadRibbonXml();
            }
            catch (Exception ex)
            {
                _log.Error("GetCustomUI failed", ex);

                // Returning empty markup costs the ribbon button. Throwing costs the add-in.
                return string.Empty;
            }
        }

        public void OnConnection(object application, int connectMode, object addInInst, ref Array custom)
        {
            try
            {
                _application = application;
                _gateway = new OneNoteGateway(application);
                _log.Info("Connected " + ConnectMode.Describe(connectMode) + ", version " + Version());
            }
            catch (Exception ex)
            {
                _log.Error("OnConnection failed", ex);
            }
        }

        public void OnDisconnection(int removeMode, ref Array custom)
        {
            try
            {
                StopImport();
                _gateway = null;
                _application = null;
                _log.Info("Disconnected");
            }
            catch (Exception ex)
            {
                _log.Error("OnDisconnection failed", ex);
            }
        }

        public void OnAddInsUpdate(ref Array custom)
        {
        }

        public void OnStartupComplete(ref Array custom)
        {
        }

        public void OnBeginShutdown(ref Array custom)
        {
            try
            {
                StopImport();
            }
            catch (Exception ex)
            {
                _log.Error("OnBeginShutdown failed", ex);
            }
        }

        /// <summary>
        /// OneNote is going away: a running import stops after its current file, and gets ten
        /// seconds to do so before OneNote's shutdown continues without it.
        /// </summary>
        private void StopImport()
        {
            _imports.Shutdown(TimeSpan.FromSeconds(10));
        }

        // ---- ribbon callbacks (IRibbonCallbacks) --------------------------------------------
        // Signature is (object control): the parameter is an IRibbonControl, which lives in the
        // Office PIA we deliberately do not reference. Office invokes these by name over IDispatch,
        // so the static type of the parameter is never checked.

        public System.Runtime.InteropServices.ComTypes.IStream LoadImage(string imageId)
        {
            try
            {
                var stream = RibbonImages.Load(imageId);
                if (stream == null)
                {
                    _log.Error("Ribbon asked for an image that is not embedded: " + imageId, null);
                }
                else
                {
                    _log.Info("LoadImage(" + imageId + ")");
                }

                return stream;
            }
            catch (Exception ex)
            {
                // A missing icon is a blank button; an exception here is a broken ribbon.
                _log.Error("LoadImage(" + imageId + ") failed", ex);
                return null;
            }
        }

        public void OnImportClicked(object control)
        {
            Guard("Import", () =>
            {
                if (_gateway == null)
                {
                    Say("The add-in loaded but never received OneNote's Application object.");
                    return;
                }

                // Checked before the picker so nobody chooses files for nothing; TryStart checks
                // again, atomically, for the click that lands in between.
                if (_imports.IsRunning)
                {
                    Say(AlreadyRunning);
                    return;
                }

                var owner = OneNoteWindow();
                var paths = PickMarkdownFiles(owner);
                if (paths.Length == 0)
                {
                    return;
                }

                // From here on the import runs on its own thread and this callback returns, which
                // is what keeps OneNote usable meanwhile (DESIGN.md §4). The report and the
                // re-import question come from that thread, owned by OneNote's window as before.
                var gateway = _gateway;
                var version = Version();
                var started = _imports.TryStart(
                    owner,
                    (progress, cancellation) => ImportComposition.Run(gateway, paths, version, _log, AskReimport, progress, cancellation),
                    Say);

                if (!started)
                {
                    Say(AlreadyRunning);
                }
            });
        }

        public void OnAboutClicked(object control)
        {
            Guard("About", () =>
            {
                var owner = OneNoteWindow();
                var report = EnvironmentReport.Collect();
                var logPath = _log.FilePath;

                OnStaThread(() =>
                {
                    using (var form = new AboutForm(report, logPath))
                    {
                        if (owner == IntPtr.Zero)
                        {
                            form.ShowDialog();
                        }
                        else
                        {
                            form.ShowDialog(new WindowHandle(owner));
                        }
                    }
                });
            });
        }

        /// <summary>
        /// Shows the file picker on its own STA thread, owned by OneNote's window.
        /// </summary>
        private static string[] PickMarkdownFiles(IntPtr owner)
        {
            var picked = new string[0];

            OnStaThread(() =>
            {
                using (var dialog = new OpenFileDialog())
                {
                    dialog.Title = "Import Markdown into the current section";
                    dialog.Filter = "Markdown files (*.md;*.markdown)|*.md;*.markdown|All files (*.*)|*.*";
                    dialog.Multiselect = true;
                    dialog.CheckFileExists = true;

                    var result = owner == IntPtr.Zero
                        ? dialog.ShowDialog()
                        : dialog.ShowDialog(new WindowHandle(owner));

                    if (result == DialogResult.OK)
                    {
                        picked = dialog.FileNames;
                    }
                }
            });

            return picked;
        }

        /// <summary>
        /// Runs UI on a dedicated STA thread and waits for it.
        /// </summary>
        /// <remarks>
        /// Not on the callback thread: that thread is inside an incoming COM call from OneNote,
        /// which is blocked until we return, and a modal dialog opened there with no owner
        /// appeared nowhere while OneNote sat frozen behind it (2026-09-14). A dedicated STA
        /// thread has its own message loop and no re-entrancy to worry about, and owning the
        /// dialog by OneNote's window puts it in front of OneNote where the user is looking.
        /// Only plain data crosses back; the COM proxy never leaves the callback thread.
        /// </remarks>
        private static void OnStaThread(Action action)
        {
            Exception failure = null;

            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception ex)
                {
                    failure = ex;
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (failure != null)
            {
                throw new InvalidOperationException(failure.Message, failure);
            }
        }

        /// <summary>OneNote's current window, for owning dialogs; zero when it cannot be read.</summary>
        private IntPtr OneNoteWindow()
        {
            try
            {
                var application = _application as IApplication;
                if (application == null)
                {
                    return IntPtr.Zero;
                }

                return new IntPtr((long)application.Windows.CurrentWindow.WindowHandle);
            }
            catch (Exception ex)
            {
                _log.Error("Could not read OneNote's window handle; dialogs will be unowned", ex);
                return IntPtr.Zero;
            }
        }

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle)
            {
                Handle = handle;
            }

            public IntPtr Handle { get; }
        }

        /// <summary>
        /// Runs a ribbon callback so that no failure reaches OneNote, and the user is told what
        /// happened rather than left with a button that silently does nothing.
        /// </summary>
        private void Guard(string name, Action action)
        {
            try
            {
                _log.Info("Ribbon: " + name + " (thread " + Thread.CurrentThread.ManagedThreadId
                    + ", " + Thread.CurrentThread.GetApartmentState() + ")");
                action();
            }
            catch (Exception ex)
            {
                _log.Error("Ribbon " + name + " failed", ex);

                try
                {
                    Say(ErrorText(ex));
                }
                catch (Exception)
                {
                }
            }
        }

        /// <summary>What the user reads when something failed: the message and where the details are.</summary>
        internal static string ErrorText(Exception ex)
        {
            return "Md2OneNote hit an error:\r\n\r\n" + ex.Message
                + "\r\n\r\nDetails are in %LOCALAPPDATA%\\Md2OneNote\\log.txt";
        }

        /// <summary>
        /// The one question an import asks (FR-19 versus "I want it again"). Asked after the
        /// cheap first pass, so a routine import of unchanged files still costs one click.
        /// </summary>
        private bool AskReimport(int count)
        {
            var message = count == 1
                ? "1 file is unchanged since its last import into this section.\r\n\r\nImport it again as a new page?"
                : count + " files are unchanged since their last import into this section.\r\n\r\nImport them again as new pages?";

            var owner = OneNoteWindow();
            var answer = owner == IntPtr.Zero
                ? MessageBox.Show(message, "Md2OneNote", MessageBoxButtons.YesNo, MessageBoxIcon.Question)
                : MessageBox.Show(new WindowHandle(owner), message, "Md2OneNote", MessageBoxButtons.YesNo, MessageBoxIcon.Question);

            return answer == DialogResult.Yes;
        }

        private void Say(string message)
        {
            var owner = OneNoteWindow();
            if (owner == IntPtr.Zero)
            {
                MessageBox.Show(message, "Md2OneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(new WindowHandle(owner), message, "Md2OneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private static string LoadRibbonXml()
        {
            var assembly = Assembly.GetExecutingAssembly();
            var name = typeof(Connect).Namespace + ".Ribbon.xml";

            using (var stream = assembly.GetManifestResourceStream(name))
            {
                if (stream == null)
                {
                    throw new InvalidOperationException("Embedded resource not found: " + name);
                }

                using (var reader = new StreamReader(stream))
                {
                    return reader.ReadToEnd();
                }
            }
        }

        private static string Version()
        {
            return Assembly.GetExecutingAssembly().GetName().Version.ToString();
        }

        private static string Host()
        {
            try
            {
                using (var process = Process.GetCurrentProcess())
                {
                    return "host=" + process.ProcessName + " pid=" + process.Id.ToString(CultureInfo.InvariantCulture);
                }
            }
            catch (Exception)
            {
                return "host=unknown";
            }
        }
    }
}
