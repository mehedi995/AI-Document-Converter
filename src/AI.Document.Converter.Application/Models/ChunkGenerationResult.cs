using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Models;

// Chunking can discover problems the extractor could not know about, because
// they depend on the chunk size the user chose - an oversized table is the
// case that matters (SR-INT-7, audit C-08). Returning only the chunks left
// nowhere to report that, so the generator returns both.
public sealed class ChunkGenerationResult
{
    public required IReadOnlyList<DocumentChunk> Chunks { get; init; }

    // Empty when nothing needed saying. Never null, so callers can concatenate
    // without a null check.
    public IReadOnlyList<ExtractionWarning> Warnings { get; init; } = [];
}
