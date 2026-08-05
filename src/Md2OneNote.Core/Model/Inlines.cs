using System;
using System.Collections.Generic;

namespace Md2OneNote.Core
{
    /// <summary>
    /// Marker for a node of the inline model — the content of a single OneNote <c>one:T</c>.
    /// </summary>
    /// <remarks>
    /// The model is deliberately small. It carries only what <see cref="Rendering.InlineWriter"/>
    /// knows how to express inside a <c>one:T</c> fragment (IMPLEMENTATION.md §5.3); anything the
    /// parser cannot express here it lowers to text rather than inventing markup.
    /// </remarks>
    public interface IInline
    {
    }

    /// <summary>Literal text exactly as the author wrote it. Never pre-escaped.</summary>
    public sealed class TextRun : IInline
    {
        public TextRun(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            Text = text;
        }

        public string Text { get; }
    }

    public enum RunStyle
    {
        Bold,
        Italic,
        Strikethrough
    }

    /// <summary>Emphasis wrapping other inlines, so that nesting such as bold-inside-link works.</summary>
    public sealed class StyledRun : IInline
    {
        public StyledRun(RunStyle style, IReadOnlyList<IInline> children)
        {
            if (children == null) throw new ArgumentNullException(nameof(children));

            Style = style;
            Children = children;
        }

        public RunStyle Style { get; }

        public IReadOnlyList<IInline> Children { get; }
    }

    /// <summary>A backtick code span. Its content is literal: no inline markup applies inside.</summary>
    public sealed class CodeRun : IInline
    {
        public CodeRun(string text)
        {
            if (text == null) throw new ArgumentNullException(nameof(text));

            Text = text;
        }

        public string Text { get; }
    }

    /// <summary>
    /// A live hyperlink. Only schemes the renderer is willing to emit reach this type — the parser
    /// lowers everything else to its link text (FR-16, NFR-4).
    /// </summary>
    public sealed class HyperlinkRun : IInline
    {
        public HyperlinkRun(string url, IReadOnlyList<IInline> children)
        {
            if (string.IsNullOrEmpty(url)) throw new ArgumentNullException(nameof(url));
            if (children == null) throw new ArgumentNullException(nameof(children));

            Url = url;
            Children = children;
        }

        public string Url { get; }

        public IReadOnlyList<IInline> Children { get; }
    }

    /// <summary>
    /// A break within a paragraph. A hard break becomes a line break; a soft one becomes a space,
    /// which is what OneNote's own whitespace handling would collapse it to anyway.
    /// </summary>
    public sealed class LineBreakRun : IInline
    {
        private LineBreakRun(bool isHard)
        {
            IsHard = isHard;
        }

        public bool IsHard { get; }

        public static LineBreakRun Hard { get; } = new LineBreakRun(true);

        public static LineBreakRun Soft { get; } = new LineBreakRun(false);
    }

    /// <summary>
    /// A reference to a footnote definition, rendered as a bracketed number. OneNote's superscript
    /// support inside <c>one:T</c> is not established until the Phase 0 dump, and a wrong guess
    /// there costs the whole page; a bracketed number is legible, searchable, and certain.
    /// </summary>
    public sealed class FootnoteReferenceRun : IInline
    {
        public FootnoteReferenceRun(int number)
        {
            if (number <= 0) throw new ArgumentOutOfRangeException(nameof(number));

            Number = number;
        }

        public int Number { get; }
    }
}
