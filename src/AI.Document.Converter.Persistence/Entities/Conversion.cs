namespace AI.Document.Converter.Persistence.Entities;

// SaaS §7. Every value here must reflect real work: a status is set when the
// stage actually starts or finishes, never optimistically.
public enum JobStatus
{
    // Accepted and persisted, not yet safe to run - uploads still being
    // checked. Persisting before scheduling is what makes recovery possible
    // (SR-JOB-1).
    Validating = 0,
    Queued = 1,
    Extracting = 2,
    Formatting = 3,
    Chunking = 4,
    Exporting = 5,

    // Everything the caller asked for was produced, with nothing knowingly
    // lost.
    Completed = 6,

    // Output exists and is downloadable, but content was NOT fully recovered
    // (an Error-severity warning). Distinct from Completed because calling
    // this a success would be a false claim of completeness (SR-INT-1/3).
    CompletedWithWarnings = 7,

    Failed = 8,
    Cancelled = 9,

    // Retention elapsed; metadata survives, bytes do not (SR-SEC-6).
    Expired = 10
}

public static class JobStatusExtensions
{
    // A job in one of these states will never change again on its own, so a
    // worker must not lease it and the UI must not show it as in-flight.
    public static bool IsTerminal(this JobStatus status) => status
        is JobStatus.Completed
        or JobStatus.CompletedWithWarnings
        or JobStatus.Failed
        or JobStatus.Cancelled
        or JobStatus.Expired;
}

// The uploaded bytes plus what we know about them. Source bytes live in object
// storage under StorageKey; this row is the metadata, and the two have
// different retention (SR-SEC-6).
public sealed class SourceDocument
{
    public Guid Id { get; init; }

    // Denormalized onto every tenant-owned row on purpose: it lets every query
    // filter by workspace directly, instead of relying on a join the caller
    // might forget (SR-SEC-2).
    public required Guid WorkspaceId { get; init; }

    // As supplied by the browser. Treated as untrusted display text - never
    // used to build a storage path, and never rendered unescaped (SR-SEC-1/3).
    public required string OriginalFileName { get; set; }

    public required string ContentType { get; init; }

    public required long SizeBytes { get; init; }

    // Of the stored bytes. Recorded so an artifact can be traced back to the
    // exact input that produced it, and so a re-upload of identical content is
    // recognisable. Deduplication, if it is ever added, stays inside the
    // workspace boundary.
    public required string Sha256 { get; init; }

    // Opaque, server-generated object-storage key. Never exposed to the client
    // and never derived from the user's filename (SR-SEC-1).
    public required string StorageKey { get; init; }

    public required DateTime UploadedAtUtc { get; init; }

    public required Guid UploadedByUserId { get; init; }

    // Null once the bytes have been removed. Metadata is kept so history and
    // billing still make sense after the source is gone.
    public DateTime? SourceBytesDeletedAtUtc { get; set; }

    public Workspace? Workspace { get; set; }
}

// One user-initiated conversion, covering one or more files.
public sealed class ConversionJob
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Guid CreatedByUserId { get; init; }

    public required JobStatus Status { get; set; }

    // Which named preset the user chose. Stored as text, not an enum, because
    // presets are server-side configuration that will gain entries without a
    // schema change.
    public required string PresetName { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    // Set when the user cancels. Distinct from CompletedAtUtc so a cancelled
    // job is never mistaken for a finished one (SR-JOB-4).
    public DateTime? CancelledAtUtc { get; set; }

    // The deletion tombstone (SR-SEC-6). Set when the customer deletes the job,
    // and it OUTLIVES the bytes on purpose.
    //
    // Without it, deletion is not actually deletion: a queued or retried item
    // could be picked up afterwards and write a fresh artifact for a job the
    // customer believes is gone. The worker checks this before publishing, and
    // a restored backup that replays old rows still carries the tombstone, so
    // the content is not served again.
    public DateTime? DeletedAtUtc { get; set; }

    // Which of the two retention clocks has already been applied, so a sweep
    // does not repeatedly re-delete and re-log the same objects.
    public DateTime? SourceBytesPurgedAtUtc { get; set; }

    public DateTime? OutputBytesPurgedAtUtc { get; set; }

    // Optimistic concurrency. Two workers finishing two items of the same job
    // at the same moment must not clobber each other's status update.
    public uint Version { get; set; }

    public ICollection<ConversionJobItem> Items { get; set; } = [];

    public Workspace? Workspace { get; set; }
}

// One file within a job - the unit that actually gets leased, retried and
// charged. A job's files succeed or fail independently (BR-006).
public sealed class ConversionJobItem
{
    public Guid Id { get; init; }

    public required Guid JobId { get; init; }

    // Repeated from the parent job so a worker can authorize a message without
    // a join, and so a stray message carrying the wrong workspace cannot read
    // another tenant's row (SR-SEC-2).
    public required Guid WorkspaceId { get; init; }

    public required Guid SourceDocumentId { get; init; }

    public required JobStatus Status { get; set; }

    // What this file was quoted at when the job was accepted (SR-BIL-5).
    //
    // Kept per item rather than only as the job's single hold, because a RETRY
    // re-queues some of a job's items and has to place a new hold covering just
    // those. Without this the retry would either run unmetered or have to guess.
    // It is an ESTIMATE: the ledger records what was actually charged.
    public long EstimatedCredits { get; set; }

    // Queue delivery is at-least-once, so this counts real processing attempts
    // and bounds retries. Exactly-once execution is not claimed (SR-JOB-2).
    public int AttemptCount { get; set; }

    // Lease: which worker currently owns this item and until when. An expired
    // lease means the worker died, and the item becomes claimable again -
    // this is what stops a crash from stranding work forever (SR-JOB-2).
    public string? LeaseOwner { get; set; }

    public DateTime? LeaseExpiresAtUtc { get; set; }

    // Machine-readable category matching the desktop pipeline's ErrorCategory,
    // plus a message that is safe to show a customer (no paths, no stack
    // traces).
    public string? ErrorCategory { get; set; }

    public string? ErrorMessage { get; set; }

    // Whether this failure is worth retrying. Stored rather than re-derived so
    // the UI and the worker cannot disagree about it (SR-JOB / audit D-07).
    public bool IsRetryable { get; set; }

    public DateTime? StartedAtUtc { get; set; }

    public DateTime? CompletedAtUtc { get; set; }

    public uint Version { get; set; }

    public ConversionJob? Job { get; set; }

    public SourceDocument? SourceDocument { get; set; }

    public ICollection<Artifact> Artifacts { get; set; } = [];

    public ICollection<JobItemWarning> Warnings { get; set; } = [];
}

public enum ArtifactKind
{
    Markdown = 0,
    ChunkSet = 1,
    MetadataJson = 2,
    ZipPackage = 3
}

// A produced output. Rows are written only once the bytes are durably stored
// and complete, so a partially written artifact is never downloadable as a
// successful result (SR-JOB-3).
public sealed class Artifact
{
    public Guid Id { get; init; }

    public required Guid JobItemId { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required ArtifactKind Kind { get; init; }

    public required string StorageKey { get; init; }

    // Suggested name for download. Display text only - the download path
    // resolves bytes by StorageKey, never by anything the user can influence.
    public required string FileName { get; init; }

    public required long SizeBytes { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    // Output bytes have their own retention, shorter than the metadata row
    // (SR-SEC-6). Null means still available.
    public DateTime? BytesDeletedAtUtc { get; set; }

    public ConversionJobItem? JobItem { get; set; }
}

// A persisted copy of what the engine could not fully recover, so the results
// screen and the export manifest can show it without re-running extraction.
// Mirrors Domain.Entities.ExtractionWarning, deliberately as its own storage
// shape rather than reusing that type: the engine contract and the database
// schema version independently (SaaS §11).
public sealed class JobItemWarning
{
    public Guid Id { get; init; }

    public required Guid JobItemId { get; init; }

    public required Guid WorkspaceId { get; init; }

    // Stable string form of WarningCode, e.g. "sheetTruncated". Text, not an
    // enum column, so the engine can introduce a code the database has not
    // been migrated for without failing the insert.
    public required string Code { get; init; }

    // "info" | "warning" | "error". An "error" here is what promotes the job
    // to CompletedWithWarnings.
    public required string Severity { get; init; }

    public required string Message { get; init; }

    public string? BlockId { get; init; }

    public int? PageNumber { get; init; }

    public int? SlideNumber { get; init; }

    public string? SheetName { get; init; }

    // Free-form specifics (rows dropped, pages without text). JSON because the
    // shape differs per code and inventing a column per code would be worse.
    public string? DetailsJson { get; init; }

    public ConversionJobItem? JobItem { get; set; }
}
