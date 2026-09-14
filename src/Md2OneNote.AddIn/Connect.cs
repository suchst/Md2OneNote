using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
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
        private object _application;
        private OneNoteGateway _gateway;

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
        }

        // ---- ribbon callbacks (IRibbonCallbacks) --------------------------------------------
        // Signature is (object control): the parameter is an IRibbonControl, which lives in the
        // Office PIA we deliberately do not reference. Office invokes these by name over IDispatch,
        // so the static type of the parameter is never checked.

        public void OnImportClicked(object control)
        {
            Guard("Import", () =>
            {
                // Phase 2 wires ImportService in here. Deliberately not yet: the pipeline should
                // not be connected to a notebook until the shell is proven to load.
                Say("Import is not wired up yet.\r\n\r\nThe conversion pipeline is complete and "
                    + "tested; this button is the last connection and lands in Phase 2.");
            });
        }

        public void OnActiveSectionClicked(object control)
        {
            Guard("ActiveSection", () =>
            {
                if (_gateway == null)
                {
                    Say("The add-in loaded but never received OneNote's Application object.");
                    return;
                }

                try
                {
                    var section = _gateway.GetActiveSection();
                    Say("Active section: " + section.Name + "\r\n\r\nId: " + section.Id);
                }
                catch (NoActiveSectionException)
                {
                    // FR-6 in miniature: say so plainly rather than guessing a fallback.
                    Say("No section is currently being viewed, so an import would have nowhere "
                        + "to put its pages.");
                }
            });
        }

        public void OnDumpXmlClicked(object control)
        {
            Guard("DumpXml", () =>
            {
                var path = Diagnostics.DumpCurrentPage(_application);
                Say(path == null
                    ? "No page is open, so there was nothing to dump."
                    : "Wrote the current page's XML to:\r\n\r\n" + path);
            });
        }

        /// <summary>
        /// Runs a ribbon callback so that no failure reaches OneNote, and the user is told what
        /// happened rather than left with a button that silently does nothing.
        /// </summary>
        private void Guard(string name, Action action)
        {
            try
            {
                _log.Info("Ribbon: " + name);
                action();
            }
            catch (Exception ex)
            {
                _log.Error("Ribbon " + name + " failed", ex);

                try
                {
                    Say("Md2OneNote hit an error:\r\n\r\n" + ex.Message
                        + "\r\n\r\nDetails are in %LOCALAPPDATA%\\Md2OneNote\\log.txt");
                }
                catch (Exception)
                {
                }
            }
        }

        private static void Say(string message)
        {
            MessageBox.Show(message, "Md2OneNote", MessageBoxButtons.OK, MessageBoxIcon.Information);
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
