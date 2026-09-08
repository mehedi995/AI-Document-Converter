namespace AI.Document.Converter.Application.Models;

// AC-016/AC-025: ProcessedFiles may be less than TotalFiles when the batch
// was cancelled (FR-037) - that is the "partial summary" the caller reports.
//
// SR-JOB-4 (SaaS audit D-03): four outcomes have to be distinguishable, not
// two. Reporting only success and failure meant a batch that was cancelled
// halfway, and a batch where every file converted but half of them lost
// content to a scanned page, both reported as clean successes.
public sealed class BatchSummary
{
    public required int TotalFiles { get; init; }

    public required int ProcessedFiles { get; init; }

    // Completed with nothing knowingly lost. Excludes WarningCount - a file is
    // counted in exactly one of these buckets, so the four always sum to
    // ProcessedFiles and a caller can safely display them side by side.
    public required int SuccessCount { get; init; }

    // Completed, but the extractor or chunker could not recover everything
    // (an Error-severity warning). The output exists and is downloadable; it
    // is just not complete, and saying otherwise would be a false claim.
    public int WarningCount { get; init; }

    public required int FailureCount { get; init; }

    // Stopped part-way by the user (FR-037), which is not a failure of the
    // file and must not be reported as one.
    public int CancelledCount { get; init; }
}
