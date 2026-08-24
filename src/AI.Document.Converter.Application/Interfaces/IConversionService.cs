using AI.Document.Converter.Domain.Entities;

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
}
