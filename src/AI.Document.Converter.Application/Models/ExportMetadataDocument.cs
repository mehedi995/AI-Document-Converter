using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Models;

// FR-046: the JSON written to metadata/<name>.json inside the export ZIP.
// Deliberately does not duplicate the front matter already embedded in the
// markdown/chunks files (FR-013) beyond what's needed to make this file
// self-contained - it exists specifically to carry the conversion-result
// data (token estimates, success/error, chunk count) that today is shown
// only transiently in the UI and never otherwise persisted anywhere.
public sealed class ExportMetadataDocument
{
    public required string SourceFileName { get; init; }

    public required string FileType { get; init; }

    public required DateTime CreatedDate { get; init; }

    public required DateTime ConvertedDate { get; init; }

    public int? PageCount { get; init; }

    public int? SlideCount { get; init; }

    public int? SheetCount { get; init; }

    public string? Author { get; init; }

    public required bool ConversionSuccess { get; init; }

    public string? ConversionErrorMessage { get; init; }

    public TokenEstimate? Tokens { get; init; }

    public int? ChunkCount { get; init; }
}
