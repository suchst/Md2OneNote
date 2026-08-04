namespace Md2OneNote.Core
{
    /// <summary>
    /// Every user-visible string resolves through this, including strings emitted into page
    /// content (NFR-11).
    /// </summary>
    public interface IStringCatalog
    {
        /// <summary>
        /// Returns the localized string for a key. Implementations must never return null and
        /// must never throw for an unknown key — a missing translation is not a reason to fail
        /// an import.
        /// </summary>
        string Get(string key);
    }
}
