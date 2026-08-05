using System;
using System.Collections.Generic;
using ColorCode;
using ColorCode.Parsing;
using ColorCode.Styling;

namespace Md2OneNote.Core.Rendering
{
    /// <summary>
    /// Syntax highlighting through ColorCode (FR-9), reduced to the coloured runs the OneNote code
    /// block writer needs.
    /// </summary>
    /// <remarks>
    /// ColorCode is the only place in Core where untrusted input meets a third-party parser, so
    /// every call is contained: an unknown language, a tokenizer that throws, and a tokenizer that
    /// returns something inconsistent all degrade to unhighlighted code rather than failing the
    /// page (FR-13, NFR-8).
    /// </remarks>
    public sealed class ColorCodeSyntaxHighlighter : ISyntaxHighlighter
    {
        public static ColorCodeSyntaxHighlighter Instance { get; } = new ColorCodeSyntaxHighlighter();

        public IReadOnlyList<CodeToken> Highlight(string language, string code)
        {
            if (code == null) throw new ArgumentNullException(nameof(code));

            var resolved = Resolve(language);
            if (resolved == null || code.Length == 0)
            {
                return PlainSyntaxHighlighter.Instance.Highlight(language, code);
            }

            try
            {
                var tokens = new Tokenizer().Run(resolved, code);
                return Covers(tokens, code) ? tokens : PlainSyntaxHighlighter.Instance.Highlight(language, code);
            }
            catch (Exception)
            {
                // Highlighting is decoration. Nothing it can do to itself is worth losing the
                // user's code block over.
                return PlainSyntaxHighlighter.Instance.Highlight(language, code);
            }
        }

        private static ILanguage Resolve(string language)
        {
            if (string.IsNullOrWhiteSpace(language))
            {
                return null;
            }

            try
            {
                // FindById already understands the usual fence aliases: cs, py, js, ps1.
                return Languages.FindById(language.Trim().ToLowerInvariant());
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// Verifies the invariant <see cref="ISyntaxHighlighter"/> promises: the tokens reproduce
        /// the source exactly. Checked rather than assumed, because a tokenizer that silently drops
        /// a character would silently corrupt the user's code.
        /// </summary>
        private static bool Covers(IReadOnlyList<CodeToken> tokens, string code)
        {
            var length = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                length += tokens[i].Text.Length;
            }

            if (length != code.Length)
            {
                return false;
            }

            var offset = 0;
            for (var i = 0; i < tokens.Count; i++)
            {
                var text = tokens[i].Text;
                if (string.CompareOrdinal(code, offset, text, 0, text.Length) != 0)
                {
                    return false;
                }

                offset += text.Length;
            }

            return true;
        }

        /// <summary>
        /// ColorCode drives output through a protected callback and keeps state while it does, so
        /// each call gets its own instance rather than sharing one.
        /// </summary>
        private sealed class Tokenizer : CodeColorizerBase
        {
            private readonly List<CodeToken> _tokens = new List<CodeToken>();

            public Tokenizer()
                : base(StyleDictionary.DefaultLight, null)
            {
            }

            public IReadOnlyList<CodeToken> Run(ILanguage language, string code)
            {
                languageParser.Parse(code, language, (parsedSourceCode, captures) => Write(parsedSourceCode, captures));
                return _tokens;
            }

            protected override void Write(string parsedSourceCode, IList<Scope> scopes)
            {
                // Only the outermost capture of each region is used. Nested scopes would give
                // finer colour, at the cost of a merge that has to be exactly right to preserve
                // the source text; the flat reading cannot lose a character.
                var spans = new List<Scope>();
                for (var i = 0; i < scopes.Count; i++)
                {
                    if (scopes[i].Length > 0)
                    {
                        spans.Add(scopes[i]);
                    }
                }

                spans.Sort((x, y) => x.Index.CompareTo(y.Index));

                var offset = 0;
                for (var i = 0; i < spans.Count; i++)
                {
                    var span = spans[i];
                    if (span.Index < offset || span.Index + span.Length > parsedSourceCode.Length)
                    {
                        continue;
                    }

                    Add(parsedSourceCode.Substring(offset, span.Index - offset), null);
                    Add(parsedSourceCode.Substring(span.Index, span.Length), ColorFor(span.Name));
                    offset = span.Index + span.Length;
                }

                Add(parsedSourceCode.Substring(offset), null);
            }

            private void Add(string text, string color)
            {
                if (text.Length == 0)
                {
                    return;
                }

                _tokens.Add(new CodeToken(text, color));
            }

            private string ColorFor(string scopeName)
            {
                if (string.IsNullOrEmpty(scopeName) || !Styles.Contains(scopeName))
                {
                    return null;
                }

                return ToRgb(Styles[scopeName].Foreground);
            }

            /// <summary>ColorCode stores colours as <c>#AARRGGBB</c>; OneNote wants <c>#RRGGBB</c>.</summary>
            private static string ToRgb(string argb)
            {
                if (string.IsNullOrEmpty(argb) || argb[0] != '#')
                {
                    return null;
                }

                if (argb.Length == 9)
                {
                    return "#" + argb.Substring(3);
                }

                return argb.Length == 7 ? argb : null;
            }
        }
    }
}
