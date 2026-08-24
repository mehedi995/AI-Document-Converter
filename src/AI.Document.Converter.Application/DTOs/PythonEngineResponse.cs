using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.DTOs;

// Mirrors the JSON response contract in ADR-001: either a successful, typed
// result, or a categorized error (ErrorCategory maps 1:1 to FR-029).
public sealed class PythonEngineResponse<TResult>
{
    public required bool Success { get; init; }

    public TResult? Result { get; init; }

    public ErrorCategory? ErrorCategory { get; init; }

    public string? ErrorMessage { get; init; }
}
