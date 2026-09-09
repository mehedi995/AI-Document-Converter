using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Persistence.Operations;

public sealed record QueueHealth(
    int Queued,
    int Running,
    int Failed,
    int Retryable,
    TimeSpan? OldestQueuedAge,
    int ExpiredLeases,
    int AtAttemptLimit);

public sealed record OperatorJobRow(
    Guid JobId,
    string WorkspaceSlug,
    Guid WorkspaceId,
    JobStatus Status,
    int ItemCount,
    int FailedItems,
    int WarningItems,
    int MaxAttempts,
    string? ErrorCategories,
    DateTime CreatedAtUtc,
    DateTime? CompletedAtUtc,
    bool IsDeleted);

public sealed record WorkspaceUsageRow(
    Guid WorkspaceId,
    string Slug,
    string PlanCode,
    long IncludedCredits,
    long ReservedCredits,
    long SettledCredits,
    DateTime PeriodEndsAtUtc);

public sealed record RetentionPosture(
    int SourcesAwaitingDeletion,
    int SourcesOverdue,
    int ArtifactsAwaitingDeletion,
    int ArtifactsOverdue,
    int DeletedJobsWithLiveBytes,
    string PolicyDescription);

// What an operator can see WITHOUT looking at anybody's documents.
//
// SR-SEC-7: "Operator status alone must not reveal documents." So nothing here
// returns a filename, a document's text, a warning message, or an error message
// - every one of those can carry customer content. A filename like
// "Q3-redundancy-list.xlsx" tells you something the customer did not agree to
// share with support, and an error message can quote the text it choked on.
//
// What is left is deliberately enough to run the service: how much work is
// queued, what is stuck, what is failing and in which CATEGORY, whether
// retention is keeping up, and what each workspace is consuming. Diagnosing an
// outage does not require reading anyone's documents, and the console is shaped
// so that it cannot.
//
// Anything narrower than this - the filenames on one job - goes through
// InspectJobAsync, which demands a reason and writes an audit entry.
public sealed class OperatorConsoleService
{
    private readonly ConverterDbContext _db;
    private readonly RetentionPolicy _retention;

    public OperatorConsoleService(ConverterDbContext db, IOptions<RetentionPolicy> retention)
    {
        _db = db;
        _retention = retention.Value;
    }

    public async Task<QueueHealth> GetQueueHealthAsync(DateTime nowUtc, CancellationToken cancellationToken)
    {
        // One pass over the item table rather than six count queries. The
        // operator console is the page somebody opens WHILE the system is
        // struggling, so it must not add six scans to a database already under
        // load.
        var byStatus = await _db.ConversionJobItems
            .AsNoTracking()
            .GroupBy(i => i.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync(cancellationToken);

        int CountOf(params JobStatus[] statuses) =>
            byStatus.Where(s => statuses.Contains(s.Status)).Sum(s => s.Count);

        var oldestQueuedCreatedAt = await _db.ConversionJobItems
            .AsNoTracking()
            .Where(i => i.Status == JobStatus.Queued)
            .Join(_db.ConversionJobs, i => i.JobId, j => j.Id, (i, j) => j.CreatedAtUtc)
            .OrderBy(createdAt => createdAt)
            .Cast<DateTime?>()
            .FirstOrDefaultAsync(cancellationToken);

        // A lease that expired without the item finishing means a worker died
        // holding it. The item is claimable again, so this is not data loss -
        // but a rising number here is the earliest visible sign that workers
        // are crashing.
        var expiredLeases = await _db.ConversionJobItems
            .AsNoTracking()
            .CountAsync(
                i => i.LeaseExpiresAtUtc != null
                     && i.LeaseExpiresAtUtc < nowUtc
                     && i.Status != JobStatus.Completed
                     && i.Status != JobStatus.CompletedWithWarnings
                     && i.Status != JobStatus.Failed
                     && i.Status != JobStatus.Cancelled
                     && i.Status != JobStatus.Expired,
                cancellationToken);

        var atAttemptLimit = await _db.ConversionJobItems
            .AsNoTracking()
            .CountAsync(i => i.Status == JobStatus.Failed && i.AttemptCount >= 3, cancellationToken);

        var retryable = await _db.ConversionJobItems
            .AsNoTracking()
            .CountAsync(i => i.Status == JobStatus.Failed && i.IsRetryable && i.AttemptCount < 3, cancellationToken);

        return new QueueHealth(
            Queued: CountOf(JobStatus.Queued, JobStatus.Validating),
            Running: CountOf(
                JobStatus.Extracting, JobStatus.Formatting, JobStatus.Chunking, JobStatus.Exporting),
            Failed: CountOf(JobStatus.Failed),
            Retryable: retryable,
            OldestQueuedAge: oldestQueuedCreatedAt is { } createdAt ? nowUtc - createdAt : null,
            ExpiredLeases: expiredLeases,
            AtAttemptLimit: atAttemptLimit);
    }

    // Jobs across every workspace. No filenames, no messages - see the class
    // comment. Error CATEGORIES are included because they are a fixed
    // vocabulary the system produces, not text derived from a document.
    public async Task<IReadOnlyList<OperatorJobRow>> GetRecentJobsAsync(
        JobStatus? statusFilter, Guid? workspaceFilter, int take, CancellationToken cancellationToken)
    {
        var query = _db.ConversionJobs.AsNoTracking();

        if (statusFilter is { } status)
        {
            query = query.Where(j => j.Status == status);
        }

        if (workspaceFilter is { } workspaceId)
        {
            query = query.Where(j => j.WorkspaceId == workspaceId);
        }

        return await query
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(take)
            .Select(j => new OperatorJobRow(
                j.Id,
                j.Workspace!.Slug,
                j.WorkspaceId,
                j.Status,
                j.Items.Count,
                j.Items.Count(i => i.Status == JobStatus.Failed),
                j.Items.Count(i => i.Status == JobStatus.CompletedWithWarnings),
                j.Items.Max(i => (int?)i.AttemptCount) ?? 0,
                // Categories only, joined. Never ErrorMessage, which can quote
                // the document.
                string.Join(", ", j.Items
                    .Where(i => i.ErrorCategory != null)
                    .Select(i => i.ErrorCategory!)
                    .Distinct()),
                j.CreatedAtUtc,
                j.CompletedAtUtc,
                j.DeletedAtUtc != null))
            .ToListAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<WorkspaceUsageRow>> GetWorkspaceUsageAsync(
        DateTime nowUtc, int take, CancellationToken cancellationToken) =>
        await _db.UsagePeriods
            .AsNoTracking()
            .Where(p => p.StartsAtUtc <= nowUtc && p.EndsAtUtc > nowUtc)
            .OrderByDescending(p => p.SettledCredits)
            .Take(take)
            .Select(p => new WorkspaceUsageRow(
                p.WorkspaceId,
                _db.Workspaces.Where(w => w.Id == p.WorkspaceId).Select(w => w.Slug).First(),
                p.PlanCode,
                p.IncludedCredits,
                p.ReservedCredits,
                p.SettledCredits,
                p.EndsAtUtc))
            .ToListAsync(cancellationToken);

    // Whether retention is actually keeping its promise. "Overdue" is the
    // number that matters: bytes past their deletion time are a broken promise
    // to the customer (SR-SEC-6), and without this nobody would notice a
    // wedged sweep until someone asked.
    public async Task<RetentionPosture> GetRetentionPostureAsync(
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var sourceDeadline = nowUtc - _retention.SourceBytes;
        var outputDeadline = nowUtc - _retention.OutputBytes;

        // A grace window before calling something overdue. A sweep runs on an
        // interval, so an object one second past its deadline is normal
        // operation, not a failure - flagging it would make the number cry wolf
        // every few minutes.
        var overdueGrace = _retention.SweepInterval + TimeSpan.FromMinutes(5);

        var sourcesAwaiting = await _db.SourceDocuments
            .AsNoTracking()
            .CountAsync(d => d.SourceBytesDeletedAtUtc == null, cancellationToken);

        var sourcesOverdue = await _db.SourceDocuments
            .AsNoTracking()
            .CountAsync(
                d => d.SourceBytesDeletedAtUtc == null && d.UploadedAtUtc < sourceDeadline - overdueGrace,
                cancellationToken);

        var artifactsAwaiting = await _db.Artifacts
            .AsNoTracking()
            .CountAsync(a => a.BytesDeletedAtUtc == null, cancellationToken);

        var artifactsOverdue = await _db.Artifacts
            .AsNoTracking()
            .CountAsync(
                a => a.BytesDeletedAtUtc == null && a.CreatedAtUtc < outputDeadline - overdueGrace,
                cancellationToken);

        // The one that would be a real incident: a customer deleted a job and
        // its bytes are still on disk.
        var deletedJobsWithLiveBytes = await _db.Artifacts
            .AsNoTracking()
            .CountAsync(
                a => a.BytesDeletedAtUtc == null
                     && _db.ConversionJobItems
                         .Any(i => i.Id == a.JobItemId
                                   && _db.ConversionJobs.Any(j => j.Id == i.JobId && j.DeletedAtUtc != null)),
                cancellationToken);

        return new RetentionPosture(
            sourcesAwaiting,
            sourcesOverdue,
            artifactsAwaiting,
            artifactsOverdue,
            deletedJobsWithLiveBytes,
            _retention.DescribeForCustomer());
    }
}
