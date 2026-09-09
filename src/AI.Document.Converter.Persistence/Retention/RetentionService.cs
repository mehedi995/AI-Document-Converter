using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Persistence.Retention;

public sealed record SweepReport(int SourcePurged, int OutputPurged, int JobsExpired)
{
    public bool DidWork => SourcePurged > 0 || OutputPurged > 0 || JobsExpired > 0;
}

// Enforces the retention the customer was shown before uploading (SR-SEC-6).
//
// Ordering matters throughout: BYTES ARE DELETED BEFORE THE ROW IS MARKED.
// Marking first would mean a crash in between leaves a row that claims the
// content is gone while the content is still sitting in storage - the one
// outcome that turns a deletion promise into a false statement. Doing it the
// other way round leaves at worst a re-attempted delete, which is harmless
// because IObjectStorage.DeleteAsync is idempotent.
public sealed class RetentionService
{
    private readonly ConverterDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly RetentionPolicy _policy;
    private readonly ILogger<RetentionService> _logger;

    public RetentionService(
        ConverterDbContext db,
        IObjectStorage storage,
        IOptions<RetentionPolicy> policy,
        ILogger<RetentionService> logger)
    {
        _db = db;
        _storage = storage;
        _policy = policy.Value;
        _logger = logger;
    }

    public async Task<SweepReport> SweepAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        var sourcePurged = await PurgeExpiredSourceBytesAsync(nowUtc, cancellationToken);
        var outputPurged = await PurgeExpiredOutputBytesAsync(nowUtc, cancellationToken);
        var jobsExpired = await MarkFullyPurgedJobsExpiredAsync(cancellationToken);

        var report = new SweepReport(sourcePurged, outputPurged, jobsExpired);

        if (report.DidWork)
        {
            // Counts only - never filenames, never workspace-identifying detail
            // beyond ids (FR-035, SR-SEC-8).
            _logger.LogInformation(
                "Retention sweep purged {SourceCount} source and {OutputCount} output object(s); "
                + "{ExpiredCount} job(s) marked expired",
                report.SourcePurged, report.OutputPurged, report.JobsExpired);
        }

        return report;
    }

    private async Task<int> PurgeExpiredSourceBytesAsync(
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - _policy.SourceBytes;

        var expired = await _db.SourceDocuments
            .Where(d => d.SourceBytesDeletedAtUtc == null && d.UploadedAtUtc < cutoff)
            .OrderBy(d => d.UploadedAtUtc)
            .Take(_policy.MaxJobsPerSweep)
            .ToListAsync(cancellationToken);

        foreach (var document in expired)
        {
            await _storage.DeleteAsync(document.StorageKey, cancellationToken);
            document.SourceBytesDeletedAtUtc = nowUtc;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return expired.Count;
    }

    private async Task<int> PurgeExpiredOutputBytesAsync(
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - _policy.OutputBytes;

        var expired = await _db.Artifacts
            .Where(a => a.BytesDeletedAtUtc == null && a.CreatedAtUtc < cutoff)
            .OrderBy(a => a.CreatedAtUtc)
            .Take(_policy.MaxJobsPerSweep)
            .ToListAsync(cancellationToken);

        foreach (var artifact in expired)
        {
            await _storage.DeleteAsync(artifact.StorageKey, cancellationToken);
            artifact.BytesDeletedAtUtc = nowUtc;
        }

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return expired.Count;
    }

    // Once every byte a job produced is gone, its status becomes Expired so the
    // UI stops offering downloads that would only 404. The job row itself stays
    // - the customer's history, and any future billing record, must survive the
    // content.
    private async Task<int> MarkFullyPurgedJobsExpiredAsync(CancellationToken cancellationToken)
    {
        var candidates = await _db.ConversionJobs
            .Where(j => (j.Status == JobStatus.Completed || j.Status == JobStatus.CompletedWithWarnings)
                        && j.Items.Any(i => i.Artifacts.Any())
                        && j.Items.All(i => i.Artifacts.All(a => a.BytesDeletedAtUtc != null)))
            .Take(_policy.MaxJobsPerSweep)
            .ToListAsync(cancellationToken);

        foreach (var job in candidates)
        {
            job.Status = JobStatus.Expired;
        }

        if (candidates.Count > 0)
        {
            await _db.SaveChangesAsync(cancellationToken);
        }

        return candidates.Count;
    }

    // Customer-initiated deletion. Immediate, and it applies the tombstone
    // FIRST so that a queued or retried item cannot publish a fresh artifact
    // into a job the customer has just deleted (SR-SEC-6).
    //
    // Scoped by workspace, not just job id: this is a destructive operation and
    // must not be reachable by changing a number in a URL (SR-SEC-2).
    public async Task<bool> DeleteJobContentAsync(
        Guid workspaceId, Guid jobId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var job = await _db.ConversionJobs
            .Where(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            .Include(j => j.Items).ThenInclude(i => i.Artifacts)
            .Include(j => j.Items).ThenInclude(i => i.SourceDocument)
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return false;
        }

        // Tombstone before bytes. If this crashes half way, the job is already
        // marked deleted, so nothing will republish into it and the next sweep
        // finishes removing whatever is left. The reverse order could delete
        // the content and then let a retry recreate it.
        job.DeletedAtUtc = nowUtc;
        job.Status = JobStatus.Expired;
        await _db.SaveChangesAsync(cancellationToken);

        foreach (var item in job.Items)
        {
            foreach (var artifact in item.Artifacts.Where(a => a.BytesDeletedAtUtc == null))
            {
                await _storage.DeleteAsync(artifact.StorageKey, cancellationToken);
                artifact.BytesDeletedAtUtc = nowUtc;
            }

            var document = item.SourceDocument;
            if (document is not null && document.SourceBytesDeletedAtUtc == null)
            {
                await _storage.DeleteAsync(document.StorageKey, cancellationToken);
                document.SourceBytesDeletedAtUtc = nowUtc;
            }
        }

        job.SourceBytesPurgedAtUtc = nowUtc;
        job.OutputBytesPurgedAtUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation("Job {JobId} content deleted at customer request", jobId);
        return true;
    }
}
