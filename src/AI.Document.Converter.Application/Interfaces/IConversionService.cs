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

    // Audit D-05: extract once, fan out to both outputs.
    //
    // When chunkOptions is supplied, this produces the Markdown AND the chunks
    // from a SINGLE extraction, which is what the SaaS worker already does and
    // is worth roughly half the engine time for anyone who wants both.
    //
    // It does not replace GenerateChunksAsync, and the reasoning below that
    // method still stands: chunking on its own remains a separate action, and
    // nothing caches a DocumentModel across two user actions. The saving is
    // available only when both outputs are asked for at once, where exactly
    // one document is live at a time - the same memory profile as an ordinary
    // conversion.
    Task<ConversionResult> ConvertAsync(
        string filePath,
        string outputDirectory,
        IOutputPathResolver outputPathResolver,
        ChunkOptions? chunkOptions,
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
