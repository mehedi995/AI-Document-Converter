using System.Text;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// SR-SEC-6. The upload page promises customers that source files are deleted
// after 24 hours and output after 7 days. These tests are what make that a
// behaviour rather than a sentence.
//
// Run against real PostgreSQL and real object storage (a temp directory), not
// mocks: the thing under test is whether the bytes are actually gone.
[Collection(nameof(PostgresCollection))]
public sealed class RetentionTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _storageRoot;
    private readonly LocalFileSystemObjectStorage _storage;

    public RetentionTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _storageRoot = Path.Combine(Path.GetTempPath(), $"adc-retention-test-{Guid.NewGuid():N}");
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

    private RetentionService CreateService(ConverterDbContext db, RetentionPolicy policy) =>
        new(db, _storage, Options.Create(policy), NullLogger<RetentionService>.Instance);

    private sealed record Fixture(Guid WorkspaceId, Guid JobId, SourceDocument Document, Artifact Artifact);

    private async Task<Fixture> SeedWithRealBytesAsync(
        ConverterDbContext db, DateTime uploadedAtUtc, DateTime artifactCreatedAtUtc)
    {
        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "w",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = uploadedAtUtc
        });

        var documentId = Guid.NewGuid();
        var sourceKey = StorageKeys.ForSource(workspaceId, documentId);
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("source bytes")))
        {
            await _storage.WriteAsync(sourceKey, content, CancellationToken.None);
        }

        var document = new SourceDocument
        {
            Id = documentId,
            WorkspaceId = workspaceId,
            OriginalFileName = "report.pdf",
            ContentType = "application/pdf",
            SizeBytes = 12,
            Sha256 = new string('c', 64),
            StorageKey = sourceKey,
            UploadedAtUtc = uploadedAtUtc,
            UploadedByUserId = Guid.NewGuid()
        };
        db.SourceDocuments.Add(document);

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = uploadedAtUtc,
            CompletedAtUtc = artifactCreatedAtUtc
        };
        db.ConversionJobs.Add(job);

        var item = new ConversionJobItem
        {
            Id = Guid.NewGuid(),
            JobId = job.Id,
            WorkspaceId = workspaceId,
            SourceDocumentId = documentId,
            Status = JobStatus.Completed
        };
        db.ConversionJobItems.Add(item);

        var artifactId = Guid.NewGuid();
        var artifactKey = StorageKeys.ForArtifact(workspaceId, artifactId);
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("# converted")))
        {
            await _storage.WriteAsync(artifactKey, content, CancellationToken.None);
        }

        var artifact = new Artifact
        {
            Id = artifactId,
            JobItemId = item.Id,
            WorkspaceId = workspaceId,
            Kind = ArtifactKind.Markdown,
            StorageKey = artifactKey,
            FileName = "report.md",
            SizeBytes = 11,
            CreatedAtUtc = artifactCreatedAtUtc
        };
        db.Artifacts.Add(artifact);

        await db.SaveChangesAsync();
        return new Fixture(workspaceId, job.Id, document, artifact);
    }

    [Fact]
    public async Task SourceBytesArePurgedAfterTheirWindow_OutputSurvivesItsLongerOne()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;

        // Uploaded 25 hours ago (past the 24h source window), converted 1 hour
        // ago (well inside the 7d output window).
        var seeded = await SeedWithRealBytesAsync(
            db, nowUtc.AddHours(-25), nowUtc.AddHours(-1));

        var report = await CreateService(db, new RetentionPolicy())
            .SweepAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, report.SourcePurged);
        Assert.Equal(0, report.OutputPurged);

        // The bytes are actually gone, not merely flagged.
        Assert.False(await _storage.ExistsAsync(seeded.Document.StorageKey, CancellationToken.None));
        Assert.True(await _storage.ExistsAsync(seeded.Artifact.StorageKey, CancellationToken.None));

        // The metadata row survives: history has to still make sense.
        var document = await db.SourceDocuments.SingleAsync(d => d.Id == seeded.Document.Id);
        Assert.NotNull(document.SourceBytesDeletedAtUtc);
        Assert.Equal("report.pdf", document.OriginalFileName);
    }

    [Fact]
    public async Task OutputBytesArePurgedAfterTheirWindowAndTheJobBecomesExpired()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;

        var seeded = await SeedWithRealBytesAsync(
            db, nowUtc.AddDays(-9), nowUtc.AddDays(-8));

        var report = await CreateService(db, new RetentionPolicy())
            .SweepAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, report.OutputPurged);
        Assert.False(await _storage.ExistsAsync(seeded.Artifact.StorageKey, CancellationToken.None));

        // Once every byte is gone the job says so, rather than continuing to
        // offer a download that would only 404.
        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);
        Assert.Equal(JobStatus.Expired, job.Status);
    }

    [Fact]
    public async Task NothingInsideItsWindowIsTouched()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;

        var seeded = await SeedWithRealBytesAsync(db, nowUtc.AddHours(-1), nowUtc.AddHours(-1));

        var report = await CreateService(db, new RetentionPolicy())
            .SweepAsync(nowUtc, CancellationToken.None);

        Assert.False(report.DidWork);
        Assert.True(await _storage.ExistsAsync(seeded.Document.StorageKey, CancellationToken.None));
        Assert.True(await _storage.ExistsAsync(seeded.Artifact.StorageKey, CancellationToken.None));
    }

    // Running twice must not re-delete or double-count. A sweep runs every few
    // minutes forever, so non-idempotence would show up as endless log noise
    // and wasted storage calls.
    [Fact]
    public async Task SweepIsIdempotent()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        await SeedWithRealBytesAsync(db, nowUtc.AddDays(-9), nowUtc.AddDays(-8));

        var service = CreateService(db, new RetentionPolicy());

        var first = await service.SweepAsync(nowUtc, CancellationToken.None);
        var second = await service.SweepAsync(nowUtc, CancellationToken.None);

        Assert.True(first.DidWork);
        Assert.False(second.DidWork);
    }

    [Fact]
    public async Task CustomerDeletionRemovesBothSourceAndOutputImmediately()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var seeded = await SeedWithRealBytesAsync(db, nowUtc, nowUtc);

        var deleted = await CreateService(db, new RetentionPolicy())
            .DeleteJobContentAsync(seeded.WorkspaceId, seeded.JobId, nowUtc, CancellationToken.None);

        Assert.True(deleted);
        Assert.False(await _storage.ExistsAsync(seeded.Document.StorageKey, CancellationToken.None));
        Assert.False(await _storage.ExistsAsync(seeded.Artifact.StorageKey, CancellationToken.None));
    }

    // The requirement the spec singles out: "Deletion must prevent queued or
    // retried jobs from recreating artifacts." The tombstone outlives the bytes
    // so a worker replaying the item later still finds it.
    [Fact]
    public async Task DeletionLeavesATombstoneThatOutlivesTheBytes()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var seeded = await SeedWithRealBytesAsync(db, nowUtc, nowUtc);

        await CreateService(db, new RetentionPolicy())
            .DeleteJobContentAsync(seeded.WorkspaceId, seeded.JobId, nowUtc, CancellationToken.None);

        var job = await db.ConversionJobs.SingleAsync(j => j.Id == seeded.JobId);

        Assert.NotNull(job.DeletedAtUtc);
        Assert.Equal(JobStatus.Expired, job.Status);
    }

    // Deletion is destructive, so it must not be reachable by changing a job id
    // in a URL (SR-SEC-2).
    [Fact]
    public async Task AnotherTenantCannotDeleteThisWorkspacesJob()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var seeded = await SeedWithRealBytesAsync(db, nowUtc, nowUtc);

        var deleted = await CreateService(db, new RetentionPolicy())
            .DeleteJobContentAsync(Guid.NewGuid(), seeded.JobId, nowUtc, CancellationToken.None);

        Assert.False(deleted);

        // And crucially the bytes are still there - a refused delete must not
        // half-happen.
        Assert.True(await _storage.ExistsAsync(seeded.Document.StorageKey, CancellationToken.None));
        Assert.True(await _storage.ExistsAsync(seeded.Artifact.StorageKey, CancellationToken.None));
    }

    // The notice on the upload page is generated from this object, so if the
    // configured window changes the page changes with it.
    [Fact]
    public void PolicyDescriptionReflectsTheConfiguredWindows()
    {
        var policy = new RetentionPolicy
        {
            SourceBytes = TimeSpan.FromHours(24),
            OutputBytes = TimeSpan.FromDays(7)
        };

        var notice = policy.DescribeForCustomer();

        Assert.Contains("24 hours", notice);
        Assert.Contains("7 days", notice);
    }
}
