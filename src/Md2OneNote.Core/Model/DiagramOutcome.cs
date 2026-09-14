using System;

namespace Md2OneNote.Core
{
    /// <summary>
    /// The result of trying to render a diagram. A failure is a rendered artifact, not an error
    /// path: the renderer emits the original fence plus the message (FR-14). Hence an outcome
    /// rather than an exception.
    /// </summary>
    public sealed class DiagramOutcome
    {
        private DiagramOutcome(bool succeeded, byte[] png, int widthPx, int heightPx, double scale, string failureMessage)
        {
            Succeeded = succeeded;
            Png = png;
            WidthPx = widthPx;
            HeightPx = heightPx;
            Scale = scale;
            FailureMessage = failureMessage;
        }

        public bool Succeeded { get; }

        /// <summary>Captured PNG bytes, or null when the render failed.</summary>
        public byte[] Png { get; }

        /// <summary>
        /// Captured width in device pixels: the diagram's natural width in CSS pixels times
        /// <see cref="Scale"/>. The renderer divides by the scale to get back to natural size and
        /// writes that in points (DESIGN.md §8.1).
        /// </summary>
        public int WidthPx { get; }

        public int HeightPx { get; }

        /// <summary>
        /// Device pixels per CSS pixel in the capture. Normally 2, so the picture stays sharp when
        /// enlarged; less for a diagram so large that 2x would exceed the capture limit. The
        /// renderer must not assume 2: dividing a 1.4x capture by 2 puts the diagram on the page
        /// at 70% of life size.
        /// </summary>
        public double Scale { get; }

        /// <summary>Localized text shown beneath the fallback code block, or null on success.</summary>
        public string FailureMessage { get; }

        public static DiagramOutcome Success(byte[] png, int widthPx, int heightPx, double scale)
        {
            if (png == null) throw new ArgumentNullException(nameof(png));
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));
            if (!(scale > 0)) throw new ArgumentOutOfRangeException(nameof(scale));

            return new DiagramOutcome(true, png, widthPx, heightPx, scale, null);
        }

        public static DiagramOutcome Failure(string message)
        {
            if (string.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));

            return new DiagramOutcome(false, null, 0, 0, 0, message);
        }
    }
}
