using System.Security.Cryptography;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;

namespace AI.Document.Converter.Web.Services;

// Thrown when a reconvert cannot be afforded. Reconvert has no per-file
// rejection list to put a refusal in - it either runs or it does not - so the
// refusal is raised rather than returned.
public sealed class InsufficientCreditsException(string message) : Exception(message);

public sealed record IntakeFile(string FileName, string ContentType, long SizeBytes, Stream Content);

public sealed record IntakeFileOutcome(string FileName, bool Accepted, string? RejectionCode, string? RejectionMessage);

public sealed record IntakeResult(Guid? JobId, IReadOnlyList<IntakeFileOutcome> Files)
{
    public int AcceptedCount => Files.Count(f => f.Accepted);
}

// Turns an upload into a persisted, ready-to-run job.
//
// SR-JOB-1: the job and every one of its items are committed BEFORE anything is
// scheduled. Nothing here hands work to a worker; the worker claims Queued items
// from the database itself. That ordering is what makes the system
// recovery-safe - a crash between "accepted" and "scheduled" cannot exist,
// because there is no such gap.
//
// The database is deliberately the queue for now, with the lease columns already
// on ConversionJobItem doing the work a broker's visibility timeout would.
// A separate broker can be introduced later without changing this ordering,
// and a database-backed queue avoids the dual-write problem entirely: the job
// and its "enqueued" state are the same commit.
public sealed class ConversionIntakeService
{
    private readonly ConverterDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly UploadValidator _validator;
    private readonly MeteringService _metering;
    private readonly ILogger<ConversionIntakeService> _logger;

    public ConversionIntakeService(
        ConverterDbContext db,
        IObjectStorage storage,
        UploadValidator validator,
        MeteringService metering,
        ILogger<ConversionIntakeService> logger)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
        _metering = metering;
        _logger = logger;
    }

    // Reconvert: a NEW run over source documents that are already stored.
    //
    // No re-upload, no re-validation and no second copy of the bytes - the
    // documents were validated when they were first accepted, and their content
    // has not changed. The new job references the SAME SourceDocument rows, so
    // storage does not grow and both runs remain traceable to one input.
    //
    // FR-044 is superseded for the cloud edition: this never overwrites the
    // earlier run. Cloud runs are immutable, so history stays intact and the two
    // results can be compared.
    public async Task<Guid> CreateJobFromExistingDocumentsAsync(
        Guid workspaceId,
        Guid userId,
        string presetName,
        IReadOnlyList<SourceDocument> documents,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = userId,
            Status = JobStatus.Queued,
            PresetName = presetName,
            CreatedAtUtc = nowUtc
        };
        _db.ConversionJobs.Add(job);

        long estimatedCredits = 0;

        foreach (var document in documents)
        {
            // Defence in depth. These come from a workspace-scoped query, but a
            // job must never reference a document from another tenant, so the
            // invariant is asserted where it would be violated rather than
            // assumed from the caller (SR-SEC-2).
            if (document.WorkspaceId != workspaceId)
            {
                throw new InvalidOperationException(
                    "Refusing to build a job from a document belonging to another workspace.");
            }

            // A reconvert is a real run and costs real compute, so it is metered
            // like any other. Re-reading the stored bytes to price them is the
            // only option here: the file was uploaded on an earlier request, so
            // there is no stream left to inspect.
            var estimate = await EstimateStoredDocumentAsync(document, cancellationToken);
            estimatedCredits += estimate;

            _db.ConversionJobItems.Add(new ConversionJobItem
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                WorkspaceId = workspaceId,
                SourceDocumentId = document.Id,
                Status = JobStatus.Queued,
                EstimatedCredits = estimate
            });
        }

        // One commit, exactly as for a fresh upload: the job exists, is
        // claimable, and has an allowance behind it at the same instant
        // (SR-JOB-1, SR-BIL-5).
        await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

        await _db.SaveChangesAsync(cancellationToken);

        var reservation = await _metering.TryReserveAsync(
            workspaceId, job.Id, estimatedCredits, nowUtc, cancellationToken);

        if (!reservation.Granted)
        {
            await transaction.RollbackAsync(cancellationToken);
            throw new InsufficientCreditsException(reservation.Refusal
                ?? "This workspace does not have enough credits for that conversion.");
        }

        await transaction.CommitAsync(cancellationToken);

        _logger.LogInformation(
            "Created reconvert job {JobId} over {DocumentCount} existing document(s), "
            + "holding {Credits} credit(s)",
            job.Id, documents.Count, reservation.RequestedCredits);

        return job.Id;
    }

    private async Task<long> EstimateStoredDocumentAsync(
        SourceDocument document, CancellationToken cancellationToken)
    {
        await using var stored = await _storage.OpenReadAsync(document.StorageKey, cancellationToken);

        if (stored is null)
        {
            // The bytes have passed their retention period. The job will fail on
            // that when it runs; quoting the floor keeps the hold honest rather
            // than blocking here on a billing question.
            return ConversionCredits.MinimumCreditsPerFile;
        }

        // CreditEstimator seeks, and a storage stream is not guaranteed to be
        // seekable, so it is buffered first. Bounded by the upload size limit
        // that was enforced when this document was accepted.
        await using var seekable = new MemoryStream();
        await stored.CopyToAsync(seekable, cancellationToken);

        return CreditEstimator.ForUpload(document.OriginalFileName, seekable).Credits;
    }

    public async Task<IntakeResult> AcceptAsync(
        Guid workspaceId,
        Guid userId,
        string presetName,
        IReadOnlyList<IntakeFile> files,
        CancellationToken cancellationToken)
    {
        var outcomes = new List<IntakeFileOutcome>(files.Count);
        var acceptedDocuments = new List<SourceDocument>();
        var writtenKeys = new List<string>();
        var estimatePerDocument = new Dictionary<Guid, long>();
        long estimatedCredits = 0;

        var nowUtc = DateTime.UtcNow;

        try
        {
            foreach (var file in files)
            {
                var validation = await _validator.ValidateAsync(
                    file.FileName, file.SizeBytes, file.Content, cancellationToken);

                if (!validation.IsAccepted)
                {
                    // BR-006: one rejected file does not sink the rest of the
                    // upload. The user is told which ones and why.
                    outcomes.Add(new IntakeFileOutcome(
                        UploadValidator.SanitizeFileName(file.FileName),
                        Accepted: false,
                        validation.Rejection!.Code,
                        validation.Rejection.Message));

                    _logger.LogInformation(
                        "Upload rejected for workspace {WorkspaceId}: {RejectionCode}",
                        workspaceId, validation.Rejection.Code);
                    continue;
                }

                file.Content.Position = 0;
                var sha256 = Convert.ToHexString(
                    await SHA256.HashDataAsync(file.Content, cancellationToken)).ToLowerInvariant();

                var documentId = Guid.NewGuid();
                var storageKey = StorageKeys.ForSource(workspaceId, documentId);

                // Priced from the container before the bytes are handed to
                // storage, while the stream is still here to read.
                var estimate = CreditEstimator.ForUpload(file.FileName, file.Content);
                estimatedCredits += estimate.Credits;

                file.Content.Position = 0;
                var writtenBytes = await _storage.WriteAsync(storageKey, file.Content, cancellationToken);
                writtenKeys.Add(storageKey);

                var document = new SourceDocument
                {
                    Id = documentId,
                    WorkspaceId = workspaceId,
                    OriginalFileName = UploadValidator.SanitizeFileName(file.FileName),
                    ContentType = file.ContentType,
                    SizeBytes = writtenBytes,
                    Sha256 = sha256,
                    StorageKey = storageKey,
                    UploadedAtUtc = nowUtc,
                    UploadedByUserId = userId
                };

                _db.SourceDocuments.Add(document);
                acceptedDocuments.Add(document);
                estimatePerDocument[document.Id] = estimate.Credits;

                outcomes.Add(new IntakeFileOutcome(
                    document.OriginalFileName, Accepted: true, null, null));
            }

            if (acceptedDocuments.Count == 0)
            {
                // Nothing usable: no job is created, so the history is not
                // littered with empty jobs the user never meaningfully started.
                return new IntakeResult(null, outcomes);
            }

            var job = new ConversionJob
            {
                Id = Guid.NewGuid(),
                WorkspaceId = workspaceId,
                CreatedByUserId = userId,
                // Queued directly: the bytes are already stored and validated,
                // so there is no remaining validation stage to sit in.
                Status = JobStatus.Queued,
                PresetName = presetName,
                CreatedAtUtc = nowUtc
            };
            _db.ConversionJobs.Add(job);

            foreach (var document in acceptedDocuments)
            {
                _db.ConversionJobItems.Add(new ConversionJobItem
                {
                    Id = Guid.NewGuid(),
                    JobId = job.Id,
                    WorkspaceId = workspaceId,
                    SourceDocumentId = document.Id,
                    Status = JobStatus.Queued,
                    EstimatedCredits = estimatePerDocument[document.Id]
                });
            }

            // The job, its items and its allowance hold commit TOGETHER.
            //
            // Splitting them has a failure on each side: reserving first and
            // then failing to commit leaves a hold against a job that does not
            // exist, which nothing will ever release; committing first and then
            // reserving leaves a claimable job that a worker can start before
            // the allowance was ever checked. One transaction has neither gap.
            //
            // The conditional UPDATE inside TryReserveAsync keeps its guarantee
            // here: it holds the period row until this transaction commits, so
            // simultaneous uploads queue behind each other on that row instead
            // of both reading the same balance.
            await using var transaction = await _db.Database.BeginTransactionAsync(cancellationToken);

            await _db.SaveChangesAsync(cancellationToken);

            var reservation = await _metering.TryReserveAsync(
                workspaceId, job.Id, estimatedCredits, nowUtc, cancellationToken);

            if (!reservation.Granted)
            {
                // Out of allowance. Deliberately NOT an automatic overage
                // (SR-BIL-5): the customer is stopped rather than billed past
                // what they agreed to. Nothing is left behind - the job rolls
                // back and the stored bytes are removed below.
                await transaction.RollbackAsync(cancellationToken);
                await CleanUpAsync(writtenKeys);

                _logger.LogInformation(
                    "Refused job for workspace {WorkspaceId}: {RequestedCredits} credit(s) requested, "
                    + "{AvailableCredits} available",
                    workspaceId, reservation.RequestedCredits, reservation.AvailableCredits);

                return new IntakeResult(
                    null,
                    outcomes.Select(o => o.Accepted
                        ? o with
                        {
                            Accepted = false,
                            RejectionCode = "insufficientCredits",
                            RejectionMessage = reservation.Refusal
                        }
                        : o).ToList());
            }

            await transaction.CommitAsync(cancellationToken);

            _logger.LogInformation(
                "Accepted job {JobId} with {FileCount} file(s) for workspace {WorkspaceId}, "
                + "holding {Credits} credit(s)",
                job.Id, acceptedDocuments.Count, workspaceId, reservation.RequestedCredits);

            return new IntakeResult(job.Id, outcomes);
        }
        catch
        {
            // Bytes are written before the commit, so a failure here would leave
            // orphaned objects that no row references and no retention sweep
            // knows about. Clean them up rather than leaking storage.
            await CleanUpAsync(writtenKeys);
            throw;
        }
    }

    private async Task CleanUpAsync(IReadOnlyList<string> writtenKeys)
    {
        foreach (var key in writtenKeys)
        {
            try
            {
                await _storage.DeleteAsync(key, CancellationToken.None);
            }
            catch (Exception cleanupFailure)
            {
                // Never let cleanup mask the original failure.
                _logger.LogWarning(
                    cleanupFailure, "Failed to clean up orphaned object after a failed intake");
            }
        }
    }
}
