using System;
using System.Globalization;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Turns a referenced image path into an <see cref="AssetOutcome"/>: applies the path policy,
    /// reads the bytes, enforces the size limit, and identifies the content.
    /// </summary>
    /// <remarks>
    /// This is the whole of DESIGN.md §9 in one place, and it is total — every reference produces
    /// an outcome. A refusal, a missing file, an oversized file and a file that is not an image all
    /// become a placeholder plus a message (FR-10); none of them is an exception and none of them
    /// costs the user the import.
    /// <para>
    /// The order matters. The path is judged before anything is opened, so a refused reference
    /// never touches the disk at all — that is what makes the UNC refusal an actual defence rather
    /// than a report after the handshake already happened.
    /// </para>
    /// </remarks>
    public sealed class AssetLoader : IAssetSource
    {
        private readonly IFileBytes _files;
        private readonly AssetPathPolicy _paths;
        private readonly IStringCatalog _strings;

        public AssetLoader(IFileBytes files, IStringCatalog strings)
            : this(files, new AssetPathPolicy(strings), strings)
        {
        }

        public AssetLoader(IFileBytes files, AssetPathPolicy paths, IStringCatalog strings)
        {
            if (files == null) throw new ArgumentNullException(nameof(files));
            if (paths == null) throw new ArgumentNullException(nameof(paths));
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            _files = files;
            _paths = paths;
            _strings = strings;
        }

        public AssetOutcome Load(AssetRequest request, string baseDirectory, AssetPolicy policy)
        {
            if (request == null) throw new ArgumentNullException(nameof(request));
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            var decision = _paths.Evaluate(request.RawPath, baseDirectory, policy);
            if (!decision.IsAllowed)
            {
                return AssetOutcome.Failure(decision.Message ?? Format(
                    CoreStringKeys.AssetInvalidPath, request.RawPath));
            }

            byte[] bytes;
            try
            {
                bytes = _files.Read(decision.FullPath, policy.MaxBytes);
            }
            catch (Exception ex)
            {
                // A file can exist and still refuse to be read: locked by another process, denied,
                // or on a drive that has just been unplugged. The reason is worth showing.
                return AssetOutcome.Failure(Format(
                    CoreStringKeys.AssetUnreadable, request.RawPath, ex.Message));
            }

            if (bytes == null)
            {
                return AssetOutcome.Failure(Format(CoreStringKeys.AssetNotFound, request.RawPath));
            }

            if (bytes.LongLength > policy.MaxBytes)
            {
                return AssetOutcome.Failure(Format(
                    CoreStringKeys.AssetTooLarge, request.RawPath, policy.MaxBytes));
            }

            var info = ImageSignature.Detect(bytes);
            if (info == null)
            {
                return AssetOutcome.Failure(Format(CoreStringKeys.AssetNotAnImage, request.RawPath));
            }

            // The signature wins over the extension. A .png that is really a JPEG is a mislabelled
            // image, not an attack, so it embeds — under the format its bytes actually are, which
            // is the one OneNote has to be told. The reverse case, a .png that is not an image at
            // all, was already refused above.
            return AssetOutcome.Success(bytes, info.Format, info.WidthPx, info.HeightPx, decision.Message);
        }

        private string Format(string key, params object[] arguments)
        {
            return string.Format(CultureInfo.CurrentCulture, _strings.Get(key), arguments);
        }
    }
}
