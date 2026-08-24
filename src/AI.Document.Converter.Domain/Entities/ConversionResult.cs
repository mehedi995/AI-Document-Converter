using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Domain.Entities;

public sealed class ConversionResult
{
    public required bool Success { get; init; }

    public string? OutputPath { get; init; }

    public TokenEstimate? Tokens { get; init; }

    public ErrorCategory? Error { get; init; }

    public string? ErrorMessage { get; init; }

    public PipelineStage CompletedStages { get; init; } = PipelineStage.None;

    // Populated only when this result comes from a chunk-generation run
    // (FR-018-021); null for a plain conversion result.
    public int? ChunkCount { get; init; }

    // Populated on a successful ConvertAsync (FR-046): carries the
    // extracted document's own metadata through to Export, which needs it
    // to build metadata/<name>.json without re-parsing it back out of the
    // already-written front matter.
    public DocumentMetadata? Metadata { get; init; }
}
