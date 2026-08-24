namespace AI.Document.Converter.Application.Models;

// AC-016/AC-025: ProcessedFiles may be less than TotalFiles when the batch
// was cancelled (FR-037) - that is the "partial summary" the caller reports.
public sealed class BatchSummary
{
    public required int TotalFiles { get; init; }

    public required int ProcessedFiles { get; init; }

    public required int SuccessCount { get; init; }

    public required int FailureCount { get; init; }
}
