using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    /// <summary>
    /// Pass 2 for images. Like the diagram resolver, every path produces an outcome: a rejected
    /// path or a missing file becomes a placeholder plus a warning, never a silent gap and never
    /// a failed import (FR-10).
    /// </summary>
    public sealed class AssetResolver
    {
        private readonly IAssetSource _source;
        private readonly IStringCatalog _strings;

        public AssetResolver(IAssetSource source, IStringCatalog strings)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            _source = source;
            _strings = strings;
        }

        public IReadOnlyDictionary<string, AssetOutcome> Resolve(
            IReadOnlyList<AssetRequest> requests,
            string baseDirectory,
            AssetPolicy policy,
            IImportProgress progress,
            CancellationToken ct)
        {
            if (requests == null) throw new ArgumentNullException(nameof(requests));
            if (policy == null) throw new ArgumentNullException(nameof(policy));
            if (progress == null) throw new ArgumentNullException(nameof(progress));

            var outcomes = new Dictionary<string, AssetOutcome>(StringComparer.Ordinal);
            if (requests.Count == 0)
            {
                return outcomes;
            }

            progress.Step(_strings.Get(StringKeys.ProgressImages));

            for (var i = 0; i < requests.Count; i++)
            {
                ct.ThrowIfCancellationRequested();

                var request = requests[i];
                if (outcomes.ContainsKey(request.Key))
                {
                    continue;
                }

                outcomes[request.Key] = LoadOne(request, baseDirectory, policy);
            }

            return outcomes;
        }

        private AssetOutcome LoadOne(AssetRequest request, string baseDirectory, AssetPolicy policy)
        {
            try
            {
                var outcome = _source.Load(request, baseDirectory, policy);
                return outcome ?? AssetOutcome.Failure(string.Format(
                    CultureInfo.CurrentCulture,
                    _strings.Get(StringKeys.FailureUnreadable),
                    request.RawPath));
            }
            catch (Exception ex)
            {
                return AssetOutcome.Failure(string.Format(
                    CultureInfo.CurrentCulture,
                    _strings.Get(StringKeys.FailureUnreadable),
                    ex.Message));
            }
        }
    }
}
