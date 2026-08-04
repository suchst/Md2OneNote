using System;

namespace Md2OneNote.Core
{
    public enum DiagnosticSeverity
    {
        Information,
        Warning
    }

    /// <summary>
    /// A non-fatal observation made while converting a document. Diagnostics never abort an
    /// import; they are surfaced alongside the created page (FR-P17, FR-10, FR-14).
    /// </summary>
    public sealed class Diagnostic
    {
        public Diagnostic(DiagnosticSeverity severity, string code, string message)
        {
            if (string.IsNullOrEmpty(code)) throw new ArgumentNullException(nameof(code));
            if (message == null) throw new ArgumentNullException(nameof(message));

            Severity = severity;
            Code = code;
            Message = message;
        }

        public DiagnosticSeverity Severity { get; }

        /// <summary>Stable, non-localized identifier for the kind of observation.</summary>
        public string Code { get; }

        /// <summary>Localized, user-facing text.</summary>
        public string Message { get; }

        public static Diagnostic Warning(string code, string message)
        {
            return new Diagnostic(DiagnosticSeverity.Warning, code, message);
        }

        public static Diagnostic Information(string code, string message)
        {
            return new Diagnostic(DiagnosticSeverity.Information, code, message);
        }

        public override string ToString()
        {
            return Severity + " " + Code + ": " + Message;
        }
    }
}
