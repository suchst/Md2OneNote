using System;

namespace Md2OneNote.Core
{
    /// <summary>
    /// The result of trying to load a referenced image. A rejection by path policy is an ordinary
    /// failure outcome, rendered as a visible placeholder plus a warning (FR-10) — never an
    /// exception and never a silent omission.
    /// </summary>
    public sealed class AssetOutcome
    {
        private AssetOutcome(bool succeeded, byte[] bytes, string format, int widthPx, int heightPx, string failureMessage)
        {
            Succeeded = succeeded;
            Bytes = bytes;
            Format = format;
            WidthPx = widthPx;
            HeightPx = heightPx;
            FailureMessage = failureMessage;
        }

        public bool Succeeded { get; }

        public byte[] Bytes { get; }

        /// <summary>"png", "jpg" or "gif" — the value written to one:Image/@format.</summary>
        public string Format { get; }

        public int WidthPx { get; }

        public int HeightPx { get; }

        public string FailureMessage { get; }

        public static AssetOutcome Success(byte[] bytes, string format, int widthPx, int heightPx)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (string.IsNullOrEmpty(format)) throw new ArgumentNullException(nameof(format));
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));

            return new AssetOutcome(true, bytes, format, widthPx, heightPx, null);
        }

        public static AssetOutcome Failure(string message)
        {
            if (string.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));

            return new AssetOutcome(false, null, null, 0, 0, message);
        }
    }
}
