using System;
using System.Globalization;
using System.IO;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Stable, non-localized reasons a referenced image was refused or questioned. Tests and logs
    /// use these; the user sees <see cref="AssetPathDecision.Message"/>.
    /// </summary>
    public static class AssetPathCodes
    {
        public const string InvalidPath = "Asset.InvalidPath";
        public const string RemoteUrl = "Asset.RemoteUrl";
        public const string AbsolutePath = "Asset.AbsolutePath";
        public const string NetworkPath = "Asset.NetworkPath";
        public const string UnsupportedType = "Asset.UnsupportedType";

        /// <summary>Allowed, but the file sits outside the document's folder.</summary>
        public const string OutsideDocument = "Asset.OutsideDocument";
    }

    /// <summary>
    /// The result of applying <see cref="AssetPathPolicy"/> to one referenced path.
    /// </summary>
    public sealed class AssetPathDecision
    {
        private AssetPathDecision(bool allowed, string fullPath, string code, string message)
        {
            IsAllowed = allowed;
            FullPath = fullPath;
            Code = code;
            Message = message;
        }

        public bool IsAllowed { get; }

        /// <summary>The resolved absolute path, or null when the reference was refused.</summary>
        public string FullPath { get; }

        /// <summary>The reason, or null when the path was allowed without comment.</summary>
        public string Code { get; }

        /// <summary>Localized text for the user, or null when there is nothing to say.</summary>
        public string Message { get; }

        public static AssetPathDecision Allow(string fullPath)
        {
            return new AssetPathDecision(true, fullPath, null, null);
        }

        public static AssetPathDecision AllowWithWarning(string fullPath, string code, string message)
        {
            return new AssetPathDecision(true, fullPath, code, message);
        }

        public static AssetPathDecision Reject(string code, string message)
        {
            return new AssetPathDecision(false, null, code, message);
        }
    }

    /// <summary>
    /// Decides whether a path written in a Markdown file may be read and embedded. Pure and
    /// table-testable, and the one place path decisions are made (DESIGN.md §9).
    /// </summary>
    /// <remarks>
    /// The threat is that a document the user did not write causes an arbitrary local file to be
    /// embedded into a page they may later share (NFR-7), or causes a fetch that authenticates to
    /// a host the author chose. So:
    /// <list type="bullet">
    /// <item>a web address is refused, because embedding one would break the offline guarantee
    /// (NFR-5) as well as leaking that the document was opened;</item>
    /// <item>a UNC path is refused unconditionally — reading it hands an attacker-controlled host
    /// an NTLM handshake, and no setting turns that back on;</item>
    /// <item>an absolute path is refused, because a document referencing <c>C:\Users\…</c> was not
    /// written for this machine;</item>
    /// <item><c>..</c> that escapes the document's folder is allowed with a warning, because a
    /// shared-image folder is a legitimate and common layout, and the genuinely dangerous forms
    /// are already covered.</item>
    /// </list>
    /// Every refusal is an outcome the renderer turns into a visible placeholder, never a silent
    /// omission and never an exception (FR-10).
    /// </remarks>
    public sealed class AssetPathPolicy
    {
        private readonly IStringCatalog _strings;

        public AssetPathPolicy(IStringCatalog strings)
        {
            if (strings == null) throw new ArgumentNullException(nameof(strings));

            _strings = strings;
        }

        public AssetPathDecision Evaluate(string rawPath, string baseDirectory, AssetPolicy policy)
        {
            if (policy == null) throw new ArgumentNullException(nameof(policy));

            if (string.IsNullOrWhiteSpace(rawPath))
            {
                return Reject(AssetPathCodes.InvalidPath, CoreStringKeys.AssetInvalidPath, rawPath);
            }

            if (LooksRemote(rawPath))
            {
                return Reject(AssetPathCodes.RemoteUrl, CoreStringKeys.AssetRemoteUrl, rawPath);
            }

            // Tested before anything normalizes the separators away: \\host\share and //host/share
            // are the same request, and both hand credentials to whoever answers.
            if (IsNetworkPath(rawPath))
            {
                return Reject(AssetPathCodes.NetworkPath, CoreStringKeys.AssetNetworkPath, rawPath);
            }

            // Percent-escapes are decoded before the path is judged, not after, so that a traversal
            // written as %2e%2e%2f is subject to exactly the same rules as one written plainly.
            var candidate = Decode(rawPath);
            if (string.IsNullOrWhiteSpace(candidate) || IsNetworkPath(candidate))
            {
                return Reject(AssetPathCodes.NetworkPath, CoreStringKeys.AssetNetworkPath, rawPath);
            }

            if (IsRooted(candidate))
            {
                return Reject(AssetPathCodes.AbsolutePath, CoreStringKeys.AssetAbsolutePath, rawPath);
            }

            string baseFull;
            string resolved;
            if (!TryResolve(candidate, baseDirectory, out baseFull, out resolved))
            {
                return Reject(AssetPathCodes.InvalidPath, CoreStringKeys.AssetInvalidPath, rawPath);
            }

            var extension = ExtensionOf(resolved);
            if (extension.Length == 0 || !policy.AllowedExtensions.Contains(extension))
            {
                return Reject(AssetPathCodes.UnsupportedType, CoreStringKeys.AssetUnsupportedType, rawPath);
            }

            if (IsUnder(baseFull, resolved))
            {
                return AssetPathDecision.Allow(resolved);
            }

            if (!policy.AllowParentEscape)
            {
                return Reject(AssetPathCodes.OutsideDocument, CoreStringKeys.AssetOutsideDocument, rawPath);
            }

            return AssetPathDecision.AllowWithWarning(
                resolved,
                AssetPathCodes.OutsideDocument,
                Format(CoreStringKeys.AssetOutsideDocument, rawPath));
        }

        /// <summary>
        /// True for anything with a URI scheme. <c>data:</c> is included: an inline payload is not
        /// a file, and accepting one would put unbounded attacker-chosen bytes into the page.
        /// </summary>
        private static bool LooksRemote(string path)
        {
            var trimmed = path.Trim();
            var colon = trimmed.IndexOf(':');
            if (colon <= 1)
            {
                // No scheme, or a drive letter — which IsRooted deals with.
                return false;
            }

            for (var i = 0; i < colon; i++)
            {
                var c = trimmed[i];
                if (!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
                {
                    return false;
                }
            }

            return true;
        }

        private static bool IsNetworkPath(string path)
        {
            var trimmed = path.TrimStart();
            if (trimmed.Length < 2)
            {
                return false;
            }

            var first = trimmed[0];
            var second = trimmed[1];
            var firstIsSeparator = first == '\\' || first == '/';
            var secondIsSeparator = second == '\\' || second == '/';

            // Covers \\server\share, //server/share, and the \\?\ and \\.\ device prefixes.
            return firstIsSeparator && secondIsSeparator;
        }

        private static bool IsRooted(string path)
        {
            try
            {
                if (Path.IsPathRooted(path))
                {
                    return true;
                }
            }
            catch (ArgumentException)
            {
                // An unusable path is not rooted; TryResolve refuses it a moment later.
                return false;
            }

            // "C:image.png" is drive-relative: it resolves against that drive's current directory,
            // which is not this document's folder.
            return path.Length >= 2 && path[1] == ':';
        }

        private static bool TryResolve(string candidate, string baseDirectory, out string baseFull, out string resolved)
        {
            baseFull = null;
            resolved = null;

            if (string.IsNullOrWhiteSpace(baseDirectory) || !Path.IsPathRooted(baseDirectory))
            {
                // Without an absolute base the result would depend on the process's current
                // directory, which would make this function neither pure nor predictable.
                return false;
            }

            try
            {
                baseFull = Path.GetFullPath(baseDirectory);
                resolved = Path.GetFullPath(Path.Combine(baseFull, candidate));
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
            catch (NotSupportedException)
            {
                return false;
            }
            catch (PathTooLongException)
            {
                return false;
            }
        }

        private static string ExtensionOf(string path)
        {
            try
            {
                return Path.GetExtension(path) ?? string.Empty;
            }
            catch (ArgumentException)
            {
                return string.Empty;
            }
        }

        private static bool IsUnder(string baseFull, string resolved)
        {
            var prefix = baseFull.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            return resolved.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }

        private static string Decode(string path)
        {
            if (path.IndexOf('%') < 0)
            {
                return path;
            }

            try
            {
                return Uri.UnescapeDataString(path);
            }
            catch (UriFormatException)
            {
                return path;
            }
        }

        private AssetPathDecision Reject(string code, string key, string rawPath)
        {
            return AssetPathDecision.Reject(code, Format(key, rawPath));
        }

        private string Format(string key, string rawPath)
        {
            return string.Format(CultureInfo.CurrentCulture, _strings.Get(key), rawPath);
        }
    }
}
