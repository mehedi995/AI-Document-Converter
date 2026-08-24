namespace AI.Document.Converter.Application.Models;

// AC-004/AC-005/AC-031: distinguishes files rejected for an unsupported extension
// from files excluded purely because the batch ceiling (NFR-013) was reached -
// the UI shows a different message for each.
public sealed class ImportResult
{
    public required IReadOnlyList<FileImportItem> AcceptedFiles { get; init; }

    public required IReadOnlyList<RejectedFile> RejectedFiles { get; init; }

    public required int FilesExcludedByBatchLimit { get; init; }
}
