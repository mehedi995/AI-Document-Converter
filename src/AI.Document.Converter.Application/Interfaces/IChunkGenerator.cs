using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Interfaces;

// FR-018-021: operates on the structured DocumentModel, not the rendered
// Markdown string (docs/03-SRS.md Section 10's architectural guidance) - so
// table/heading-safety can be guaranteed at the block level rather than
// re-parsed out of finished text.
public interface IChunkGenerator
{
    Task<ChunkGenerationResult> GenerateChunksAsync(
        DocumentModel document,
        ChunkOptions options,
        string sourceFileName,
        CancellationToken cancellationToken);
}
