using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Application.Interfaces;

// FR-014/FR-016: both tokenizer profiles (ADR-003) computed together, never
// one at a time - see TokenEstimate for why.
public interface ITokenEstimator
{
    Task<TokenEstimate> EstimateAsync(
        string originalText,
        string convertedMarkdown,
        CancellationToken cancellationToken);
}
