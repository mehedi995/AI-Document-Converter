using System.Security.Cryptography;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;

namespace AI.Document.Converter.Web.Services;

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
    private readonly ILogger<ConversionIntakeService> _logger;

    public ConversionIntakeService(
        ConverterDbContext db,
        IObjectStorage storage,
        UploadValidator validator,
        ILogger<ConversionIntakeService> logger)
    {
        _db = db;
        _storage = storage;
        _validator = validator;
        _logger = logger;
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
                    Status = JobStatus.Queued
                });
            }

            // The single commit that makes the work exist and makes it
            // claimable, at the same instant.
            await _db.SaveChangesAsync(cancellationToken);

            _logger.LogInformation(
                "Accepted job {JobId} with {FileCount} file(s) for workspace {WorkspaceId}",
                job.Id, acceptedDocuments.Count, workspaceId);

            return new IntakeResult(job.Id, outcomes);
        }
        catch
        {
            // Bytes are written before the commit, so a failure here would leave
            // orphaned objects that no row references and no retention sweep
            // knows about. Clean them up rather than leaking storage.
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

            throw;
        }
    }
}
