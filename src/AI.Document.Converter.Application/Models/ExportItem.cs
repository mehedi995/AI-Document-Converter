using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Models;

// FR-028: one entry per file the batch attempted - ConversionResult carries
// the Convert stage's outcome (and DocumentMetadata, FR-046); ChunkResult is
// null when Generate Chunks was never run for this file.
public sealed class ExportItem
{
    public required string SourceFilePath { get; init; }

    public required ConversionResult ConversionResult { get; init; }

    public ConversionResult? ChunkResult { get; init; }
}
