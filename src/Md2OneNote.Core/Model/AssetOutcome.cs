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
        private AssetOutcome(bool succeeded, byte[] bytes, string format, int widthPx, int heightPx, string failureMessage, string warning)
        {
            Succeeded = succeeded;
            Bytes = bytes;
            Format = format;
            WidthPx = widthPx;
            HeightPx = heightPx;
            FailureMessage = failureMessage;
            Warning = warning;
        }

        public bool Succeeded { get; }

        public byte[] Bytes { get; }

        /// <summary>"png", "jpg" or "gif" — the value written to one:Image/@format.</summary>
        public string Format { get; }

        public int WidthPx { get; }

        public int HeightPx { get; }

        public string FailureMessage { get; }

        /// <summary>
        /// Localized text about an image that loaded but is worth mentioning — currently only one
        /// resolved outside the document's folder. Null when there is nothing to say. Unlike
        /// <see cref="FailureMessage"/> this never reaches the page: the image is there, so the
        /// remark belongs in the import report, not in the content.
        /// </summary>
        public string Warning { get; }

        public static AssetOutcome Success(byte[] bytes, string format, int widthPx, int heightPx)
        {
            return Success(bytes, format, widthPx, heightPx, null);
        }

        public static AssetOutcome Success(byte[] bytes, string format, int widthPx, int heightPx, string warning)
        {
            if (bytes == null) throw new ArgumentNullException(nameof(bytes));
            if (string.IsNullOrEmpty(format)) throw new ArgumentNullException(nameof(format));
            if (widthPx <= 0) throw new ArgumentOutOfRangeException(nameof(widthPx));
            if (heightPx <= 0) throw new ArgumentOutOfRangeException(nameof(heightPx));

            return new AssetOutcome(true, bytes, format, widthPx, heightPx, null, warning);
        }

        public static AssetOutcome Failure(string message)
        {
            if (string.IsNullOrEmpty(message)) throw new ArgumentNullException(nameof(message));

            return new AssetOutcome(false, null, null, 0, 0, message, null);
        }
    }
}
