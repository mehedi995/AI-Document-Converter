using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Persistence.Jobs;

public sealed record CancelOutcome(bool Found, int ItemsPrevented, int ItemsAlreadyRunning);

public sealed record RetryOutcome(bool Found, int ItemsRequeued, int ItemsLeftAlone, string? Refusal);

// Cancel and retry (FR-030, FR-037, SR-JOB-4).
//
// Both operations are scoped by workspace, not just job id: they change other
// people's work if you get the boundary wrong, so neither may be reachable by
// editing a number in a URL (SR-SEC-2).
public sealed class JobLifecycleService
{
    // Bounds retries so a document that fails identically every time cannot be
    // resubmitted forever, burning the customer's allowance each round
    // (SR-JOB-2).
    public const int MaxAttemptsPerItem = 3;

    private readonly ConverterDbContext _db;
    private readonly ILogger<JobLifecycleService> _logger;

    public JobLifecycleService(ConverterDbContext db, ILogger<JobLifecycleService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // FR-037: "No new files shall begin processing after cancellation; any file
    // already in progress may complete or be stopped safely."
    //
    // Queued items are flipped to Cancelled here and now, which is what stops
    // them: the worker's claim query only takes Queued rows, so a cancelled one
    // is simply never picked up. Items already running are left to the worker,
    // which checks CancelledAtUtc before publishing and discards its result -
    // killing a running extraction mid-write is how a half-published artifact
    // happens (SR-JOB-3).
    public async Task<CancelOutcome> CancelAsync(
        Guid workspaceId, Guid jobId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var job = await _db.ConversionJobs
            .Where(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            .Include(j => j.Items)
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return new CancelOutcome(false, 0, 0);
        }

        // Cancelling something already finished is a no-op, not an error: the
        // user may simply have clicked just as it completed.
        if (job.Status.IsTerminal())
        {
            return new CancelOutcome(true, 0, 0);
        }

        job.CancelledAtUtc = nowUtc;
        job.Status = JobStatus.Cancelled;

        var prevented = 0;
        var running = 0;

        foreach (var item in job.Items.Where(i => !i.Status.IsTerminal()))
        {
            if (item.Status == JobStatus.Queued)
            {
                item.Status = JobStatus.Cancelled;
                item.CompletedAtUtc = nowUtc;
                prevented++;
            }
            else
            {
                // In flight. Left alone deliberately - the worker owns it and
                // will discard its own result.
                running++;
            }
        }

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job {JobId} cancelled: {Prevented} item(s) prevented from starting, {Running} already running",
            jobId, prevented, running);

        return new CancelOutcome(true, prevented, running);
    }

    // FR-030. Retries only what failed. Items that already succeeded are left
    // completely untouched - re-extracting them would waste the customer's
    // allowance and could replace a good artifact with a worse one if the
    // engine has changed in between.
    public async Task<RetryOutcome> RetryFailedAsync(
        Guid workspaceId, Guid jobId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var job = await _db.ConversionJobs
            .Where(j => j.Id == jobId && j.WorkspaceId == workspaceId)
            .Include(j => j.Items)
            .SingleOrDefaultAsync(cancellationToken);

        if (job is null)
        {
            return new RetryOutcome(false, 0, 0, null);
        }

        // A deleted job has no content and must not gain any (SR-SEC-6).
        if (job.DeletedAtUtc is not null)
        {
            return new RetryOutcome(true, 0, 0, "This conversion's files have been deleted.");
        }

        var failed = job.Items.Where(i => i.Status == JobStatus.Failed).ToList();

        if (failed.Count == 0)
        {
            return new RetryOutcome(true, 0, job.Items.Count, "Nothing in this conversion failed.");
        }

        var requeued = 0;
        var exhausted = 0;

        foreach (var item in failed)
        {
            // A non-retryable failure is one that will fail the same way again
            // - a corrupt document does not become readable on a second look.
            // Offering the retry anyway would just charge the customer twice
            // for the same answer (audit D-07).
            if (!item.IsRetryable)
            {
                exhausted++;
                continue;
            }

            if (item.AttemptCount >= MaxAttemptsPerItem)
            {
                exhausted++;
                continue;
            }

            item.Status = JobStatus.Queued;
            item.ErrorCategory = null;
            item.ErrorMessage = null;
            item.CompletedAtUtc = null;
            item.LeaseOwner = null;
            item.LeaseExpiresAtUtc = null;
            // AttemptCount is deliberately NOT reset. It is what bounds the
            // retries; clearing it would make MaxAttemptsPerItem unreachable.
            requeued++;
        }

        if (requeued == 0)
        {
            return new RetryOutcome(
                true, 0, exhausted,
                "Those files cannot be retried - they either failed for a reason that will not "
                + "change, or have already been attempted the maximum number of times.");
        }

        // Back to Queued so the worker picks it up and so the UI stops showing
        // a finished job that is about to change.
        job.Status = JobStatus.Queued;
        job.CompletedAtUtc = null;

        await _db.SaveChangesAsync(cancellationToken);

        _logger.LogInformation(
            "Job {JobId} retry: {Requeued} item(s) requeued, {Exhausted} not retryable", jobId, requeued, exhausted);

        return new RetryOutcome(true, requeued, exhausted, null);
    }
}
