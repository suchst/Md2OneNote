namespace Md2OneNote.Core
{
    /// <summary>
    /// Reads the bytes of a local file. Deliberately the entire filesystem surface of asset
    /// loading: everything else about whether an image may be embedded is decided in
    /// <see cref="AssetLoader"/>, which keeps those rules pure and testable (NFR-21).
    /// </summary>
    public interface IFileBytes
    {
        /// <summary>
        /// Reads the file at <paramref name="fullPath"/>, which is always an absolute path that
        /// has already passed <see cref="AssetPathPolicy"/>.
        /// </summary>
        /// <param name="limitBytes">
        /// The most the caller is willing to accept. Implementations must stop reading once they
        /// have <paramref name="limitBytes"/> + 1 bytes and return that much, so a file of
        /// arbitrary size is never buffered whole just to be rejected. Returning the extra byte is
        /// what lets the caller tell "exactly at the limit" from "over it".
        /// </param>
        /// <returns>The bytes read, or null when no such file exists.</returns>
        /// <remarks>
        /// May throw for a file that exists but cannot be read — locked, denied, or on a device
        /// that has gone away. The caller turns that into a placeholder, so implementations should
        /// not swallow it into a null and lose the reason.
        /// </remarks>
        byte[] Read(string fullPath, long limitBytes);
    }
}
