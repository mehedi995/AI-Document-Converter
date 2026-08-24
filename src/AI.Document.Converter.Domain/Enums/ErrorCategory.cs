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

    // Retained because FR-029 names it explicitly as a required category,
    // but not currently reachable: every actual failure surface in this
    // pipeline is already more specifically categorized - extraction
    // failures above, a Python-engine timeout/bad-response as
    // PythonEngineFailure, and a genuine bug in the post-extraction
    // markdown/chunk-generation steps correctly as UnexpectedException
    // (those steps are expected to always succeed on a validly-extracted
    // DocumentModel; failing there IS unanticipated, not a "conversion
    // failure" mode). Reviewed and confirmed unused during Phase 11
    // (Testing) - don't force a contrived throw site into existence just
    // to make this reachable; keep it for FR-029 compliance only, same as
    // SEC-004's resolution in docs/18-RISK-ASSESSMENT.md R-19.
    ConversionFailure,
    OutputFailure,
    UnexpectedException
}
