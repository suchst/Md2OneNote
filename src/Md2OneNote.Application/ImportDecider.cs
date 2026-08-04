using System;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    public enum ImportDecision
    {
        /// <summary>Nothing was imported from this file into this section before.</summary>
        Create,

        /// <summary>An earlier import exists and the file has changed since.</summary>
        CreateSuperseding,

        /// <summary>An earlier import exists and the file is byte-identical to it.</summary>
        Skip
    }

    /// <summary>
    /// The re-import decision (FR-19). Pure and total, and deliberately tiny: this is the rule
    /// that keeps the product from destroying user work, so it is worth being able to read the
    /// whole of it at once.
    /// </summary>
    /// <remarks>
    /// Note that no branch produces "overwrite". FR-21 leaves the product no way to modify an
    /// existing page during an import, so a changed file can only ever result in a new one.
    /// </remarks>
    public static class ImportDecider
    {
        public static ImportDecision Decide(PageMatch match, string sourceHash)
        {
            if (match == null) throw new ArgumentNullException(nameof(match));
            if (string.IsNullOrEmpty(sourceHash)) throw new ArgumentNullException(nameof(sourceHash));

            if (!match.Found)
            {
                return ImportDecision.Create;
            }

            return string.Equals(match.SourceHash, sourceHash, StringComparison.Ordinal)
                ? ImportDecision.Skip
                : ImportDecision.CreateSuperseding;
        }
    }
}
