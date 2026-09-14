using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Md2OneNote.Core.Rendering
{
    /// <summary>
    /// Builds the small HTML fragment that goes inside a <c>one:T</c>, keeping markup we emit and
    /// text the user wrote strictly apart.
    /// </summary>
    /// <remarks>
    /// The CDATA/escaping interaction is the most error-prone surface in the project
    /// (IMPLEMENTATION.md §13), so it gets exactly one implementation (DESIGN.md §6.3). Text is
    /// escaped on the way in and markup is appended verbatim; the finished fragment is dropped into
    /// CDATA with no second pass. Because <c>&gt;</c> is escaped along with <c>&amp;</c> and
    /// <c>&lt;</c>, a <c>]]&gt;</c> the author typed cannot close the CDATA section early.
    /// </remarks>
    internal sealed class FragmentBuilder
    {
        private readonly StringBuilder _builder = new StringBuilder();

        /// <summary>Appends text the user wrote. Escaped here and nowhere else.</summary>
        public void Text(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return;
            }

            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                switch (c)
                {
                    case '&':
                        _builder.Append("&amp;");
                        break;
                    case '<':
                        _builder.Append("&lt;");
                        break;
                    case '>':
                        _builder.Append("&gt;");
                        break;
                    default:
                        if (XmlText.IsAllowed(c))
                        {
                            _builder.Append(c);
                        }

                        break;
                }
            }
        }

        /// <summary>Appends markup this assembly generated. Never user input.</summary>
        public void Markup(string markup)
        {
            _builder.Append(markup);
        }

        /// <summary>
        /// Appends non-breaking spaces. Whitespace collapses inside <c>one:T</c>, so indentation
        /// only survives as <c>&amp;nbsp;</c> (IMPLEMENTATION.md §5.3).
        /// </summary>
        public void NonBreakingSpaces(int count)
        {
            for (var i = 0; i < count; i++)
            {
                _builder.Append("&nbsp;");
            }
        }

        public override string ToString()
        {
            return _builder.ToString();
        }
    }

    /// <summary>
    /// Renders the inline model into <c>one:T</c> fragments. No other component may construct
    /// <c>one:T</c> content (DESIGN.md §6.3).
    /// </summary>
    internal static class InlineWriter
    {
        private const string BoldOpen = "<span style='font-weight:bold'>";
        private const string ItalicOpen = "<span style='font-style:italic'>";
        private const string StrikeOpen = "<span style='text-decoration:line-through'>";
        private const string CodeOpen = "<span style='font-family:Consolas;background-color:#F2F2F2'>";
        private const string SuperscriptOpen = "<span style='vertical-align:super'>";
        private const string SpanClose = "</span>";

        /// <summary>
        /// Matches the parser's nesting budget, for the same reason: this is reachable from a
        /// public API with a hand-built model, and overflowing the stack would take the host
        /// process with it (NFR-2, NFR-8).
        /// </summary>
        private const int MaxDepth = 64;

        public static string Write(IReadOnlyList<IInline> inlines)
        {
            var fragment = new FragmentBuilder();
            Write(inlines, fragment, 0);
            return fragment.ToString();
        }

        private static void Write(IReadOnlyList<IInline> inlines, FragmentBuilder fragment, int depth)
        {
            if (depth > MaxDepth)
            {
                return;
            }

            for (var i = 0; i < inlines.Count; i++)
            {
                Write(inlines[i], fragment, depth);
            }
        }

        private static void Write(IInline inline, FragmentBuilder fragment, int depth)
        {
            var text = inline as TextRun;
            if (text != null)
            {
                fragment.Text(text.Text);
                return;
            }

            var styled = inline as StyledRun;
            if (styled != null)
            {
                fragment.Markup(OpenTagFor(styled.Style));
                Write(styled.Children, fragment, depth + 1);
                fragment.Markup(SpanClose);
                return;
            }

            var code = inline as CodeRun;
            if (code != null)
            {
                fragment.Markup(CodeOpen);
                fragment.Text(code.Text);
                fragment.Markup(SpanClose);
                return;
            }

            var link = inline as HyperlinkRun;
            if (link != null)
            {
                fragment.Markup("<a href=\"" + XmlText.EscapeAttribute(link.Url) + "\">");
                Write(link.Children, fragment, depth + 1);
                fragment.Markup("</a>");
                return;
            }

            var lineBreak = inline as LineBreakRun;
            if (lineBreak != null)
            {
                fragment.Markup(lineBreak.IsHard ? "<br/>" : " ");
                return;
            }

            var footnote = inline as FootnoteReferenceRun;
            if (footnote != null)
            {
                // A real superscript, now that the dump has shown what OneNote accepts. The square
                // brackets were a placeholder for exactly this: `<sup>` was the assumed markup and
                // was never confirmed — OneNote uses a vertical-align span
                // (docs/page-schema-notes.md §5).
                fragment.Markup(SuperscriptOpen);
                fragment.Text(footnote.Number.ToString(CultureInfo.InvariantCulture));
                fragment.Markup(SpanClose);
            }
        }

        /// <summary>
        /// Writes one line of a code block: leading indentation as non-breaking spaces, then the
        /// coloured tokens. Runs of interior spaces are protected too, because a lined-up comment
        /// or ASCII table in a code listing collapses just as readily as an indent does.
        /// </summary>
        public static string WriteCodeLine(IReadOnlyList<CodeToken> tokens)
        {
            var fragment = new FragmentBuilder();
            var atLineStart = true;

            for (var i = 0; i < tokens.Count; i++)
            {
                var token = tokens[i];
                var colored = !string.IsNullOrEmpty(token.Color);
                if (colored)
                {
                    fragment.Markup("<span style='color:" + token.Color + "'>");
                }

                atLineStart = WriteCodeText(token.Text, atLineStart, fragment);

                if (colored)
                {
                    fragment.Markup(SpanClose);
                }
            }

            return fragment.ToString();
        }

        private const int TabWidth = 4;

        private static bool WriteCodeText(string text, bool atLineStart, FragmentBuilder fragment)
        {
            var i = 0;
            while (i < text.Length)
            {
                if (text[i] != ' ' && text[i] != '\t')
                {
                    var start = i;
                    while (i < text.Length && text[i] != ' ' && text[i] != '\t')
                    {
                        i++;
                    }

                    fragment.Text(text.Substring(start, i - start));
                    atLineStart = false;
                    continue;
                }

                var spaces = 0;
                var width = 0;
                while (i < text.Length && (text[i] == ' ' || text[i] == '\t'))
                {
                    width += text[i] == '\t' ? TabWidth : 1;
                    spaces++;
                    i++;
                }

                // A lone space between two words can stay a space and let the line wrap. Anything
                // else is alignment the author meant to keep, so it survives as &nbsp;.
                if (atLineStart || spaces > 1 || width > 1)
                {
                    fragment.NonBreakingSpaces(width);
                }
                else
                {
                    fragment.Text(" ");
                }

                atLineStart = false;
            }

            return atLineStart;
        }

        private static string OpenTagFor(RunStyle style)
        {
            switch (style)
            {
                case RunStyle.Bold:
                    return BoldOpen;
                case RunStyle.Strikethrough:
                    return StrikeOpen;
                default:
                    return ItalicOpen;
            }
        }
    }

    /// <summary>
    /// XML-level text hygiene, applied to everything that reaches the document — attribute values
    /// as well as <c>one:T</c> content.
    /// </summary>
    internal static class XmlText
    {
        /// <summary>
        /// Whether a character may appear in an XML 1.0 document at all. Untrusted Markdown can
        /// contain control characters that no amount of escaping makes legal, and a page that
        /// cannot be parsed is a failed import (NFR-4, NFR-8), so they are dropped.
        /// </summary>
        public static bool IsAllowed(char c)
        {
            if (c == '\t' || c == '\n' || c == '\r')
            {
                return true;
            }

            if (c < 0x20)
            {
                return false;
            }

            // Surrogates are legal only as pairs; XLinq validates that when the string is written,
            // and a lone half would fail the whole page rather than one character.
            return c != 0xFFFE && c != 0xFFFF;
        }

        /// <summary>Strips characters XML cannot represent. For attribute values and page text.</summary>
        public static string Sanitize(string value)
        {
            if (string.IsNullOrEmpty(value))
            {
                return value ?? string.Empty;
            }

            var needed = false;
            for (var i = 0; i < value.Length; i++)
            {
                if (!IsAllowed(value[i]))
                {
                    needed = true;
                    break;
                }
            }

            if (!needed)
            {
                return value;
            }

            var builder = new StringBuilder(value.Length);
            for (var i = 0; i < value.Length; i++)
            {
                if (IsAllowed(value[i]))
                {
                    builder.Append(value[i]);
                }
            }

            return builder.ToString();
        }

        /// <summary>
        /// Escapes a value for an attribute inside a <c>one:T</c> fragment. That fragment sits in
        /// CDATA, so XLinq never sees it and cannot escape it for us.
        /// </summary>
        public static string EscapeAttribute(string value)
        {
            var builder = new StringBuilder(value.Length + 8);
            for (var i = 0; i < value.Length; i++)
            {
                var c = value[i];
                if (!IsAllowed(c))
                {
                    continue;
                }

                switch (c)
                {
                    case '&':
                        builder.Append("&amp;");
                        break;
                    case '<':
                        builder.Append("&lt;");
                        break;
                    case '>':
                        builder.Append("&gt;");
                        break;
                    case '"':
                        builder.Append("&quot;");
                        break;
                    case '\'':
                        builder.Append("&#39;");
                        break;
                    default:
                        builder.Append(c);
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
