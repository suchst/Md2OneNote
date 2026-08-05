using System;
using Markdig.Extensions.Yaml;
using Md = Markdig.Syntax;

namespace Md2OneNote.Core.Parsing
{
    /// <summary>
    /// Reads the one front-matter key the product honours. FR-15 needs <c>title</c> and nothing
    /// else; <c>tags</c> is deliberately left alone until OQ-6 is answered.
    /// </summary>
    /// <remarks>
    /// This is a scalar lookup, not a YAML parser, and taking a YAML dependency to read one string
    /// would be a licence, size and attack-surface cost (NFR-15) out of all proportion to the
    /// feature. Anything it does not understand — block scalars, nested mappings, flow syntax —
    /// yields no title, which falls back to the leading H1 and then the filename.
    /// </remarks>
    internal static class FrontMatter
    {
        private const string TitleKey = "title:";

        public static string ReadTitle(Md.MarkdownDocument document)
        {
            foreach (var block in document)
            {
                var yaml = block as YamlFrontMatterBlock;
                if (yaml == null)
                {
                    continue;
                }

                return ReadTitle(yaml);
            }

            return null;
        }

        private static string ReadTitle(YamlFrontMatterBlock yaml)
        {
            for (var i = 0; i < yaml.Lines.Count; i++)
            {
                var line = yaml.Lines.Lines[i].Slice.ToString();

                // A key belonging to the document itself sits at column zero; an indented one is
                // nested under something else and is not the page title.
                if (line.Length == 0 || char.IsWhiteSpace(line[0]))
                {
                    continue;
                }

                if (!line.StartsWith(TitleKey, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                var value = Unquote(line.Substring(TitleKey.Length).Trim());
                return value.Length == 0 ? null : value;
            }

            return null;
        }

        private static string Unquote(string value)
        {
            // A trailing comment is only a comment when it is separated from the value; "a # b"
            // without quotes is ambiguous in YAML too, and guessing wrong truncates a real title.
            if (value.Length >= 2 &&
                (value[0] == '"' || value[0] == '\'') &&
                value[value.Length - 1] == value[0])
            {
                return value.Substring(1, value.Length - 2).Trim();
            }

            // Block and folded scalars carry their content on following lines, which this reader
            // does not follow. Treat them as absent rather than emitting "|" as a page title.
            if (value == "|" || value == ">" || value.StartsWith("|", StringComparison.Ordinal) ||
                value.StartsWith(">", StringComparison.Ordinal))
            {
                return string.Empty;
            }

            return value;
        }
    }
}
