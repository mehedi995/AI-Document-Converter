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

    // Everything the pipeline could not fully recover, from extraction
    // (DocumentModel.Warnings) and from chunking (an oversized table).
    // Without this the warnings were being produced and then dropped on the
    // floor: the caller had no way to see them, so a run with missing content
    // still looked like a plain success.
    public IReadOnlyList<ExtractionWarning> Warnings { get; init; } = [];

    // A run that recovered everything it was asked for versus one that did
    // not. Callers must not treat Success alone as "the output is complete"
    // (SR-INT-1, SR-INT-3).
    public bool HasUnrecoveredContent =>
        Warnings.Any(w => w.Severity == WarningSeverity.Error);
}
