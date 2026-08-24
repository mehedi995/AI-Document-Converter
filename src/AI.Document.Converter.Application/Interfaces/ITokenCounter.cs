namespace AI.Document.Converter.Application.Interfaces;

// ADR-003: chunk sizing (FR-018) uses the exact GPT-4o-style (o200k_base)
// count, distinct from ITokenEstimator's "original vs converted, both
// providers" comparison. Batched by design - ChunkGenerator needs a count per
// atomic unit (heading+block groupings) and must not spawn one subprocess per
// unit (ADR-001's per-request process-start cost is real; see
// docs/18-RISK-ASSESSMENT.md R-17).
public interface ITokenCounter
{
    Task<IReadOnlyList<int>> CountBatchAsync(IReadOnlyList<string> texts, CancellationToken cancellationToken);
}
