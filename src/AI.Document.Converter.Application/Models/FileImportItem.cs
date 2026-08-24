using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.Models;

// One row in the conversion list (FR-001-004, TASK-020). Immutable for Phase 2 -
// see the comment on ImportItemStatus for how this is expected to evolve.
public sealed class FileImportItem
{
    public required string FilePath { get; init; }

    public required string FileName { get; init; }

    public required SupportedFileType FileType { get; init; }

    public required long FileSizeBytes { get; init; }

    public ImportItemStatus Status { get; init; } = ImportItemStatus.Ready;
}
