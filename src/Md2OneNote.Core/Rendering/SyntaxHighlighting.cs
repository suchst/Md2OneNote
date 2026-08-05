using System;
using System.Collections.Generic;

namespace Md2OneNote.Core.Rendering
{
    /// <summary>A run of code sharing one colour. A null colour means the default text colour.</summary>
    public sealed class CodeToken
    {
        public CodeToken(string text, string color)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            Text = text;
            Color = color;
        }

        public string Text { get; }

        /// <summary>An <c>#RRGGBB</c> value, or null for unstyled text.</summary>
        public string Color { get; }
    }

    /// <summary>
    /// Splits source code into coloured runs (FR-9). Implementations are pure, and must return
    /// tokens whose text concatenates back to exactly the input — the code block writer relies on
    /// that to keep line breaks and indentation intact.
    /// </summary>
    public interface ISyntaxHighlighter
    {
        /// <param name="language">The fence info string. Unknown languages are not an error (FR-13).</param>
        IReadOnlyList<CodeToken> Highlight(string language, string code);
    }

    /// <summary>
    /// Highlights nothing. Used where output has to be independent of a third-party tokenizer —
    /// golden-file tests above all, since a ColorCode upgrade would otherwise rewrite every
    /// expected file.
    /// </summary>
    public sealed class PlainSyntaxHighlighter : ISyntaxHighlighter
    {
        public static PlainSyntaxHighlighter Instance { get; } = new PlainSyntaxHighlighter();

        public IReadOnlyList<CodeToken> Highlight(string language, string code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));

            return new[] { new CodeToken(code, null) };
        }
    }
}
