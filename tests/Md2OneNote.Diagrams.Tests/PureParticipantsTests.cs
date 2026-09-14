using Md2OneNote.Diagrams;
using Xunit;

namespace Md2OneNote.Diagrams.Tests
{
    /// <summary>
    /// The parts of the diagram subsystem that run without a browser: the reply protocol, the
    /// script escaping, and the runaway-rendering policy. The WebView2 path itself is verified
    /// by importing a document with diagrams (IMPLEMENTATION.md §12).
    /// </summary>
    public class RenderReplyTests
    {
        [Fact]
        public void Parses_a_successful_reply()
        {
            var reply = RenderReply.TryParse("{\"id\":\"abc\",\"ok\":true,\"w\":720,\"h\":410}");

            Assert.NotNull(reply);
            Assert.Equal("abc", reply.Id);
            Assert.True(reply.Ok);
            Assert.Equal(720, reply.Width);
            Assert.Equal(410, reply.Height);
        }

        [Fact]
        public void Parses_a_failure_reply()
        {
            var reply = RenderReply.TryParse("{\"id\":\"abc\",\"ok\":false,\"error\":\"Parse error on line 2\"}");

            Assert.False(reply.Ok);
            Assert.Equal("Parse error on line 2", reply.Error);
        }

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("not json")]
        [InlineData("{\"ok\":true}")]
        [InlineData("42")]
        public void Anything_that_is_not_a_reply_is_null_rather_than_an_exception(string json)
        {
            Assert.Null(RenderReply.TryParse(json));
        }
    }

    public class JsLiteralTests
    {
        [Fact]
        public void Quotes_and_escapes_what_would_break_out_of_a_string()
        {
            var literal = JsLiteral.Quote("a \"b\" \\ c\nd</script>\u2028");

            Assert.Equal("\"a \\\"b\\\" \\\\ c\\nd\\u003c/script\\u003e\\u2028\"", literal);
        }

        [Fact]
        public void Null_becomes_an_empty_string()
        {
            Assert.Equal("\"\"", JsLiteral.Quote(null));
        }
    }

    public class RendererHealthTests
    {
        [Fact]
        public void A_timeout_poisons_the_instance_until_it_is_replaced()
        {
            var health = new RendererHealth();

            health.RecordFault(poisonsInstance: true);
            Assert.True(health.Poisoned);

            health.InstanceReplaced();
            Assert.False(health.Poisoned);
        }

        [Fact]
        public void Consecutive_faults_degrade_the_subsystem_exactly_once()
        {
            var health = new RendererHealth(faultsBeforeDegraded: 3);

            Assert.False(health.RecordFault(false));
            Assert.False(health.RecordFault(false));
            Assert.False(health.Degraded);

            Assert.True(health.RecordFault(false));
            Assert.True(health.Degraded);

            Assert.False(health.RecordFault(false));
        }

        [Fact]
        public void A_success_or_a_diagrams_own_failure_resets_the_streak()
        {
            var health = new RendererHealth(faultsBeforeDegraded: 2);

            health.RecordFault(false);
            health.RecordDiagramFailure();
            health.RecordFault(false);
            Assert.False(health.Degraded);

            health.RecordSuccess();
            health.RecordFault(false);
            Assert.False(health.Degraded);
            Assert.Equal(1, health.ConsecutiveFaults);
        }
    }
}
