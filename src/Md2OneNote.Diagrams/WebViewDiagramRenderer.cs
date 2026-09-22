using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Md2OneNote.Core;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace Md2OneNote.Diagrams
{
    /// <summary>
    /// Renders diagrams and formulas (<see cref="Languages"/>) to PNG in a hidden WebView2
    /// (IMPLEMENTATION.md §8.2, DESIGN.md §8).
    /// </summary>
    /// <remarks>
    /// <b>Everything WebView2 happens on one dedicated STA thread with its own message loop.</b>
    /// The control needs a pumping UI thread, and the thread an import runs on is whatever the
    /// host handed us — an MTA thread inside a COM call, in the add-in's case. Callers may be on
    /// any thread; <see cref="RenderAsync"/> posts to the host thread and waits. Only bytes and
    /// numbers cross back.
    /// <para>
    /// One instance per import, disposed afterwards. Lazily created on first use, reused across
    /// the import's diagrams, thrown away and recreated after a timeout (poisoning), and
    /// abandoned for the rest of the import after repeated faults (degradation) — see
    /// <see cref="RendererHealth"/>. A diagram's own failure is an outcome and never throws.
    /// </para>
    /// </remarks>
    public sealed class WebViewDiagramRenderer : IDiagramRenderer, IDisposable
    {
        /// <summary>
        /// The fence languages the shell renders: Mermaid, Graphviz (<c>dot</c> or
        /// <c>graphviz</c>) and KaTeX (<c>math</c> or <c>katex</c>; a <c>$$</c> block is
        /// <c>math</c> too). The shell's <c>renderDiagram</c> switches on the same names.
        /// </summary>
        public static readonly string[] Languages = { "mermaid", "dot", "graphviz", "math", "katex" };

        private const double CaptureScale = 2.0;
        private const int MaxCaptureEdgePx = 4096;

        private readonly TimeSpan _budget;
        private readonly RendererHealth _health = new RendererHealth();
        private readonly SemaphoreSlim _oneAtATime = new SemaphoreSlim(1, 1);
        private readonly object _gate = new object();
        private readonly Dictionary<string, TaskCompletionSource<RenderReply>> _pending =
            new Dictionary<string, TaskCompletionSource<RenderReply>>(StringComparer.Ordinal);

        private Thread _thread;
        private Form _host;
        private WebView2 _webView;
        private bool _ready;
        private bool _disposed;

        public WebViewDiagramRenderer() : this(TimeSpan.FromSeconds(10))
        {
        }

        public WebViewDiagramRenderer(TimeSpan budgetPerDiagram)
        {
            if (budgetPerDiagram <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(budgetPerDiagram));

            _budget = budgetPerDiagram;
        }

        /// <summary>
        /// Whether the Evergreen WebView2 runtime is installed. Checked once by the composition
        /// root; when it is absent no renderer is registered and the summary says why (NFR-12).
        /// </summary>
        public static string AvailableRuntimeVersion()
        {
            try
            {
                return CoreWebView2Environment.GetAvailableBrowserVersionString();
            }
            catch (WebView2RuntimeNotFoundException)
            {
                return null;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public bool CanRender(string language)
        {
            return !_disposed
                && !_health.Degraded
                && Array.FindIndex(Languages, l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase)) >= 0;
        }

        public async Task<DiagramOutcome> RenderAsync(DiagramRequest request, CancellationToken ct)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (_disposed) return DiagramOutcome.Failure("the diagram renderer has been disposed");
            if (_health.Degraded) return DiagramOutcome.Failure("the diagram renderer is unavailable for the rest of this import");

            await _oneAtATime.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                return await RenderLockedAsync(request, ct).ConfigureAwait(false);
            }
            finally
            {
                _oneAtATime.Release();
            }
        }

        private async Task<DiagramOutcome> RenderLockedAsync(DiagramRequest request, CancellationToken ct)
        {
            try
            {
                if (_health.Poisoned)
                {
                    await OnHostAsync(TearDownWebView).ConfigureAwait(false);
                    _health.InstanceReplaced();
                }

                var ready = await WithBudgetAsync(EnsureReadyAsync(), ct).ConfigureAwait(false);
                if (!ready)
                {
                    return Fault("the diagram renderer did not start within " + Seconds(_budget), true);
                }

                var id = Guid.NewGuid().ToString("N");
                var rendered = await WithBudgetAsync(RenderInShellAsync(id, request), ct).ConfigureAwait(false);
                if (rendered == null)
                {
                    return Fault("rendering exceeded the budget of " + Seconds(_budget), true);
                }

                if (!rendered.Ok)
                {
                    _health.RecordDiagramFailure();
                    return DiagramOutcome.Failure(string.IsNullOrEmpty(rendered.Error) ? "the diagram could not be rendered" : rendered.Error);
                }

                var png = await WithBudgetAsync(CaptureAsync(id, rendered.Width, rendered.Height), ct).ConfigureAwait(false);
                if (png == null)
                {
                    return Fault("capturing the diagram exceeded the budget of " + Seconds(_budget), true);
                }

                var scale = ScaleFor(rendered.Width, rendered.Height);
                var expectedWidth = Math.Max(1, (int)Math.Ceiling(rendered.Width * scale));
                var expectedHeight = Math.Max(1, (int)Math.Ceiling(rendered.Height * scale));

                // A capture of the wrong size is a broken capture, however valid the PNG: the
                // page would show it stretched to the size it was told (PngHeader). Off by one
                // is rounding between the host's client size and the browser's own pixels.
                var actual = PngHeader.Size(png);
                if (actual == null
                    || Math.Abs(actual.Item1 - expectedWidth) > 1
                    || Math.Abs(actual.Item2 - expectedHeight) > 1)
                {
                    return Fault(string.Format(
                        CultureInfo.InvariantCulture,
                        "the capture was {0}, not the {1}x{2} pixels the diagram needs",
                        actual == null ? "not a PNG" : actual.Item1 + "x" + actual.Item2,
                        expectedWidth,
                        expectedHeight), false);
                }

                _health.RecordSuccess();
                return DiagramOutcome.Success(png, actual.Item1, actual.Item2, scale);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                return Fault("the diagram renderer failed: " + ex.Message, false);
            }
        }

        private DiagramOutcome Fault(string message, bool poisons)
        {
            _health.RecordFault(poisons);
            return DiagramOutcome.Failure(message);
        }

        // ---- host thread --------------------------------------------------------------------

        private Task EnsureReadyAsync()
        {
            StartHostThread();
            return OnHostAsync(async () =>
            {
                if (_ready && _webView != null && !_webView.IsDisposed)
                {
                    return;
                }

                var shellFolder = ShellFiles.Ensure();
                var userData = Path.Combine(ShellFiles.RootFolder, "webview2");
                Directory.CreateDirectory(userData);

                var options = new CoreWebView2EnvironmentOptions();
                var environment = await CoreWebView2Environment.CreateAsync(null, userData, options);

                _webView = new WebView2 { Dock = DockStyle.Fill };
                _host.Controls.Add(_webView);
                await _webView.EnsureCoreWebView2Async(environment);

                var core = _webView.CoreWebView2;
                core.Settings.AreDevToolsEnabled = false;
                core.Settings.AreDefaultContextMenusEnabled = false;
                core.Settings.IsStatusBarEnabled = false;
                core.Settings.AreDefaultScriptDialogsEnabled = false;
                core.Settings.IsWebMessageEnabled = true;

                // Layer two of the network isolation (DESIGN.md §8.3): every request that is not
                // for the shell's own virtual host is refused here, whatever the CSP says.
                core.SetVirtualHostNameToFolderMapping(ShellFiles.HostName, shellFolder, CoreWebView2HostResourceAccessKind.Deny);
                core.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All);
                core.WebResourceRequested += OnWebResourceRequested;
                core.WebMessageReceived += OnWebMessageReceived;

                var loaded = new TaskCompletionSource<bool>();
                EventHandler<CoreWebView2NavigationCompletedEventArgs> onCompleted = null;
                onCompleted = (s, e) =>
                {
                    core.NavigationCompleted -= onCompleted;
                    loaded.TrySetResult(e.IsSuccess);
                };
                core.NavigationCompleted += onCompleted;
                core.Navigate(ShellFiles.IndexUri);

                if (!await loaded.Task)
                {
                    throw new InvalidOperationException("the diagram shell failed to load");
                }

                _ready = true;
            });
        }

        private Task<RenderReply> RenderInShellAsync(string id, DiagramRequest request)
        {
            var reply = Expect(id);
            OnHostAsync(() =>
            {
                var script = "renderDiagram(" + JsLiteral.Quote(request.Language.ToLowerInvariant()) + ","
                    + JsLiteral.Quote(request.Source) + "," + JsLiteral.Quote(id) + ")";
                return _webView.ExecuteScriptAsync(script);
            }).ContinueWith(t => { if (t.IsFaulted) Fail(id, t.Exception); }, TaskScheduler.Default);
            return reply;
        }

        private async Task<byte[]> CaptureAsync(string id, int width, int height)
        {
            // The browser's own layout viewport becomes the diagram's box, at capture scale,
            // through the DevTools protocol; the host window stays 800x600 and never matters.
            // It had to be the diagram's size with CapturePreviewAsync, and could not: Windows
            // clamps the first resize of a shown window to the screen, a docked child does not
            // follow the next one, and a tall sequence diagram lost its bottom quarter. A plain
            // captureBeyondViewport clip then grew the capture downwards only, and a wide
            // diagram lost its right half instead (both 2026-09-14). Overriding the device
            // metrics is what Puppeteer does for a full-page shot, and it holds in both axes.
            var scale = ScaleFor(width, height);
            var metrics = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"width\":{0},\"height\":{1},\"deviceScaleFactor\":{2},\"mobile\":false}}",
                width,
                height,
                scale);
            var clip = string.Format(
                CultureInfo.InvariantCulture,
                "{{\"format\":\"png\",\"captureBeyondViewport\":true,\"clip\":{{\"x\":0,\"y\":0,\"width\":{0},\"height\":{1},\"scale\":1}}}}",
                width,
                height);

            var frameId = id + "-frame";
            var frame = Expect(frameId);

            try
            {
                await OnHostAsync(async () =>
                {
                    await _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.setDeviceMetricsOverride", metrics);
                    await _webView.ExecuteScriptAsync("afterFrame(" + JsLiteral.Quote(frameId) + ")");
                }).ConfigureAwait(false);

                await frame.ConfigureAwait(false);

                var json = await OnHostAsync(
                    () => _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Page.captureScreenshot", clip))
                    .ConfigureAwait(false);

                var png = ScreenshotReply.TryDecode(json);
                if (png == null)
                {
                    throw new InvalidOperationException("Page.captureScreenshot returned no image.");
                }

                return png;
            }
            finally
            {
                try
                {
                    await OnHostAsync(
                        () => _webView.CoreWebView2.CallDevToolsProtocolMethodAsync("Emulation.clearDeviceMetricsOverride", "{}"))
                        .ConfigureAwait(false);
                }
                catch (Exception)
                {
                    // The next capture sets its own metrics; a failure to clear costs nothing.
                }
            }
        }

        private static double ScaleFor(int width, int height)
        {
            var longest = Math.Max(width, height);
            return longest * CaptureScale <= MaxCaptureEdgePx
                ? CaptureScale
                : Math.Max(0.25, MaxCaptureEdgePx / (double)longest);
        }

        private void OnWebResourceRequested(object sender, CoreWebView2WebResourceRequestedEventArgs e)
        {
            Uri uri;
            if (Uri.TryCreate(e.Request.Uri, UriKind.Absolute, out uri)
                && uri.Scheme == Uri.UriSchemeHttps
                && string.Equals(uri.Host, ShellFiles.HostName, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            e.Response = _webView.CoreWebView2.Environment.CreateWebResourceResponse(null, 403, "Blocked", string.Empty);
        }

        private void OnWebMessageReceived(object sender, CoreWebView2WebMessageReceivedEventArgs e)
        {
            string text;
            try
            {
                text = e.TryGetWebMessageAsString();
            }
            catch (Exception)
            {
                return;
            }

            var reply = RenderReply.TryParse(text);
            if (reply == null)
            {
                return;
            }

            TaskCompletionSource<RenderReply> waiter;
            lock (_gate)
            {
                if (!_pending.TryGetValue(reply.Id, out waiter))
                {
                    return;
                }

                _pending.Remove(reply.Id);
            }

            waiter.TrySetResult(reply);
        }

        private Task<RenderReply> Expect(string id)
        {
            var tcs = new TaskCompletionSource<RenderReply>(TaskCreationOptions.RunContinuationsAsynchronously);
            lock (_gate)
            {
                _pending[id] = tcs;
            }

            return tcs.Task;
        }

        private void Fail(string id, Exception error)
        {
            TaskCompletionSource<RenderReply> waiter;
            lock (_gate)
            {
                if (!_pending.TryGetValue(id, out waiter))
                {
                    return;
                }

                _pending.Remove(id);
            }

            waiter.TrySetException(error ?? new InvalidOperationException("render failed"));
        }

        /// <summary>
        /// Awaits <paramref name="work"/> for at most the budget. On expiry the work is
        /// abandoned, not cancelled — a script that never returns cannot be stopped (DESIGN.md
        /// §8.2), which is why the caller poisons the instance.
        /// </summary>
        private async Task<T> WithBudgetAsync<T>(Task<T> work, CancellationToken ct)
        {
            var timeout = Task.Delay(_budget, ct);
            var first = await Task.WhenAny(work, timeout).ConfigureAwait(false);
            if (first == work)
            {
                return await work.ConfigureAwait(false);
            }

            ct.ThrowIfCancellationRequested();
            return default(T);
        }

        private async Task<bool> WithBudgetAsync(Task work, CancellationToken ct)
        {
            var timeout = Task.Delay(_budget, ct);
            var first = await Task.WhenAny(work, timeout).ConfigureAwait(false);
            if (first == work)
            {
                await work.ConfigureAwait(false);
                return true;
            }

            ct.ThrowIfCancellationRequested();
            return false;
        }

        private void StartHostThread()
        {
            lock (_gate)
            {
                if (_thread != null)
                {
                    return;
                }

                var started = new ManualResetEventSlim(false);
                _thread = new Thread(() =>
                {
                    // Shown, but off every monitor: WebView2 renders only while its controller
                    // is visible, and a window that is never shown never becomes visible.
                    _host = new Form
                    {
                        FormBorderStyle = FormBorderStyle.None,
                        ShowInTaskbar = false,
                        StartPosition = FormStartPosition.Manual,
                        Location = new Point(-32000, -32000),
                        ClientSize = new Size(800, 600),
                        Text = "Md2OneNote diagram renderer"
                    };
                    _host.Show();
                    started.Set();
                    Application.Run(_host);
                })
                {
                    IsBackground = true,
                    Name = "Md2OneNote diagram host"
                };
                _thread.SetApartmentState(ApartmentState.STA);
                _thread.Start();
                started.Wait();
            }
        }

        private Task OnHostAsync(Func<Task> work)
        {
            var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            _host.BeginInvoke(new Action(async () =>
            {
                try
                {
                    await work();
                    tcs.TrySetResult(true);
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));
            return tcs.Task;
        }

        private Task<T> OnHostAsync<T>(Func<Task<T>> work)
        {
            var tcs = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
            _host.BeginInvoke(new Action(async () =>
            {
                try
                {
                    tcs.TrySetResult(await work());
                }
                catch (Exception ex)
                {
                    tcs.TrySetException(ex);
                }
            }));
            return tcs.Task;
        }

        private Task OnHostAsync(Action work)
        {
            return OnHostAsync(() =>
            {
                work();
                return Task.FromResult(true);
            });
        }

        private void TearDownWebView()
        {
            _ready = false;
            if (_webView == null)
            {
                return;
            }

            try
            {
                if (_webView.CoreWebView2 != null)
                {
                    _webView.CoreWebView2.WebResourceRequested -= OnWebResourceRequested;
                    _webView.CoreWebView2.WebMessageReceived -= OnWebMessageReceived;
                }

                _host.Controls.Remove(_webView);
                _webView.Dispose();
            }
            catch (Exception)
            {
            }
            finally
            {
                _webView = null;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            Thread thread;
            lock (_gate)
            {
                thread = _thread;
            }

            if (thread == null)
            {
                return;
            }

            try
            {
                _host.BeginInvoke(new Action(() =>
                {
                    TearDownWebView();
                    _host.Close();
                    Application.ExitThread();
                }));
                thread.Join(TimeSpan.FromSeconds(5));
            }
            catch (Exception)
            {
            }
        }

        private static string Seconds(TimeSpan span)
        {
            return span.TotalSeconds.ToString("0.#", CultureInfo.InvariantCulture) + " s";
        }
    }
}
