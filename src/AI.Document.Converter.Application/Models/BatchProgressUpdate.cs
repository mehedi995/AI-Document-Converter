using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.Models;

// FR-023: one report per file state transition (not just an aggregate tick),
// so the UI can update that specific file's row as well as the running
// totals - necessary once real parallelism (NFR-011) means several files are
// "in progress" at once, not just one "current file".
public sealed class BatchProgressUpdate
{
    public required string FilePath { get; init; }

    public required BatchItemState State { get; init; }

    // Set only when State is Completed.
    public ConversionResult? Result { get; init; }

    public required int TotalCount { get; init; }

    public required int ProcessedCount { get; init; }

    public required int SuccessCount { get; init; }

    // Completed, but content was not fully recovered (SR-JOB-4). Counted
    // separately from SuccessCount, never folded into it.
    public int WarningCount { get; init; }

    public required int FailureCount { get; init; }

    public int CancelledCount { get; init; }
}
