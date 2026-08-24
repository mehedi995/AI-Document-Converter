namespace AI.Document.Converter.Application.DTOs;

// Payload for a PythonEngineRequest with Operation = "tokenize" (ADR-003).
public sealed class TokenizeRequestPayload
{
    public required string OriginalText { get; init; }

    public required string ConvertedText { get; init; }
}
