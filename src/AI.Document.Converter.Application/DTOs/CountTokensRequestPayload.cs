namespace AI.Document.Converter.Application.DTOs;

// Payload for a PythonEngineRequest with Operation = "count_tokens" (ADR-003).
// Deliberately batched - see ITokenCounter for why.
public sealed class CountTokensRequestPayload
{
    public required IReadOnlyList<string> Texts { get; init; }
}
