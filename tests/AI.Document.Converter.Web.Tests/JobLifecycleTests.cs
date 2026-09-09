using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.Web.Tests;

// FR-030, FR-037, SR-JOB-4.
[Collection(nameof(PostgresCollection))]
public sealed class JobLifecycleTests
{
    private readonly PostgresFixture _fixture;

    public JobLifecycleTests(PostgresFixture fixture) => _fixture = fixture;

    private static JobLifecycleService Service(ConverterDbContext db) =>
        new(db,
            new MeteringService(db, NullLogger<MeteringService>.Instance),
            NullLogger<JobLifecycleService>.Instance);

    private sealed record Seeded(Guid WorkspaceId, Guid JobId, Dictionary<string, Guid> ItemsByName);

    // Builds a job whose items are in exactly the states a test needs, so each
    // test states its own scenario instead of sharing one vague fixture.
    private static async Task<Seeded> SeedAsync(
        ConverterDbContext db,
        JobStatus jobStatus,
        params (string Name, JobStatus Status, bool Retryable, int Attempts)[] items)
    {
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "w",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = nowUtc
        });

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = jobStatus,
            PresetName = "default",
            CreatedAtUtc = nowUtc
        };
        db.ConversionJobs.Add(job);

        var itemIds = new Dictionary<string, Guid>();

        foreach (var (name, status, retryable, attempts) in items)
        {
            var documentId = Guid.NewGuid();
            db.SourceDocuments.Add(new SourceDocument
            {
                Id = documentId,
                WorkspaceId = workspaceId,
                OriginalFileName = name,
                ContentType = "application/pdf",
                SizeBytes = 1,
                Sha256 = new string('d', 64),
                StorageKey = $"workspaces/{workspaceId:N}/sources/{documentId:N}",
                UploadedAtUtc = nowUtc,
                UploadedByUserId = Guid.NewGuid()
            });

            var itemId = Guid.NewGuid();
            itemIds[name] = itemId;

            db.ConversionJobItems.Add(new ConversionJobItem
            {
                Id = itemId,
                JobId = job.Id,
                WorkspaceId = workspaceId,
                SourceDocumentId = documentId,
                Status = status,
                IsRetryable = retryable,
                AttemptCount = attempts,
                ErrorCategory = status == JobStatus.Failed ? "pythonEngineFailure" : null,
                ErrorMessage = status == JobStatus.Failed ? "engine did not respond" : null
            });
        }

        await db.SaveChangesAsync();
        return new Seeded(workspaceId, job.Id, itemIds);
    }

    // Gives a seeded job an allowance and a hold, the way intake would have left
    // it. Returns the period id so a test can read the balance back.
    private static async Task<Guid> SeedAllowanceAsync(
        ConverterDbContext db, Guid workspaceId, Guid jobId, long included, long held)
    {
        var nowUtc = DateTime.UtcNow;

        var period = new UsagePeriod
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            StartsAtUtc = nowUtc.AddDays(-1),
            EndsAtUtc = nowUtc.AddDays(29),
            PlanCode = "standard@v1",
            IncludedCredits = included,
            ReservedCredits = held,
            CreatedAtUtc = nowUtc
        };
        db.UsagePeriods.Add(period);

        db.UsageReservations.Add(new UsageReservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UsagePeriodId = period.Id,
            JobId = jobId,
            EstimatedCredits = held,
            Status = ReservationStatus.Held,
            PolicyVersion = ConversionCredits.PolicyVersion,
            CreatedAtUtc = nowUtc
        });

        await db.SaveChangesAsync();
        return period.Id;
    }

    // Cancelled work produced nothing, so the customer must not keep paying for
    // it in held allowance they cannot spend elsewhere.
    [Fact]
    public async Task CancellingAJobReturnsItsHold()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(
            db, JobStatus.Queued, ("a.pdf", JobStatus.Queued, true, 0));
        await SeedAllowanceAsync(db, seeded.WorkspaceId, seeded.JobId, included: 100, held: 20);

        await Service(db).CancelAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == seeded.WorkspaceId);
        Assert.Equal(0, period.ReservedCredits);
        Assert.Equal(100, period.AvailableCredits);
    }

    // The first hold was closed when the job finished. Re-running work without
    // taking a new one would let an empty balance keep buying compute.
    [Fact]
    public async Task RetryingTakesAFreshHoldForTheItemsBeingRerun()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(
            db, JobStatus.Failed, ("a.pdf", JobStatus.Failed, true, 1));
        await SeedAllowanceAsync(db, seeded.WorkspaceId, seeded.JobId, included: 100, held: 0);

        // Priced at what the file was quoted at on acceptance.
        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["a.pdf"]);
        item.EstimatedCredits = 4;
        await db.SaveChangesAsync();

        var outcome = await Service(db).RetryFailedAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, outcome.ItemsRequeued);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == seeded.WorkspaceId);
        Assert.Equal(4, period.ReservedCredits);
    }

    // Refusing a retry has to leave the job exactly as it was. Requeuing it
    // anyway would hand a worker work with no allowance behind it.
    [Fact]
    public async Task ARetryThatCannotBeAffordedChangesNothing()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(
            db, JobStatus.Failed, ("a.pdf", JobStatus.Failed, true, 1));
        await SeedAllowanceAsync(db, seeded.WorkspaceId, seeded.JobId, included: 2, held: 0);

        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["a.pdf"]);
        item.EstimatedCredits = 40;
        await db.SaveChangesAsync();

        var outcome = await Service(db).RetryFailedAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, outcome.ItemsRequeued);
        Assert.Contains("40 credit(s)", outcome.Refusal);

        // The rollback has to undo the in-memory requeue as well as the rows.
        db.ChangeTracker.Clear();

        var afterwards = await db.ConversionJobItems.SingleAsync(i => i.Id == item.Id);
        Assert.Equal(JobStatus.Failed, afterwards.Status);

        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);
        Assert.Equal(JobStatus.Failed, job.Status);
    }

    // FR-037: "No new files shall begin processing after cancellation."
    // A Queued item is flipped to Cancelled, which is what stops it - the
    // worker's claim query only takes Queued rows.
    [Fact]
    public async Task CancelPreventsQueuedItemsFromEverStarting()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Queued,
            ("a.pdf", JobStatus.Queued, false, 0),
            ("b.pdf", JobStatus.Queued, false, 0));

        var outcome = await Service(db).CancelAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.True(outcome.Found);
        Assert.Equal(2, outcome.ItemsPrevented);
        Assert.Equal(0, outcome.ItemsAlreadyRunning);

        var statuses = await db.ConversionJobItems
            .Where(i => i.JobId == seeded.JobId).Select(i => i.Status).ToListAsync();
        Assert.All(statuses, status => Assert.Equal(JobStatus.Cancelled, status));
    }

    // An item already being extracted is deliberately NOT flipped here. The
    // worker owns it and discards its own result; reaching in to change its
    // status from outside would race the worker's own write.
    [Fact]
    public async Task CancelLeavesRunningItemsToTheWorkerAndReportsThem()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Extracting,
            ("running.pdf", JobStatus.Extracting, false, 1),
            ("waiting.pdf", JobStatus.Queued, false, 0));

        var outcome = await Service(db).CancelAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, outcome.ItemsPrevented);
        Assert.Equal(1, outcome.ItemsAlreadyRunning);

        var running = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["running.pdf"]);
        Assert.Equal(JobStatus.Extracting, running.Status);

        // The job carries the cancellation, which is what the worker checks
        // before publishing.
        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);
        Assert.NotNull(job.CancelledAtUtc);
        Assert.Equal(JobStatus.Cancelled, job.Status);
    }

    // Clicking cancel just as a job finishes is a no-op, not an error.
    [Fact]
    public async Task CancellingAFinishedJobIsHarmless()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Completed,
            ("done.pdf", JobStatus.Completed, false, 1));

        var outcome = await Service(db).CancelAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.True(outcome.Found);
        Assert.Equal(0, outcome.ItemsPrevented);

        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);
        Assert.Equal(JobStatus.Completed, job.Status);
    }

    [Fact]
    public async Task AnotherTenantCannotCancelThisWorkspacesJob()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Queued, ("a.pdf", JobStatus.Queued, false, 0));

        var outcome = await Service(db).CancelAsync(
            Guid.NewGuid(), seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.False(outcome.Found);

        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);
        Assert.Null(job.CancelledAtUtc);
    }

    // FR-030 and the point of retrying at all: re-running a file that already
    // succeeded would waste the customer's allowance and could replace a good
    // artifact with a worse one if the engine changed in between.
    [Fact]
    public async Task RetryRequeuesOnlyFailedItemsAndLeavesSucceededOnesAlone()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.CompletedWithWarnings,
            ("good.pdf", JobStatus.Completed, false, 1),
            ("bad.pdf", JobStatus.Failed, true, 1));

        var outcome = await Service(db).RetryFailedAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(1, outcome.ItemsRequeued);

        var good = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["good.pdf"]);
        Assert.Equal(JobStatus.Completed, good.Status);

        var bad = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["bad.pdf"]);
        Assert.Equal(JobStatus.Queued, bad.Status);
        Assert.Null(bad.ErrorMessage);

        // Not reset: AttemptCount is what bounds the retries, so clearing it
        // would make the maximum unreachable.
        Assert.Equal(1, bad.AttemptCount);
    }

    // A corrupt document does not become readable on a second look. Offering
    // the retry anyway would charge the customer twice for the same answer.
    [Fact]
    public async Task NonRetryableFailuresAreRefusedRatherThanRequeued()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Failed,
            ("corrupt.pdf", JobStatus.Failed, false, 1));

        var outcome = await Service(db).RetryFailedAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, outcome.ItemsRequeued);
        Assert.NotNull(outcome.Refusal);

        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["corrupt.pdf"]);
        Assert.Equal(JobStatus.Failed, item.Status);
    }

    [Fact]
    public async Task RetryIsRefusedOnceTheAttemptLimitIsReached()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Failed,
            ("stubborn.pdf", JobStatus.Failed, true, JobLifecycleService.MaxAttemptsPerItem));

        var outcome = await Service(db).RetryFailedAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, outcome.ItemsRequeued);
        Assert.NotNull(outcome.Refusal);
    }

    // SR-SEC-6: a deleted job has no content and must not gain any.
    [Fact]
    public async Task RetryIsRefusedForADeletedJob()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Failed,
            ("gone.pdf", JobStatus.Failed, true, 1));

        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);
        job.DeletedAtUtc = DateTime.UtcNow;
        await db.SaveChangesAsync();

        var outcome = await Service(db).RetryFailedAsync(
            seeded.WorkspaceId, seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(0, outcome.ItemsRequeued);
        Assert.Contains("deleted", outcome.Refusal);

        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["gone.pdf"]);
        Assert.Equal(JobStatus.Failed, item.Status);
    }

    [Fact]
    public async Task AnotherTenantCannotRetryThisWorkspacesJob()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedAsync(db, JobStatus.Failed, ("a.pdf", JobStatus.Failed, true, 1));

        var outcome = await Service(db).RetryFailedAsync(
            Guid.NewGuid(), seeded.JobId, DateTime.UtcNow, CancellationToken.None);

        Assert.False(outcome.Found);

        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemsByName["a.pdf"]);
        Assert.Equal(JobStatus.Failed, item.Status);
    }
}
