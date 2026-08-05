using System.Linq;
using Md2OneNote.Core.Rendering;
using Xunit;

namespace Md2OneNote.Core.Tests
{
    public class SyntaxHighlighterTests
    {
        private readonly ISyntaxHighlighter _highlighter = ColorCodeSyntaxHighlighter.Instance;

        [Theory]
        [InlineData("csharp", "public class A { /* c */ int x = 1; }")]
        [InlineData("cs", "var s = \"text\";\nvar n = 42;\n")]
        [InlineData("python", "def main():\n    return 1  # done\n")]
        [InlineData("json", "{ \"a\": [1, 2], \"b\": null }")]
        [InlineData("xml", "<a b=\"c\">d</a>")]
        [InlineData("", "no language at all")]
        [InlineData("brainfuck", "+++[->+<]")]
        public void Tokens_always_reproduce_the_source_exactly(string language, string code)
        {
            var tokens = _highlighter.Highlight(language, code);

            Assert.Equal(code, string.Concat(tokens.Select(t => t.Text)));
        }

        [Fact]
        public void A_known_language_actually_gets_coloured()
        {
            var tokens = _highlighter.Highlight("csharp", "public class Thing { }");

            Assert.Contains(tokens, t => t.Color != null);
            Assert.All(tokens.Where(t => t.Color != null), t =>
            {
                Assert.StartsWith("#", t.Color);
                Assert.Equal(7, t.Color.Length);
            });
        }

        [Fact]
        public void An_unknown_language_is_left_alone_rather_than_failing()
        {
            var tokens = _highlighter.Highlight("cobol", "IDENTIFICATION DIVISION.");

            var token = Assert.Single(tokens);
            Assert.Null(token.Color);
        }

        [Fact]
        public void Highlighted_code_keeps_its_line_structure_when_rendered()
        {
            var parsed = Convert.Parse("```csharp\nif (x)\n{\n    return;\n}\n```");
            var page = Convert.Render(parsed, null, null, ColorCodeSyntaxHighlighter.Instance);

            var lines = page.Descendants(Convert.One + "T").Skip(1).Select(t => t.Value).ToArray();

            Assert.Equal(4, lines.Length);
            Assert.StartsWith("&nbsp;&nbsp;&nbsp;&nbsp;", lines[2]);
            Assert.Contains(lines, l => l.Contains("<span style='color:#"));
        }

        [Fact]
        public void Empty_code_is_not_a_special_case()
        {
            Assert.Equal(string.Empty, string.Concat(_highlighter.Highlight("csharp", string.Empty).Select(t => t.Text)));
        }
    }
}
