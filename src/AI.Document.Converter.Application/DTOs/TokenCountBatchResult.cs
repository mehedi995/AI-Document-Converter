namespace AI.Document.Converter.Application.DTOs;

// Result shape for a "count_tokens" response - Counts[i] corresponds to
// Texts[i] from the request, in order.
public sealed class TokenCountBatchResult
{
    public required IReadOnlyList<int> Counts { get; init; }
}
