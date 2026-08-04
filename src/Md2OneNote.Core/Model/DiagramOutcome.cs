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
        private DiagramOutcome(bool succeeded, byte[] png, int widthPx, int heightPx, string failureMessage)
        {
            Succeeded = succeeded;
            Png = png;
            WidthPx = widthPx;
            HeightPx = heightPx;
            FailureMessage = failureMessage;
        }

        public bool Succeeded { get; }

        /// <summary>Captured PNG bytes, or null when the render failed.</summary>
        public byte[] Png { get; }

        /// <summary>Captured width in pixels. The renderer halves this for one:Size (DESIGN.md §8.1).</summary>
        public int WidthPx { get; }

        public int HeightPx { get; }

        /// <summary>Localized text shown beneath the fallback code block, or null on success.</summary>
        public string FailureMessage { get; }

        public static DiagramOutcome Success(byte[] png, int widthPx, int heightPx)
        {
            if (png == null) throw new ArgumentNullException(nameof(png));
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));

            return new DiagramOutcome(true, png, widthPx, heightPx, null);
        }

        public static DiagramOutcome Failure(string message)
        {
            if (string.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));

            return new DiagramOutcome(false, null, 0, 0, message);
        }
    }
}
