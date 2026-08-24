using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.DTOs;

// Payload for a PythonEngineRequest with Operation = "extract" (ADR-001).
public sealed class ExtractRequestPayload
{
    public required string FilePath { get; init; }

    public required SupportedFileType Format { get; init; }
}
