using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// SaaS §5.8 (reconvert) and the FR-044 supersession: in the cloud edition a
// reconvert creates a NEW run rather than overwriting the earlier one. Cloud
// runs are immutable, so history stays intact and two results can be compared.
[Collection(nameof(PostgresCollection))]
public sealed class ReconvertTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _storageRoot;
    private readonly LocalFileSystemObjectStorage _storage;

    public ReconvertTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _storageRoot = Path.Combine(Path.GetTempPath(), $"adc-reconvert-test-{Guid.NewGuid():N}");
        _storage = new LocalFileSystemObjectStorage(
            Options.Create(new LocalFileSystemObjectStorageOptions { RootDirectory = _storageRoot }));
    }

    public void Dispose()
    {
        if (Directory.Exists(_storageRoot))
        {
            Directory.Delete(_storageRoot, recursive: true);
        }
    }

    private ConversionIntakeService Intake(ConverterDbContext db) =>
        new(db, _storage, new UploadValidator(),
            new MeteringService(db, NullLogger<MeteringService>.Instance),
            NullLogger<ConversionIntakeService>.Instance);

    private static async Task<(Guid WorkspaceId, ConversionJob Job, List<SourceDocument> Documents)>
        SeedCompletedJobAsync(ConverterDbContext db, int fileCount)
    {
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId, Name = "w", Slug = $"ws-{Guid.NewGuid():N}"[..15], CreatedAtUtc = nowUtc
        });

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = nowUtc,
            CompletedAtUtc = nowUtc
        };
        db.ConversionJobs.Add(job);

        var documents = new List<SourceDocument>();

        for (var i = 0; i < fileCount; i++)
        {
            var documentId = Guid.NewGuid();
            var document = new SourceDocument
            {
                Id = documentId,
                WorkspaceId = workspaceId,
                OriginalFileName = $"file{i}.pdf",
                ContentType = "application/pdf",
                SizeBytes = 10,
                Sha256 = new string('f', 64),
                StorageKey = StorageKeys.ForSource(workspaceId, documentId),
                UploadedAtUtc = nowUtc,
                UploadedByUserId = Guid.NewGuid()
            };
            db.SourceDocuments.Add(document);
            documents.Add(document);

            db.ConversionJobItems.Add(new ConversionJobItem
            {
                Id = Guid.NewGuid(),
                JobId = job.Id,
                WorkspaceId = workspaceId,
                SourceDocumentId = documentId,
                Status = JobStatus.Completed
            });
        }

        await db.SaveChangesAsync();
        return (workspaceId, job, documents);
    }

    [Fact]
    public async Task ReconvertCreatesANewRunAndLeavesTheOriginalUntouched()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, original, documents) = await SeedCompletedJobAsync(db, 2);

        var newJobId = await Intake(db).CreateJobFromExistingDocumentsAsync(
            workspaceId, Guid.NewGuid(), "default", documents, CancellationToken.None);

        Assert.NotEqual(original.Id, newJobId);

        var reloadedOriginal = await db.ConversionJobs.SingleAsync(j => j.Id == original.Id);
        Assert.Equal(JobStatus.Completed, reloadedOriginal.Status);
        Assert.NotNull(reloadedOriginal.CompletedAtUtc);

        var newJob = await db.ConversionJobs.SingleAsync(j => j.Id == newJobId);
        Assert.Equal(JobStatus.Queued, newJob.Status);
    }

    // The point of referencing existing documents rather than re-uploading:
    // storage does not grow, and both runs remain traceable to one input.
    [Fact]
    public async Task ReconvertReusesTheSameSourceDocumentsRatherThanCopyingBytes()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, original, documents) = await SeedCompletedJobAsync(db, 3);

        var newJobId = await Intake(db).CreateJobFromExistingDocumentsAsync(
            workspaceId, Guid.NewGuid(), "default", documents, CancellationToken.None);

        var originalDocumentIds = await db.ConversionJobItems
            .Where(i => i.JobId == original.Id).Select(i => i.SourceDocumentId).OrderBy(id => id).ToListAsync();
        var newDocumentIds = await db.ConversionJobItems
            .Where(i => i.JobId == newJobId).Select(i => i.SourceDocumentId).OrderBy(id => id).ToListAsync();

        Assert.Equal(originalDocumentIds, newDocumentIds);

        // Six job items, still only three documents.
        Assert.Equal(3, await db.SourceDocuments.CountAsync(d => d.WorkspaceId == workspaceId));
        Assert.Equal(6, await db.ConversionJobItems.CountAsync(i => i.WorkspaceId == workspaceId));
    }

    // A reconvert may use a different preset - that is much of the point of
    // running it again.
    [Fact]
    public async Task ReconvertCanUseADifferentPreset()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, _, documents) = await SeedCompletedJobAsync(db, 1);

        var newJobId = await Intake(db).CreateJobFromExistingDocumentsAsync(
            workspaceId, Guid.NewGuid(), "large-chunks", documents, CancellationToken.None);

        var newJob = await db.ConversionJobs.SingleAsync(j => j.Id == newJobId);
        Assert.Equal("large-chunks", newJob.PresetName);
    }

    // Defence in depth (SR-SEC-2). The caller queries by workspace, but a job
    // must never reference another tenant's document, so the invariant is
    // asserted where it would be violated rather than assumed.
    [Fact]
    public async Task ReconvertRefusesADocumentFromAnotherWorkspace()
    {
        await using var db = _fixture.CreateContext();
        var (_, _, documents) = await SeedCompletedJobAsync(db, 1);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Intake(db).CreateJobFromExistingDocumentsAsync(
                Guid.NewGuid(), Guid.NewGuid(), "default", documents, CancellationToken.None));
    }

    // The new run is Queued in the same commit that creates it, so a worker can
    // claim it immediately - the same SR-JOB-1 ordering as a fresh upload.
    [Fact]
    public async Task ReconvertQueuesEveryItemImmediately()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, _, documents) = await SeedCompletedJobAsync(db, 4);

        var newJobId = await Intake(db).CreateJobFromExistingDocumentsAsync(
            workspaceId, Guid.NewGuid(), "default", documents, CancellationToken.None);

        var statuses = await db.ConversionJobItems
            .Where(i => i.JobId == newJobId).Select(i => i.Status).ToListAsync();

        Assert.Equal(4, statuses.Count);
        Assert.All(statuses, status => Assert.Equal(JobStatus.Queued, status));
    }
}
