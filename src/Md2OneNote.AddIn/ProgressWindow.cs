using System;
using System.Threading;
using System.Windows.Forms;
using Md2OneNote.AddIn.Logging;
using Md2OneNote.Application;

namespace Md2OneNote.AddIn
{
    /// <summary>
    /// Hosts <see cref="ProgressForm"/> on its own STA thread with a message pump, so it paints
    /// and answers Cancel while the import runs on the <see cref="ImportHost"/> thread. Implements
    /// <see cref="IImportProgress"/> for the import service and owns the
    /// <see cref="CancellationTokenSource"/> (DESIGN.md §10).
    /// </summary>
    /// <remarks>
    /// Every member is safe to call from any thread and never throws: a progress sink that fails
    /// must not fail the import (<see cref="IImportProgress"/> contract).
    /// </remarks>
    internal sealed class ProgressWindow : IImportProgress, IDisposable
    {
        private readonly FileLogger _log;
        private readonly CancellationTokenSource _cancellation = new CancellationTokenSource();
        private readonly ManualResetEventSlim _ready = new ManualResetEventSlim(false);
        private ProgressForm _form;
        private Thread _thread;
        private int _completed;

        public ProgressWindow(FileLogger log)
        {
            _log = log;
        }

        /// <summary>Cancelled when the user clicks Cancel or closes the window, or the host shuts down.</summary>
        public CancellationToken Token
        {
            get { return _cancellation.Token; }
        }

        /// <summary>Cancels as if the user had; the window shows "Cancelling" until the import stops.</summary>
        public void Cancel()
        {
            try
            {
                _cancellation.Cancel();
                Post(f => f.ShowCancelling());
            }
            catch (ObjectDisposedException)
            {
                // Already closed: nothing left to cancel.
            }
        }

        /// <summary>Shows the window, owned by OneNote's window when there is one, and returns once it is up.</summary>
        public void Show(IntPtr owner)
        {
            _thread = new Thread(() =>
            {
                try
                {
                    using (var context = new ApplicationContext())
                    using (var form = new ProgressForm())
                    {
                        _form = form;
                        form.CancelRequested += (s, e) =>
                        {
                            _log.Info("Import cancelled by the user.");
                            _cancellation.Cancel();
                        };
                        form.FormClosed += (s, e) => context.ExitThread();
                        form.HandleCreated += (s, e) => _ready.Set();

                        if (owner == IntPtr.Zero)
                        {
                            form.Show();
                        }
                        else
                        {
                            form.Show(new WindowHandle(owner));
                        }

                        System.Windows.Forms.Application.Run(context);
                    }
                }
                catch (Exception ex)
                {
                    _log.Error("Progress window failed", ex);
                }
                finally
                {
                    _form = null;
                    _ready.Set();
                }
            });

            _thread.SetApartmentState(ApartmentState.STA);
            _thread.IsBackground = true;
            _thread.Start();
            _ready.Wait(TimeSpan.FromSeconds(5));
        }

        public void FileStarted(int index, int total, string path)
        {
            Post(f => f.ShowFileStarted(index, total, path));
        }

        public void Step(string localizedMessage)
        {
            Post(f => f.ShowStep(localizedMessage));
        }

        public void FileFinished(FileResult result)
        {
            var completed = Interlocked.Increment(ref _completed);
            Post(f => f.ShowFileFinished(completed));
        }

        /// <summary>Closes the window and waits for its thread; safe to call more than once.</summary>
        public void Dispose()
        {
            var form = _form;
            if (form != null)
            {
                Post(f =>
                {
                    f.AllowClose = true;
                    f.Close();
                });
            }

            var thread = _thread;
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            {
                thread.Join(TimeSpan.FromSeconds(5));
            }

            _cancellation.Dispose();
            _ready.Dispose();
        }

        private void Post(Action<ProgressForm> action)
        {
            try
            {
                var form = _form;
                if (form == null || !form.IsHandleCreated)
                {
                    return;
                }

                form.BeginInvoke(new Action(() =>
                {
                    if (!form.IsDisposed)
                    {
                        action(form);
                    }
                }));
            }
            catch (Exception ex)
            {
                // Closed under us, or the pump is gone: the import carries on without a window.
                _log.Error("Progress update dropped", ex);
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
    }
}
