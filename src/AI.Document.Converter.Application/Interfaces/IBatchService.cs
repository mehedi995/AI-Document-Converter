using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;

namespace AI.Document.Converter.Application.Interfaces;

// FR-022-025/FR-037/NFR-011: orchestrates the single-file pipeline (whichever
// operation the caller supplies - IConversionService.ConvertAsync or
// .GenerateChunksAsync, both already share the ConversionResult shape) across
// many files with bounded parallelism, per-file progress, and cancellation.
// Generic over the operation rather than hard-coding "convert" so both the
// Dashboard's Convert All and Generate Chunks actions reuse the same
// parallelism/progress/cancellation logic instead of duplicating it.
public interface IBatchService
{
    Task<BatchSummary> RunAsync(
        IReadOnlyList<string> filePaths,
        Func<string, CancellationToken, Task<ConversionResult>> operation,
        int maxParallelism,
        IProgress<BatchProgressUpdate> progress,
        CancellationToken cancellationToken);
}
