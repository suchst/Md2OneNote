using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Inputs to pass 1 that the parser cannot know on its own.
    /// </summary>
    public sealed class ParseOptions
    {
        public ParseOptions(ISet<string> diagramLanguages)
        {
            if (diagramLanguages == null) throw new ArgumentNullException(nameof(diagramLanguages));

            DiagramLanguages = diagramLanguages;
        }

        /// <summary>
        /// Fence languages that should become <see cref="DiagramRequest"/>s rather than code
        /// blocks. Supplied by the composition root from the renderer registry, so the parser
        /// never needs to know which renderers exist. A language absent here is a plain code
        /// block (FR-13).
        /// </summary>
        public ISet<string> DiagramLanguages { get; }

        public static ParseOptions None
        {
            get { return new ParseOptions(new HashSet<string>(StringComparer.OrdinalIgnoreCase)); }
        }
    }
}
