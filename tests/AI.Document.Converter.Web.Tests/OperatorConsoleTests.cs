using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Operations;
using AI.Document.Converter.Persistence.Retention;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// SaaS 5.11 and SR-SEC-7.
//
// The tests that matter here are the REFUSALS. An operator console that shows
// the right numbers but also leaks filenames has failed at the thing it is most
// likely to be judged on, so most of this file is about what must not appear
// and what must be recorded before anything does.
[Collection(nameof(PostgresCollection))]
public sealed class OperatorConsoleTests
{
    private readonly PostgresFixture _fixture;

    public OperatorConsoleTests(PostgresFixture fixture) => _fixture = fixture;

    private static OperatorConsoleService Console(ConverterDbContext db) =>
        new(db, Options.Create(new RetentionPolicy()));

    private static OperatorInspectionService Inspection(ConverterDbContext db) =>
        new(db, NullLogger<OperatorInspectionService>.Instance);

    private sealed record Seeded(Guid WorkspaceId, Guid JobId, Guid ItemId);

    private static async Task<Seeded> SeedJobAsync(
        ConverterDbContext db,
        JobStatus itemStatus,
        string fileName = "quarterly-redundancies.xlsx",
        string? errorCategory = null,
        string? errorMessage = null,
        DateTime? uploadedAtUtc = null)
    {
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();
        var documentId = Guid.NewGuid();
        var jobId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId, Name = "w", Slug = $"ws-{Guid.NewGuid():N}"[..15], CreatedAtUtc = nowUtc
        });

        db.SourceDocuments.Add(new SourceDocument
        {
            Id = documentId,
            WorkspaceId = workspaceId,
            OriginalFileName = fileName,
            ContentType = "application/vnd.ms-excel",
            SizeBytes = 4096,
            Sha256 = new string('a', 64),
            StorageKey = $"workspaces/{workspaceId:N}/sources/{documentId:N}",
            UploadedAtUtc = uploadedAtUtc ?? nowUtc,
            UploadedByUserId = userId
        });

        db.ConversionJobs.Add(new ConversionJob
        {
            Id = jobId,
            WorkspaceId = workspaceId,
            CreatedByUserId = userId,
            Status = JobStatus.Queued,
            PresetName = "default",
            CreatedAtUtc = nowUtc
        });

        db.ConversionJobItems.Add(new ConversionJobItem
        {
            Id = itemId,
            JobId = jobId,
            WorkspaceId = workspaceId,
            SourceDocumentId = documentId,
            Status = itemStatus,
            ErrorCategory = errorCategory,
            ErrorMessage = errorMessage
        });

        await db.SaveChangesAsync();
        return new Seeded(workspaceId, jobId, itemId);
    }

    // SR-SEC-7: "Operator status alone must not reveal documents." A filename
    // like "quarterly-redundancies.xlsx" is something the customer never agreed
    // to show support.
    [Fact]
    public async Task TheJobListNeverCarriesAFilename()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Failed, fileName: "quarterly-redundancies.xlsx");

        var jobs = await Console(db).GetRecentJobsAsync(
            null, seeded.WorkspaceId, 50, CancellationToken.None);

        var row = Assert.Single(jobs);

        // Checked over the whole row rather than field by field, so adding a
        // field that happens to carry the filename fails this test.
        Assert.DoesNotContain("quarterly-redundancies", row.ToString());
    }

    // Error messages can quote the text the extractor choked on, so the
    // unaudited view carries the CATEGORY only.
    [Fact]
    public async Task TheJobListCarriesErrorCategoriesButNotErrorMessages()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(
            db,
            JobStatus.Failed,
            errorCategory: "corruptedDocument",
            errorMessage: "could not parse row containing 'Salary band: confidential'");

        var row = Assert.Single(await Console(db).GetRecentJobsAsync(
            null, seeded.WorkspaceId, 50, CancellationToken.None));

        Assert.Contains("corruptedDocument", row.ErrorCategories);
        Assert.DoesNotContain("confidential", row.ToString());
    }

    [Fact]
    public async Task QueueHealthCountsWorkWaitingAndWorkStuck()
    {
        await using var db = _fixture.CreateContext();
        await SeedJobAsync(db, JobStatus.Queued);

        var health = await Console(db).GetQueueHealthAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.True(health.Queued >= 1);
        Assert.NotNull(health.OldestQueuedAge);
    }

    // A dead worker leaves its lease behind. The item is claimable again, so
    // this is not data loss - but it is the earliest visible sign of crashing
    // workers, and it has to be counted or nobody notices.
    [Fact]
    public async Task AnExpiredLeaseIsCountedAsStuck()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Extracting);

        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemId);
        item.LeaseOwner = "dead-worker";
        item.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var health = await Console(db).GetQueueHealthAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.True(health.ExpiredLeases >= 1);
    }

    // A finished item whose lease was never cleared is not stuck. Counting it
    // would make the alarm permanently loud and therefore useless.
    [Fact]
    public async Task ACompletedItemWithAStaleLeaseIsNotCountedAsStuck()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Completed);

        var before = await Console(db).GetQueueHealthAsync(DateTime.UtcNow, CancellationToken.None);

        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == seeded.ItemId);
        item.LeaseExpiresAtUtc = DateTime.UtcNow.AddMinutes(-10);
        await db.SaveChangesAsync();

        var after = await Console(db).GetQueueHealthAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(before.ExpiredLeases, after.ExpiredLeases);
    }

    // Bytes past their deletion time are a broken promise to the customer
    // (SR-SEC-6), not a backlog, so they are counted separately.
    [Fact]
    public async Task RetentionPostureFlagsSourceBytesPastTheirDeadline()
    {
        await using var db = _fixture.CreateContext();
        await SeedJobAsync(db, JobStatus.Completed, uploadedAtUtc: DateTime.UtcNow.AddDays(-5));

        var posture = await Console(db).GetRetentionPostureAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.True(posture.SourcesOverdue >= 1);
    }

    // A sweep runs on an interval, so something a minute past its deadline is
    // normal operation. Flagging it would make the number cry wolf constantly.
    [Fact]
    public async Task RetentionPostureDoesNotFlagSomethingOnlyJustPastItsDeadline()
    {
        await using var db = _fixture.CreateContext();
        var policy = new RetentionPolicy();

        var before = await Console(db).GetRetentionPostureAsync(DateTime.UtcNow, CancellationToken.None);

        // One minute past 24 hours: due, but well inside the sweep interval.
        await SeedJobAsync(
            db, JobStatus.Completed,
            uploadedAtUtc: DateTime.UtcNow - policy.SourceBytes - TimeSpan.FromMinutes(1));

        var after = await Console(db).GetRetentionPostureAsync(DateTime.UtcNow, CancellationToken.None);

        Assert.Equal(before.SourcesOverdue, after.SourcesOverdue);
    }

    // The core of SR-SEC-7's "narrowly scoped": no reason, no data.
    [Fact]
    public async Task InspectionIsRefusedWithoutAReason()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Failed);

        var (inspection, refusal) = await Inspection(db).InspectJobAsync(
            seeded.JobId, Guid.NewGuid(), "op@example.test", "  ", null,
            DateTime.UtcNow, CancellationToken.None);

        Assert.Null(inspection);
        Assert.NotNull(refusal);
        Assert.False(await db.OperatorAuditEntries.AnyAsync(e => e.TargetJobId == seeded.JobId));
    }

    [Fact]
    public async Task InspectionIsRefusedWhenTheReasonIsTooShortToBeOne()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Failed);

        var (inspection, refusal) = await Inspection(db).InspectJobAsync(
            seeded.JobId, Guid.NewGuid(), "op@example.test", "because", null,
            DateTime.UtcNow, CancellationToken.None);

        Assert.Null(inspection);
        Assert.NotNull(refusal);
    }

    [Fact]
    public async Task InspectionWithAReasonRevealsTheFilenamesAndRecordsWhoLooked()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Failed, fileName: "payroll.xlsx");
        var actorId = Guid.NewGuid();

        var (inspection, refusal) = await Inspection(db).InspectJobAsync(
            seeded.JobId, actorId, "op@example.test", "ticket 4821 - customer reports total failure",
            "203.0.113.7", DateTime.UtcNow, CancellationToken.None);

        Assert.Null(refusal);
        Assert.NotNull(inspection);
        Assert.Equal("payroll.xlsx", Assert.Single(inspection.Items).FileName);

        var audit = await db.OperatorAuditEntries.SingleAsync(e => e.TargetJobId == seeded.JobId);
        Assert.Equal(actorId, audit.ActorUserId);
        Assert.Equal("op@example.test", audit.ActorEmail);
        Assert.Equal(OperatorAuditActions.InspectJob, audit.Action);
        Assert.Equal(seeded.WorkspaceId, audit.TargetWorkspaceId);
        Assert.Contains("ticket 4821", audit.Reason);
        Assert.Equal("203.0.113.7", audit.IpAddress);
    }

    // Support seeing why a conversion failed is a far smaller promise than
    // support being able to read the document, and only the first is offered.
    [Fact]
    public async Task InspectionDoesNotExposeDocumentContent()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.CompletedWithWarnings);

        db.JobItemWarnings.Add(new JobItemWarning
        {
            Id = Guid.NewGuid(),
            JobItemId = seeded.ItemId,
            WorkspaceId = seeded.WorkspaceId,
            Code = "sheetTruncated",
            Severity = "error",
            // A real warning message quotes the document.
            Message = "row 251 begins 'Director bonus pool 4,200,000'",
            BlockId = "s1-b2",
            PageNumber = 3
        });
        await db.SaveChangesAsync();

        var (inspection, _) = await Inspection(db).InspectJobAsync(
            seeded.JobId, Guid.NewGuid(), "op@example.test", "investigating truncation report",
            null, DateTime.UtcNow, CancellationToken.None);

        var warning = Assert.Single(Assert.Single(inspection!.Items).Warnings);

        Assert.Equal("sheetTruncated", warning.Code);
        Assert.Equal("error", warning.Severity);
        Assert.Equal(3, warning.PageNumber);

        // The code and location are useful; the message is not offered at all.
        Assert.DoesNotContain("bonus pool", warning.ToString());
    }

    // An id that does not exist must not create an audit entry - otherwise the
    // trail fills with noise from typos and mis-pasted ids, and the real
    // entries get harder to find.
    [Fact]
    public async Task InspectingAJobThatDoesNotExistRecordsNothing()
    {
        await using var db = _fixture.CreateContext();
        var missingJobId = Guid.NewGuid();

        var (inspection, refusal) = await Inspection(db).InspectJobAsync(
            missingJobId, Guid.NewGuid(), "op@example.test", "checking a customer's reported id",
            null, DateTime.UtcNow, CancellationToken.None);

        Assert.Null(inspection);
        Assert.NotNull(refusal);
        Assert.False(await db.OperatorAuditEntries.AnyAsync(e => e.TargetJobId == missingJobId));
    }

    // The record that someone looked at a customer's files must outlive the
    // files. A cascade would erase the evidence exactly when it matters.
    [Fact]
    public async Task TheAuditEntrySurvivesTheJobBeingDeleted()
    {
        await using var db = _fixture.CreateContext();
        var seeded = await SeedJobAsync(db, JobStatus.Completed);

        await Inspection(db).InspectJobAsync(
            seeded.JobId, Guid.NewGuid(), "op@example.test", "ticket 5150 - checking output",
            null, DateTime.UtcNow, CancellationToken.None);

        // Hard-deleted, which is stronger than the tombstone the product
        // actually uses - if the entry survives this, it survives anything.
        await db.Database.ExecuteSqlAsync(
            $"""DELETE FROM "JobItemWarnings" WHERE "JobItemId" = {seeded.ItemId}""");
        await db.Database.ExecuteSqlAsync(
            $"""DELETE FROM "ConversionJobItems" WHERE "JobId" = {seeded.JobId}""");
        await db.Database.ExecuteSqlAsync(
            $"""DELETE FROM "ConversionJobs" WHERE "Id" = {seeded.JobId}""");

        Assert.True(await db.OperatorAuditEntries.AnyAsync(e => e.TargetJobId == seeded.JobId));
    }
}
