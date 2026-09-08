using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.ValueObjects;

namespace AI.Document.Converter.Domain.Entities;

// One thing the extractor could not fully recover from the source document.
//
// Before model v2 the normalized model had no warnings channel at all (SaaS
// audit B-05), which is why a 1000-row spreadsheet could return 5 rows and
// still report plain success (audit C-01). Everything a result does not
// contain has to be representable here, or the pipeline has no honest way to
// say so.
public sealed class ExtractionWarning
{
    public required WarningCode Code { get; init; }

    public required WarningSeverity Severity { get; init; }

    // Plain-language explanation of WHAT was not recovered - shown to the
    // customer, so it must never contain file paths, stack traces, or
    // library internals.
    public required string Message { get; init; }

    // Where in the source this happened, when it is attributable to one place.
    public SourceLocation? Location { get; init; }

    // The block this warning is about, when it is attributable to one block
    // (e.g. the oversized table). Matches ContentBlock.BlockId.
    public string? BlockId { get; init; }

    // Machine-readable specifics for the UI and the export manifest - e.g. for
    // SheetTruncated: how many rows exist versus how many were returned. Kept
    // as strings so the wire contract does not depend on numeric JSON types.
    public IReadOnlyDictionary<string, string>? Details { get; init; }
}
