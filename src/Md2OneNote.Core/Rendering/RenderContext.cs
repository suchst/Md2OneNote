using System;
using System.Collections.Generic;

namespace Md2OneNote.Core.Rendering
{
    /// <summary>
    /// The cross-cutting state of a single render: the resolved outcomes, the paragraph style in
    /// force, and the number-sequence allocator (DESIGN.md §6.2).
    /// </summary>
    internal sealed class RenderContext
    {
        /// <summary>
        /// Matches the parser's limit. The renderer's input normally comes from the parser and is
        /// already bounded, but this is a public entry point that can be handed a hand-built model,
        /// and a stack overflow here would take the host process with it (NFR-2, NFR-8).
        /// </summary>
        public const int MaxNestingDepth = 64;

        private readonly ISyntaxHighlighter _highlighter;
        private int _listDepth;
        private int _depth;
        private int _paragraphStyle = StyleTable.Body;

        public RenderContext(RenderInputs inputs, ISyntaxHighlighter highlighter)
        {
            Inputs = inputs;
            _highlighter = highlighter;
        }

        public RenderInputs Inputs { get; }

        /// <summary>The style body text takes here — <c>p</c> normally, something else inside a quote.</summary>
        public int ParagraphStyle
        {
            get { return _paragraphStyle; }
        }

        /// <summary>
        /// How deep in nested lists the writer currently is, 1-based inside a list.
        /// </summary>
        /// <remarks>
        /// This replaces the per-instance number allocator DESIGN.md §6.2 called for. OneNote keys
        /// both the numbering style and the bullet glyph on depth, not on list identity
        /// (docs/page-schema-notes.md §4), so depth is the only thing a writer needs to know.
        /// </remarks>
        public int ListDepth
        {
            get { return _listDepth; }
        }

        public IDisposable EnterList()
        {
            _listDepth++;
            return new ListScope(this);
        }

        /// <summary>Takes one level of the nesting budget. Deeper content is not written.</summary>
        public bool Descend()
        {
            if (_depth >= MaxNestingDepth)
            {
                return false;
            }

            _depth++;
            return true;
        }

        public void Ascend()
        {
            _depth--;
        }

        public IDisposable EnterStyle(int quickStyleIndex)
        {
            var scope = new StyleScope(this, _paragraphStyle);
            _paragraphStyle = quickStyleIndex;
            return scope;
        }

        /// <summary>
        /// Highlights the code and cuts it into lines, since OneNote needs one <c>one:OE</c> per
        /// line. Tokens that straddle a newline — a block comment, say — are split rather than
        /// dropped, which is why this cannot be done before highlighting.
        /// </summary>
        public IReadOnlyList<IReadOnlyList<CodeToken>> HighlightLines(string language, string code)
        {
            var lines = new List<IReadOnlyList<CodeToken>>();
            var current = new List<CodeToken>();

            IReadOnlyList<CodeToken> tokens;
            try
            {
                tokens = _highlighter.Highlight(language, code ?? string.Empty);
            }
            catch (Exception)
            {
                tokens = PlainSyntaxHighlighter.Instance.Highlight(language, code ?? string.Empty);
            }

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var start = 0;
                var text = token.Text;

                for (var j = 0; j < text.Length; j++)
                {
                    if (text[j] != '\n')
                    {
                        continue;
                    }

                    var end = j > start && text[j - 1] == '\r' ? j - 1 : j;
                    Add(current, text.Substring(start, end - start), token.Color);
                    lines.Add(current);
                    current = new List<CodeToken>();
                    start = j + 1;
                }

                Add(current, text.Substring(start), token.Color);
            }

            // A fence's content ends with a newline, which would otherwise show as a blank last
            // line in every code block.
            if (current.Count > 0 || lines.Count == 0)
            {
                lines.Add(current);
            }

            return lines;
        }

        private static void Add(List<CodeToken> line, string text, string color)
        {
            if (text.Length > 0)
            {
                line.Add(new CodeToken(text, color));
            }
        }

        private sealed class ListScope : IDisposable
        {
            private readonly RenderContext _context;

            public ListScope(RenderContext context)
            {
                _context = context;
            }

            public void Dispose()
            {
                _context._listDepth--;
            }
        }

        private sealed class StyleScope : IDisposable
        {
            private readonly RenderContext _context;
            private readonly int _previous;

            public StyleScope(RenderContext context, int previous)
            {
                _context = context;
                _previous = previous;
            }

            public void Dispose()
            {
                _context._paragraphStyle = _previous;
            }
        }
    }
}
