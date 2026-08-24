namespace AI.Document.Converter.Application.DTOs;

// The .NET side of the JSON stdin/stdout contract with the bundled Python engine
// (ADR-001). One shape covers every operation ("health_check" now; "extract" and
// "tokenize" are added when Phases 3 and 6 introduce them) - the operation name
// plus an operation-specific payload, rather than one request type per operation.
public sealed class PythonEngineRequest
{
    public required string Operation { get; init; }

    public object? Payload { get; init; }
}
