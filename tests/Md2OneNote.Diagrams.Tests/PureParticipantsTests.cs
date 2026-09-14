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

        // ---- ScreenshotReply ------------------------------------------------------------

        [Fact]
        public void A_screenshot_reply_decodes_its_base64_data()
        {
            var bytes = ScreenshotReply.TryDecode("{\"data\":\"iVBORw0KGgo=\"}");

            Assert.Equal(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }, bytes);
        }

        [Fact]
        public void A_reply_without_an_image_decodes_to_nothing()
        {
            Assert.Null(ScreenshotReply.TryDecode(null));
            Assert.Null(ScreenshotReply.TryDecode(string.Empty));
            Assert.Null(ScreenshotReply.TryDecode("{}"));
            Assert.Null(ScreenshotReply.TryDecode("{\"data\":\"\"}"));
            Assert.Null(ScreenshotReply.TryDecode("{\"data\":\"not base64!\"}"));
            Assert.Null(ScreenshotReply.TryDecode("not json"));
        }

        // ---- PngHeader ------------------------------------------------------------------

        [Fact]
        public void The_pixel_size_comes_from_the_IHDR_chunk()
        {
            var png = new byte[]
            {
                0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A,
                0x00, 0x00, 0x00, 0x0D, (byte)'I', (byte)'H', (byte)'D', (byte)'R',
                0x00, 0x00, 0x09, 0xAC, // 2476
                0x00, 0x00, 0x07, 0xEA, // 2026
                0x08, 0x06, 0x00, 0x00, 0x00
            };

            var size = PngHeader.Size(png);

            Assert.Equal(2476, size.Item1);
            Assert.Equal(2026, size.Item2);
        }

        [Fact]
        public void Anything_that_is_not_a_png_has_no_size()
        {
            Assert.Null(PngHeader.Size(null));
            Assert.Null(PngHeader.Size(new byte[10]));
            Assert.Null(PngHeader.Size(new byte[40]));

            var jpeg = new byte[40];
            jpeg[0] = 0xFF;
            jpeg[1] = 0xD8;
            Assert.Null(PngHeader.Size(jpeg));
        }
    }
}
