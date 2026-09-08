namespace AI.Document.Converter.Domain.Entities;

// The normalized document model every IDocumentProcessor implementation maps its
// source format into (FR-011), before Markdown generation.
public sealed class DocumentModel
{
    // Model v2 (SaaS audit B-06). Bumped when the shape changes in a way a
    // consumer must notice; carried into artifact metadata so an output can
    // always be traced back to the contract that produced it. v1 payloads
    // omit it entirely, which is why this is not `required`.
    public string? ModelVersion { get; init; }

    // Which build of the extraction engine produced this. Two runs that differ
    // only by engine version can legitimately differ in output, and without
    // this there is no way to tell that from a regression.
    public string? EngineVersion { get; init; }

    public required DocumentMetadata Metadata { get; init; }

    public required List<Section> Sections { get; init; }

    // Everything the extractor could not fully recover (SaaS audit B-05).
    // Empty means "nothing was knowingly lost" - it does NOT mean the parser
    // recovered every fact from the source, which is a separate claim that has
    // to be evaluated against fixtures.
    public List<ExtractionWarning> Warnings { get; init; } = [];

    // A result carrying any Error-severity warning is incomplete, and the job
    // must surface it as "completed with warnings" rather than a plain success
    // (SR-INT-1, SR-INT-3). Centralized here so no caller has to re-derive the
    // rule and get it subtly wrong.
    public bool HasUnrecoveredContent =>
        Warnings.Any(w => w.Severity == Enums.WarningSeverity.Error);
}
