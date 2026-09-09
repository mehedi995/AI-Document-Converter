using System.Text;
using System.Text.Json;
using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Domain.Exceptions;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Presets;
using AI.Document.Converter.Persistence.Storage;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Worker;

// Converts one claimed job item using the REAL extraction engine - the same
// Python engine and the same Markdown generator the desktop application uses.
// Placeholder output would make every downstream check meaningless.
public sealed class JobProcessor
{
    private readonly ConverterDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly IDocumentProcessorResolver _processorResolver;
    private readonly IMarkdownGenerator _markdownGenerator;
    private readonly IChunkGenerator _chunkGenerator;
    private readonly JobClaimer _claimer;
    private readonly ILogger<JobProcessor> _logger;

    public JobProcessor(
        ConverterDbContext db,
        IObjectStorage storage,
        IDocumentProcessorResolver processorResolver,
        IMarkdownGenerator markdownGenerator,
        IChunkGenerator chunkGenerator,
        JobClaimer claimer,
        ILogger<JobProcessor> logger)
    {
        _db = db;
        _storage = storage;
        _processorResolver = processorResolver;
        _markdownGenerator = markdownGenerator;
        _chunkGenerator = chunkGenerator;
        _claimer = claimer;
        _logger = logger;
    }

    public async Task ProcessAsync(
        Guid itemId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var item = await _db.ConversionJobItems
            .Include(i => i.SourceDocument)
            .Include(i => i.Job)
            .SingleAsync(i => i.Id == itemId, cancellationToken);

        // SR-SEC-6: deletion must prevent a queued or retried item from
        // recreating content. Without this check, a customer could delete a job
        // and then have a worker - one that claimed the item just before the
        // deletion, or one replaying it after a restore - write a brand-new
        // artifact into it. The tombstone outlives the bytes precisely so this
        // check still works after everything else is gone.
        if (StoppedReasonFor(item.Job) is { } stoppedReason)
        {
            _logger.LogInformation(
                "Skipping item {ItemId}: {Reason}, so no work will be done", itemId, stoppedReason);

            item.Status = JobStatus.Cancelled;
            item.LeaseOwner = null;
            item.LeaseExpiresAtUtc = null;
            item.CompletedAtUtc = DateTime.UtcNow;
            await _db.SaveChangesAsync(cancellationToken);
            return;
        }

        // SR-SEC-2: the workspace is reloaded from the persisted row, never
        // taken from whatever handed us this id. A queue payload's tenant id is
        // not authorization, and this is the exact place that rule has to hold
        // for the worker.
        var workspaceId = item.WorkspaceId;
        var document = item.SourceDocument!;

        // The engine takes a file path, so the stored bytes are staged to a temp
        // file. Cleaned up in `finally` including on failure (SEC-004).
        //
        // Staged in a per-item DIRECTORY under the document's own name, rather
        // than as a file named after the item id. The engine records the path it
        // was given as the document's source, and that value ends up in the
        // Markdown front matter the customer downloads - naming the file
        // "3f2a...c1.pdf" put an internal identifier in their metadata instead
        // of their filename. The directory keeps the path unique; the file name
        // keeps the metadata truthful.
        //
        // The name is already sanitized at upload (no separators, no control
        // characters), and it is combined with a directory we control, so it
        // cannot escape the staging root.
        var stagingDirectory = Path.Combine(Path.GetTempPath(), "adc-worker", itemId.ToString("N"));
        var stagingPath = Path.Combine(stagingDirectory, document.OriginalFileName);
        Directory.CreateDirectory(stagingDirectory);

        try
        {
            await using (var source = await _storage.OpenReadAsync(document.StorageKey, cancellationToken))
            {
                if (source is null)
                {
                    await FailAsync(
                        item, "fileNotFound",
                        "The uploaded file is no longer available. It may have passed its retention period.",
                        isRetryable: false, cancellationToken);
                    return;
                }

                await using var staging = new FileStream(stagingPath, FileMode.Create, FileAccess.Write);
                await source.CopyToAsync(staging, cancellationToken);
            }

            DocumentModel extracted;
            try
            {
                var processor = _processorResolver.Resolve(stagingPath);
                extracted = await processor.ExtractAsync(stagingPath, cancellationToken);
            }
            catch (DocumentConversionException ex)
            {
                await FailAsync(
                    item, ex.Category.ToString(), ex.Message, IsRetryable(ex.Category), cancellationToken);
                return;
            }

            var markdown = _markdownGenerator.Generate(extracted);

            // Chunked from the SAME DocumentModel the Markdown came from, in
            // the same pass. The desktop re-extracts for chunking (audit D-05),
            // which doubles the engine cost per document - unacceptable when
            // that compute is metered.
            var preset = ConversionPresets.Resolve(item.Job?.PresetName);
            ChunkGenerationResult? chunkResult = null;

            if (preset.GenerateChunks)
            {
                chunkResult = await _chunkGenerator.GenerateChunksAsync(
                    extracted,
                    preset.ToChunkOptions(),
                    document.OriginalFileName,
                    cancellationToken);
            }

            // Lease check BEFORE publishing. If our lease expired mid-extraction
            // another worker may already be redoing this item; publishing now
            // would produce two artifacts for one item and, later, two charges.
            // Delivery is at-least-once, so this check is what keeps the RESULT
            // single (SR-JOB-2).
            if (!await _claimer.TryRenewLeaseAsync(itemId, leaseOwner, leaseDuration, cancellationToken))
            {
                _logger.LogWarning(
                    "Abandoning item {ItemId}: lease lost during extraction, another worker owns it", itemId);
                return;
            }

            // The renewal above is raw SQL, which bumps the row's xmin. The
            // tracked entity still holds the version it was loaded with, so the
            // next SaveChanges would fail its concurrency check against a change
            // this same worker made. Reloading refreshes the version. Nothing
            // has been modified on the entity yet, so nothing is lost.
            await _db.Entry(item).ReloadAsync(cancellationToken);

            // Checked again here, not only at the start: extraction takes real
            // time, and a customer can delete the job while it is running. The
            // first check stops work that never should have begun; this one
            // stops a result from landing in a job the customer already
            // believes is gone.
            await _db.Entry(item).Reference(i => i.Job).LoadAsync(cancellationToken);
            if (StoppedReasonFor(item.Job) is { } lateReason)
            {
                // FR-037: an item already running when the user cancels is
                // allowed to finish extracting, but its RESULT is discarded.
                // Killing the process mid-write is how a half-published
                // artifact happens (SR-JOB-3); discarding a finished result
                // costs only the work already done.
                _logger.LogInformation(
                    "Discarding result for item {ItemId}: {Reason} during extraction", itemId, lateReason);
                return;
            }

            await PublishAsync(
                item, workspaceId, document, markdown, chunkResult, extracted, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never let an unexpected fault leave the item leased and silently
            // stuck; it becomes a categorized failure the user can see.
            _logger.LogError(ex, "Unexpected failure processing item {ItemId}", itemId);
            await FailAsync(
                item, ErrorCategory.UnexpectedException.ToString(),
                "An unexpected error occurred while converting this file.",
                isRetryable: true, CancellationToken.None);
        }
        finally
        {
            TryDeleteStagingDirectory(stagingDirectory);
        }
    }

    // The two states that mean "stop, and do not produce anything". Checked
    // both before starting and again before publishing, because extraction
    // takes real time and either can happen while it runs.
    private static string? StoppedReasonFor(ConversionJob? job) => job switch
    {
        null => null,
        { DeletedAtUtc: not null } => "its job was deleted",
        { CancelledAtUtc: not null } => "its job was cancelled",
        _ => null
    };

    private async Task PublishAsync(
        ConversionJobItem item,
        Guid workspaceId,
        SourceDocument document,
        string markdown,
        ChunkGenerationResult? chunkResult,
        DocumentModel extracted,
        CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var artifactId = Guid.NewGuid();
        var storageKey = StorageKeys.ForArtifact(workspaceId, artifactId);

        // Bytes first, row second. The Artifact row is what makes an output
        // downloadable, so it must not exist until the bytes are fully written
        // (SR-JOB-3). The reverse order would expose a truncated download.
        var bytes = Encoding.UTF8.GetBytes(markdown);
        await using (var content = new MemoryStream(bytes))
        {
            await _storage.WriteAsync(storageKey, content, cancellationToken);
        }

        _db.Artifacts.Add(new Artifact
        {
            Id = artifactId,
            JobItemId = item.Id,
            WorkspaceId = workspaceId,
            Kind = ArtifactKind.Markdown,
            StorageKey = storageKey,
            FileName = Path.GetFileNameWithoutExtension(document.OriginalFileName) + ".md",
            SizeBytes = bytes.LongLength,
            CreatedAtUtc = nowUtc
        });

        if (chunkResult is { Chunks.Count: > 0 })
        {
            await PublishChunkSetAsync(item, workspaceId, document, chunkResult, nowUtc, cancellationToken);
        }

        // Extraction warnings AND chunking warnings. An oversized table is only
        // discoverable once a chunk size is known, so it cannot come from
        // extraction - but to the customer it is the same kind of fact about
        // their document, and belongs in the same list.
        var allWarnings = chunkResult is null
            ? extracted.Warnings.AsEnumerable()
            : extracted.Warnings.Concat(chunkResult.Warnings);

        foreach (var warning in allWarnings)
        {
            _db.JobItemWarnings.Add(new JobItemWarning
            {
                Id = Guid.NewGuid(),
                JobItemId = item.Id,
                WorkspaceId = workspaceId,
                Code = ToCamelCase(warning.Code.ToString()),
                Severity = ToCamelCase(warning.Severity.ToString()),
                Message = warning.Message,
                BlockId = warning.BlockId,
                PageNumber = warning.Location?.PageNumber,
                SlideNumber = warning.Location?.SlideNumber,
                SheetName = warning.Location?.SheetName,
                DetailsJson = warning.Details is null ? null : JsonSerializer.Serialize(warning.Details)
            });
        }

        // The distinction the whole warnings channel exists for: an incomplete
        // extraction is NOT a plain success (SR-INT-1, SR-INT-3).
        // Only Error severity means content was not recovered. An oversized
        // table is a Warning - the table is intact, the chunk is just large -
        // so it must NOT demote the item, or the distinction stops meaning
        // anything.
        item.Status = allWarnings.Any(w => w.Severity == WarningSeverity.Error)
            ? JobStatus.CompletedWithWarnings
            : JobStatus.Completed;
        item.CompletedAtUtc = nowUtc;
        item.LeaseOwner = null;
        item.LeaseExpiresAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);
        await RollUpJobStatusAsync(item.JobId, cancellationToken);

        _logger.LogInformation(
            "Published item {ItemId} as {Status} with {WarningCount} warning(s)",
            item.Id, item.Status, extracted.Warnings.Count);
    }

    // Chunks are stored as ONE JSON object rather than one object per chunk.
    // A 200-page document can produce hundreds of chunks; a row and a stored
    // object each would swamp the artifact table and the object store for no
    // benefit. The JSON also carries FR-021's per-chunk metadata (sequence,
    // token count, overlap) naturally, which a folder of .md files cannot.
    // The export expands it back into individual files.
    private async Task PublishChunkSetAsync(
        ConversionJobItem item,
        Guid workspaceId,
        SourceDocument document,
        ChunkGenerationResult chunkResult,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var chunkSet = new
        {
            sourceFileName = document.OriginalFileName,
            chunkCount = chunkResult.Chunks.Count,
            chunks = chunkResult.Chunks.Select(c => new
            {
                sequenceNumber = c.SequenceNumber,
                sourceFileName = c.SourceFileName,
                tokenCount = c.TokenCount,
                overlapTokens = c.OverlapTokens,
                content = c.Content
            })
        };

        var artifactId = Guid.NewGuid();
        var storageKey = StorageKeys.ForArtifact(workspaceId, artifactId);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(chunkSet);

        await using (var content = new MemoryStream(bytes))
        {
            await _storage.WriteAsync(storageKey, content, cancellationToken);
        }

        _db.Artifacts.Add(new Artifact
        {
            Id = artifactId,
            JobItemId = item.Id,
            WorkspaceId = workspaceId,
            Kind = ArtifactKind.ChunkSet,
            StorageKey = storageKey,
            FileName = Path.GetFileNameWithoutExtension(document.OriginalFileName) + ".chunks.json",
            SizeBytes = bytes.LongLength,
            CreatedAtUtc = nowUtc
        });
    }

    private async Task FailAsync(
        ConversionJobItem item, string category, string message, bool isRetryable,
        CancellationToken cancellationToken)
    {
        // Reload first. This runs on the failure path, where the entity may be
        // stale for reasons unrelated to the failure being recorded - and a
        // concurrency exception thrown while recording an error would hide the
        // original error entirely, which is how a bug becomes unexplainable.
        await _db.Entry(item).ReloadAsync(cancellationToken);

        item.Status = JobStatus.Failed;
        item.ErrorCategory = category;
        item.ErrorMessage = message;
        item.IsRetryable = isRetryable;
        item.CompletedAtUtc = DateTime.UtcNow;
        item.LeaseOwner = null;
        item.LeaseExpiresAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);
        await RollUpJobStatusAsync(item.JobId, cancellationToken);

        _logger.LogWarning("Item {ItemId} failed: {Category}", item.Id, category);
    }

    // A job is finished only when every one of its items is. BR-006: one failed
    // file does not fail the job, and one incomplete file does not let the job
    // claim a clean success.
    //
    // Two workers finishing the last two items of a job will both reach here at
    // once and both try to write the job row. That is precisely the collision
    // ConversionJob.Version (PostgreSQL xmin) exists to catch - but catching it
    // is only useful if it is then handled. The loser reloads and re-evaluates
    // rather than failing: by that point the winner has committed, so the
    // second pass sees the complete picture and usually finds the job already
    // in its correct state.
    private async Task RollUpJobStatusAsync(Guid jobId, CancellationToken cancellationToken)
    {
        const int maxAttempts = 3;

        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            var job = await _db.ConversionJobs
                .Include(j => j.Items)
                .AsTracking()
                .SingleAsync(j => j.Id == jobId, cancellationToken);

            if (job.Items.Any(i => !i.Status.IsTerminal()))
            {
                return;
            }

            var hasFailure = job.Items.Any(i => i.Status == JobStatus.Failed);
            var hasWarnings = job.Items.Any(i => i.Status == JobStatus.CompletedWithWarnings);
            var allFailed = job.Items.All(i => i.Status == JobStatus.Failed);

            var rolledUpStatus = allFailed
                ? JobStatus.Failed
                : hasFailure || hasWarnings
                    ? JobStatus.CompletedWithWarnings
                    : JobStatus.Completed;

            if (job.Status == rolledUpStatus && job.CompletedAtUtc is not null)
            {
                // The other worker already wrote exactly this. Nothing to do -
                // and importantly, not an error.
                return;
            }

            job.Status = rolledUpStatus;
            job.CompletedAtUtc = DateTime.UtcNow;

            try
            {
                await _db.SaveChangesAsync(cancellationToken);
                return;
            }
            catch (DbUpdateConcurrencyException) when (attempt < maxAttempts)
            {
                // Someone committed between our read and our write. Drop the
                // stale tracked state and look again.
                foreach (var entry in _db.ChangeTracker.Entries().ToList())
                {
                    await entry.ReloadAsync(cancellationToken);
                }

                _logger.LogDebug(
                    "Job {JobId} roll-up lost a concurrency race on attempt {Attempt}; retrying",
                    jobId, attempt);
            }
        }

        _logger.LogWarning(
            "Job {JobId} roll-up could not commit after {MaxAttempts} attempts. Item statuses are "
            + "correct; the job summary will be corrected by the next item to finish or by "
            + "reconciliation.", jobId, maxAttempts);
    }

    // A transient condition is worth another attempt; a malformed document will
    // fail identically every time and retrying it just burns the customer's
    // allowance (audit D-07).
    private static bool IsRetryable(ErrorCategory category) => category switch
    {
        ErrorCategory.FileLocked => true,
        ErrorCategory.PythonEngineFailure => true,
        ErrorCategory.UnexpectedException => true,
        _ => false
    };

    private static string ToCamelCase(string value) =>
        string.IsNullOrEmpty(value) ? value : char.ToLowerInvariant(value[0]) + value[1..];

    private void TryDeleteStagingDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, recursive: true);
            }
        }
        catch (IOException ex)
        {
            // Worth knowing about - temp files that accumulate eventually fill
            // the disk - but not worth failing a completed conversion over.
            _logger.LogWarning(ex, "Could not delete the staging directory for a processed item");
        }
    }
}
