using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.Models;

// FR-028: the safe, categorized result of one export attempt - follows the
// same "never throw across the UI boundary" shape as ConversionResult/
// ImportResult.
public sealed class ExportResult
{
    public required bool Success { get; init; }

    public string? ZipPath { get; init; }

    public ErrorCategory? Error { get; init; }

    public string? ErrorMessage { get; init; }

    public int ExportedFileCount { get; init; }
}
