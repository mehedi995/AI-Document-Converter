namespace AI.Document.Converter.Domain.Enums;

// Mirrors the error categories required by SRS FR-029.
public enum ErrorCategory
{
    UnsupportedFile,
    FileNotFound,
    FileLocked,
    PermissionDenied,
    CorruptedDocument,
    PythonEngineFailure,
    ConversionFailure,
    OutputFailure,
    UnexpectedException
}
