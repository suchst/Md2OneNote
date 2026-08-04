using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    /// <summary>
    /// Pass 2 for diagrams: turns requests into outcomes by consulting a registry of renderers
    /// (DESIGN.md §3).
    /// </summary>
    /// <remarks>
    /// Every path here produces an outcome. A missing renderer, a renderer that throws, and a
    /// document over the diagram limit all resolve to a failure outcome rather than an exception,
    /// because pass 3 renders failures as a code block plus the message and the import must
    /// always complete (FR-13, FR-14).
    /// </remarks>
    public sealed class DiagramResolver
    {
        private readonly IReadOnlyList<IDiagramRenderer> _renderers;
        private readonly IStringCatalog _strings;

        public DiagramResolver(IReadOnlyList<IDiagramRenderer> renderers, IStringCatalog strings)
        {
            if (renderers == null) throw new ArgumentNullException(nameof(renderers));
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            _renderers = renderers;
            _strings = strings;
        }

        public async Task<IReadOnlyDictionary<string, DiagramOutcome>> ResolveAsync(
            IReadOnlyList<DiagramRequest> requests,
            int maxDiagrams,
            IImportProgress progress,
            CancellationToken ct)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (progress == null) throw new ArgumentNullException(nameof(progress));

            var outcomes = new Dictionary<string, DiagramOutcome>(StringComparer.Ordinal);
            if (requests.Count == 0)
            {
                return outcomes;
            }

            progress.Step(_strings.Get(StringKeys.ProgressDiagrams));

            var rendered = 0;
            for (var i = 0; i < requests.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var request = requests[i];

                // Deduplication (NFR-20). Equal language and source share a key, so a document
                // that repeats a diagram pays for it once.
                if (outcomes.ContainsKey(request.Key))
                {
                    continue;
                }

                if (rendered >= maxDiagrams)
                {
                    outcomes[request.Key] = DiagramOutcome.Failure(string.Format(
                        CultureInfo.CurrentCulture,
                        _strings.Get(StringKeys.DiagramLimitExceeded),
                        maxDiagrams));
                    continue;
                }

                outcomes[request.Key] = await RenderOneAsync(request, ct).ConfigureAwait(true);
                rendered++;
            }

            return outcomes;
        }

        private async Task<DiagramOutcome> RenderOneAsync(DiagramRequest request, CancellationToken ct)
        {
            var renderer = FindRenderer(request.Language);
            if (renderer == null)
            {
                // Either no renderer was registered for this language, or the subsystem has
                // degraded — WebView2 missing, or too many consecutive failures (NFR-12).
                return DiagramOutcome.Failure(string.Format(
                    CultureInfo.CurrentCulture,
                    _strings.Get(StringKeys.DiagramNoRenderer),
                    request.Language));
            }

            try
            {
                var outcome = await renderer.RenderAsync(request, ct).ConfigureAwait(true);
                return outcome ?? DiagramOutcome.Failure(string.Format(
                    CultureInfo.CurrentCulture,
                    _strings.Get(StringKeys.DiagramRendererFaulted),
                    "renderer returned no result"));
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A renderer is contractually not supposed to throw. If one does it is a bug in
                // that renderer, not a reason to lose the rest of the document.
                return DiagramOutcome.Failure(string.Format(
                    CultureInfo.CurrentCulture,
                    _strings.Get(StringKeys.DiagramRendererFaulted),
                    ex.Message));
            }
        }

        private IDiagramRenderer FindRenderer(string language)
        {
            for (var i = 0; i < _renderers.Count; i++)
            {
                var renderer = _renderers[i];
                try
                {
                    if (renderer.CanRender(language))
                    {
                        return renderer;
                    }
                }
                catch (Exception)
                {
                    // A renderer that cannot answer whether it can render is treated as unable to.
                }
            }

            return null;
        }
    }
}
