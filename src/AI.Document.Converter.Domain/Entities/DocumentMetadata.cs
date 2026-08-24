using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Domain.Entities;

// Maps directly to the FR-013 front-matter fields (source, file_type, created_date,
// converted_date, pages, author). Author is nullable and, per docs/09-DATA-MODEL.md
// Section 2, is omitted from front matter entirely when null, not written as "".
public sealed class DocumentMetadata
{
    public required string SourceFilePath { get; init; }

    public required SupportedFileType FileType { get; init; }

    public required DateTime CreatedDate { get; init; }

    public required DateTime ConvertedDate { get; init; }

    public int? PageCount { get; init; }

    public int? SlideCount { get; init; }

    public int? SheetCount { get; init; }

    public string? Author { get; init; }
}
