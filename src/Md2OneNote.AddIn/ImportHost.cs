using System;
using System.Threading;
using Md2OneNote.AddIn.Logging;
using Md2OneNote.Application;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// Runs one import at a time on a background thread, so the ribbon callback returns at once
    /// and OneNote's window stays live while pages are made (DESIGN.md §4).
    /// </summary>
    /// <remarks>
    /// The thread is MTA, which is the apartment OneNote's <c>Application</c> proxy lives in: the
    /// surrogate activates a <c>ThreadingModel=Both</c> class on an RPC thread, every ribbon
    /// callback arrives on one (the log says so), and diagram continuations have reached the
    /// gateway from thread-pool threads since Phase 4. Within one MTA the proxy is shared, so
    /// nothing is marshalled. The two pieces that need a message pump, the progress window and
    /// the WebView2 renderer, each own an STA thread of their own.
    /// <para>
    /// A second request while one runs is refused, not queued (§4). At shutdown the running import
    /// is asked to stop after its current file and given a moment; the thread is a background
    /// thread, so the surrogate exits regardless.
    /// </para>
    /// </remarks>
    internal sealed class ImportHost
    {
        private readonly FileLogger _log;
        private readonly object _gate = new object();
        private Thread _thread;
        private ProgressWindow _progress;
        private volatile bool _stopping;

        public ImportHost(FileLogger log)
        {
            _log = log;
        }

        /// <summary>True while an import thread is alive.</summary>
        public bool IsRunning
        {
            get
            {
                lock (_gate)
                {
                    return _thread != null && _thread.IsAlive;
                }
            }
        }

        /// <summary>
        /// Starts an import on its own thread and returns at once; false when one is already
        /// running.
        /// </summary>
        /// <param name="owner">OneNote's window, which owns the progress window; zero for none.</param>
        /// <param name="work">
        /// The import itself: given the progress window and the user's Cancel, returns the report.
        /// Runs on the import thread.
        /// </param>
        /// <param name="say">Receives the report, or the error, on the import thread.</param>
        public bool TryStart(IntPtr owner, Func<IImportProgress, CancellationToken, string> work, Action<string> say)
        {
            lock (_gate)
            {
                if (_thread != null && _thread.IsAlive)
                {
                    return false;
                }

                var progress = new ProgressWindow(_log);
                var thread = new Thread(() => Run(owner, work, say, progress));
                thread.SetApartmentState(ApartmentState.MTA);
                thread.IsBackground = true;
                thread.Name = "Md2OneNote import";

                _thread = thread;
                _progress = progress;
                thread.Start();
                return true;
            }
        }

        /// <summary>
        /// Asks a running import to stop after its current file and waits up to
        /// <paramref name="patience"/> for it. Nothing to do when none runs.
        /// </summary>
        public void Shutdown(TimeSpan patience)
        {
            Thread thread;
            ProgressWindow progress;
            lock (_gate)
            {
                thread = _thread;
                progress = _progress;
            }

            if (thread == null || !thread.IsAlive || thread == Thread.CurrentThread)
            {
                return;
            }

            _log.Info("Shutting down while an import runs; cancelling it.");
            _stopping = true;
            if (progress != null)
            {
                progress.Cancel();
            }

            thread.Join(patience);
        }

        private void Run(IntPtr owner, Func<IImportProgress, CancellationToken, string> work, Action<string> say, ProgressWindow progress)
        {
            try
            {
                _log.Info("Import thread " + Thread.CurrentThread.ManagedThreadId
                    + ", " + Thread.CurrentThread.GetApartmentState());

                // The window is closed before the report is shown, so the report is never
                // behind it.
                string report;
                using (progress)
                {
                    progress.Show(owner);
                    report = work(progress, progress.Token);
                }

                // OneNote is gone when the host is stopping: a report box would come from the
                // surrogate with no owner, after the window the user closed. The log has it.
                if (_stopping)
                {
                    _log.Info("Import stopped for shutdown; report not shown: " + report.Replace("\r\n", " | "));
                    return;
                }

                say(report);
            }
            catch (Exception ex)
            {
                _log.Error("Import failed", ex);

                try
                {
                    say(Connect.ErrorText(ex));
                }
                catch (Exception)
                {
                }
            }
            finally
            {
                lock (_gate)
                {
                    _progress = null;
                }
            }
        }
    }
}
