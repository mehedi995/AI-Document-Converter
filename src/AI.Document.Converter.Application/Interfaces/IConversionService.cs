using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Interfaces;

// UC-001: single-file conversion (extract -> generate Markdown -> estimate
// tokens -> write output). Sequential, no parallelism/progress/cancellation
// orchestration - that is Phase 8's BatchService, built on top of this.
public interface IConversionService
{
    // outputPathResolver is passed in, not injected, because it is stateful
    // per batch (docs/15-IMPLEMENTATION-PLAN.md Section 4) - the caller owns
    // creating one instance per conversion run and reusing it across files.
    Task<ConversionResult> ConvertAsync(
        string filePath,
        string outputDirectory,
        IOutputPathResolver outputPathResolver,
        CancellationToken cancellationToken);

    // UC-003 (Phase 7): a separate, independently-triggerable action from
    // ConvertAsync (matching CLAUDE.md's Chunk Settings screen having its own
    // "Generate Chunks" action) rather than an optional parameter bolted onto
    // ConvertAsync - this re-extracts the source file rather than caching the
    // DocumentModel from an earlier ConvertAsync call, trading a small amount
    // of redundant work for not having to manage in-memory document lifetime
    // across separate UI actions.
    Task<ConversionResult> GenerateChunksAsync(
        string filePath,
        string outputDirectory,
        ChunkOptions chunkOptions,
        CancellationToken cancellationToken);
}
