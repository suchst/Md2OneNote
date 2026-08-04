using System;
using System.Collections.Generic;
using System.Linq;
using Md2OneNote.Core;

namespace Md2OneNote.Application
{
    public enum FileOutcome
    {
        Created,
        CreatedSuperseding,
        Skipped,
        Failed,
        Cancelled
    }

    public enum FailureReason
    {
        None,
        Unreadable,
        TooLarge,
        ParseError,
        SectionUnavailable,
        OneNoteBusy,
        Internal
    }

    /// <summary>
    /// What happened to one file. Warnings are non-fatal: a page is still created, with
    /// placeholders for whatever could not be resolved (FR-10, FR-14).
    /// </summary>
    public sealed class FileResult
    {
        private FileResult(
            string sourcePath,
            FileOutcome outcome,
            string pageId,
            FailureReason reason,
            string message,
            IReadOnlyList<Diagnostic> warnings)
        {
            SourcePath = sourcePath;
            Outcome = outcome;
            PageId = pageId;
            Reason = reason;
            Message = message;
            Warnings = warnings ?? (IReadOnlyList<Diagnostic>)new Diagnostic[0];
        }

        public string SourcePath { get; }

        public FileOutcome Outcome { get; }

        public string PageId { get; }

        public FailureReason Reason { get; }

        public string Message { get; }

        public IReadOnlyList<Diagnostic> Warnings { get; }

        public static FileResult Created(string sourcePath, string pageId, IReadOnlyList<Diagnostic> warnings)
        {
            return new FileResult(sourcePath, FileOutcome.Created, pageId, FailureReason.None, null, warnings);
        }

        public static FileResult CreatedSuperseding(string sourcePath, string pageId, IReadOnlyList<Diagnostic> warnings)
        {
            return new FileResult(sourcePath, FileOutcome.CreatedSuperseding, pageId, FailureReason.None, null, warnings);
        }

        public static FileResult Skipped(string sourcePath, string pageId, string message)
        {
            return new FileResult(sourcePath, FileOutcome.Skipped, pageId, FailureReason.None, message, null);
        }

        public static FileResult Failed(string sourcePath, FailureReason reason, string message)
        {
            return new FileResult(sourcePath, FileOutcome.Failed, null, reason, message, null);
        }

        public static FileResult Cancelled(string sourcePath)
        {
            return new FileResult(sourcePath, FileOutcome.Cancelled, null, FailureReason.None, null, null);
        }
    }

    /// <summary>
    /// The end-of-import report (FR-4).
    /// </summary>
    public sealed class ImportSummary
    {
        public ImportSummary(IReadOnlyList<FileResult> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));

            Results = results;
        }

        public IReadOnlyList<FileResult> Results { get; }

        public int CreatedCount
        {
            get { return Results.Count(r => r.Outcome == FileOutcome.Created || r.Outcome == FileOutcome.CreatedSuperseding); }
        }

        public int SkippedCount
        {
            get { return Results.Count(r => r.Outcome == FileOutcome.Skipped); }
        }

        public int FailedCount
        {
            get { return Results.Count(r => r.Outcome == FileOutcome.Failed); }
        }

        public int CancelledCount
        {
            get { return Results.Count(r => r.Outcome == FileOutcome.Cancelled); }
        }

        /// <summary>The page the caller should navigate to once the import finishes.</summary>
        public string FirstCreatedPageId
        {
            get
            {
                var first = Results.FirstOrDefault(r =>
                    r.Outcome == FileOutcome.Created || r.Outcome == FileOutcome.CreatedSuperseding);

                return first == null ? null : first.PageId;
            }
        }
    }

    /// <summary>
    /// Progress reporting for a running import (FR-4, NFR-19). Implementations must not throw:
    /// a UI problem is never a reason to fail an import.
    /// </summary>
    public interface IImportProgress
    {
        void FileStarted(int index, int total, string path);

        void Step(string localizedMessage);

        void FileFinished(FileResult result);
    }

    /// <summary>Progress sink for callers that do not want any.</summary>
    public sealed class NullImportProgress : IImportProgress
    {
        public static NullImportProgress Instance { get; } = new NullImportProgress();

        public void FileStarted(int index, int total, string path)
        {
        }

        public void Step(string localizedMessage)
        {
        }

        public void FileFinished(FileResult result)
        {
        }
    }
}
