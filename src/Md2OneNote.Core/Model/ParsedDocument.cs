using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Marker for a node of the block model. The block hierarchy itself belongs to the Core
    /// rendering work; the orchestration layer treats the body as opaque and only moves it from
    /// pass 1 to pass 3.
    /// </summary>
    public interface IBlock
    {
    }

    public sealed class DocumentModel
    {
        public DocumentModel(IReadOnlyList<IBlock> blocks)
        {
            if (blocks == null) throw new ArgumentNullException(nameof(blocks));

            Blocks = blocks;
        }

        public IReadOnlyList<IBlock> Blocks { get; }

        public static DocumentModel Empty { get; } = new DocumentModel(new IBlock[0]);
    }

    /// <summary>
    /// Output of pass 1. Pure product of the Markdown text: it names what the document needs
    /// (diagrams, assets) without resolving any of it.
    /// </summary>
    public sealed class ParsedDocument
    {
        public ParsedDocument(
            string suggestedTitle,
            DocumentModel body,
            IReadOnlyList<DiagramRequest> diagrams,
            IReadOnlyList<AssetRequest> assets,
            IReadOnlyList<Diagnostic> diagnostics)
        {
            if (body == null) throw new ArgumentNullException(nameof(body));
            if (diagrams == null) throw new ArgumentNullException(nameof(diagrams));
            if (assets == null) throw new ArgumentNullException(nameof(assets));
            if (diagnostics == null) throw new ArgumentNullException(nameof(diagnostics));

            SuggestedTitle = suggestedTitle;
            Body = body;
            Diagrams = diagrams;
            Assets = assets;
            Diagnostics = diagnostics;
        }

        /// <summary>Front matter title, else a leading H1, else null (FR-15).</summary>
        public string SuggestedTitle { get; }

        public DocumentModel Body { get; }

        public IReadOnlyList<DiagramRequest> Diagrams { get; }

        public IReadOnlyList<AssetRequest> Assets { get; }

        public IReadOnlyList<Diagnostic> Diagnostics { get; }
    }
}
