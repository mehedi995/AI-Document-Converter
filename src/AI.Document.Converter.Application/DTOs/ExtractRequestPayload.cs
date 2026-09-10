using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.DTOs;

// Payload for a PythonEngineRequest with Operation = "extract" (ADR-001).
public sealed class ExtractRequestPayload
{
    public required string FilePath { get; init; }

    public required SupportedFileType Format { get; init; }

    // Serialises as "faithful"/"summary", which is what the engine's dispatch
    // expects. Sent for every format; the engine applies it only where more
    // than one mode exists, which today is XLSX alone.
    public ExtractionMode Mode { get; init; } = ExtractionMode.Faithful;
}
