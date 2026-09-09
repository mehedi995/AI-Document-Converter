using System.Text;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Retention;
using AI.Document.Converter.Persistence.Storage;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// Closes the gap recorded in increment 9: the retention sweep walks rows, so an
// object no row points at is invisible to it and is never reclaimed.
//
// This is the only process that can delete something nothing knows about, so
// most of these tests are about what it must REFUSE to delete rather than what
// it reclaims. A false positive here is unrecoverable customer data loss.
[Collection(nameof(PostgresCollection))]
public sealed class OrphanReconciliationTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _storageRoot;
    private readonly LocalFileSystemObjectStorage _storage;

    public OrphanReconciliationTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _storageRoot = Path.Combine(Path.GetTempPath(), $"adc-orphan-test-{Guid.NewGuid():N}");
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

    private OrphanReconciliationService Service(ConverterDbContext db, OrphanReconciliationOptions options) =>
        new(db, _storage, Options.Create(options), NullLogger<OrphanReconciliationService>.Instance);

    private async Task<string> WriteObjectAsync(string key, DateTime lastWriteUtc)
    {
        await using (var content = new MemoryStream(Encoding.UTF8.GetBytes("bytes")))
        {
            await _storage.WriteAsync(key, content, CancellationToken.None);
        }

        // The age check reads the file's timestamp, so the test has to set it
        // rather than wait two hours.
        File.SetLastWriteTimeUtc(
            Path.Combine(_storageRoot, key.Replace('/', Path.DirectorySeparatorChar)), lastWriteUtc);

        return key;
    }

    private static async Task<Guid> SeedWorkspaceAsync(ConverterDbContext db)
    {
        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "w",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return workspaceId;
    }

    private static SourceDocument NewDocument(Guid workspaceId, Guid documentId, string key) => new()
    {
        Id = documentId,
        WorkspaceId = workspaceId,
        OriginalFileName = "doc.pdf",
        ContentType = "application/pdf",
        SizeBytes = 5,
        Sha256 = new string('a', 64),
        StorageKey = key,
        UploadedAtUtc = DateTime.UtcNow,
        UploadedByUserId = Guid.NewGuid()
    };

    [Fact]
    public async Task AnObjectWithNoRowIsReclaimed()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var orphanKey = await WriteObjectAsync(
            StorageKeys.ForSource(workspaceId, Guid.NewGuid()), nowUtc.AddHours(-5));

        var report = await Service(db, new OrphanReconciliationOptions())
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, report.OrphansDeleted);
        Assert.False(await _storage.ExistsAsync(orphanKey, CancellationToken.None));
    }

    [Fact]
    public async Task AnObjectWithARowIsNeverTouched()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var documentId = Guid.NewGuid();
        var key = await WriteObjectAsync(
            StorageKeys.ForSource(workspaceId, documentId), nowUtc.AddDays(-30));

        db.SourceDocuments.Add(NewDocument(workspaceId, documentId, key));
        await db.SaveChangesAsync();

        var report = await Service(db, new OrphanReconciliationOptions())
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, report.OrphansFound);
        Assert.True(await _storage.ExistsAsync(key, CancellationToken.None));
    }

    // THE test. Uploads write bytes before committing the row, so a file being
    // accepted right now legitimately has no row. Deleting it would destroy a
    // customer's upload mid-flight.
    [Fact]
    public async Task AJustWrittenObjectIsNotTreatedAsAnOrphan()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var inFlightKey = await WriteObjectAsync(
            StorageKeys.ForSource(workspaceId, Guid.NewGuid()), nowUtc.AddMinutes(-1));

        var report = await Service(db, new OrphanReconciliationOptions())
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, report.OrphansFound);
        Assert.True(await _storage.ExistsAsync(inFlightKey, CancellationToken.None));
    }

    // A row whose bytes are marked deleted still NAMES its key. If the byte
    // deletion did not actually take effect, that is retention's job to retry -
    // not an orphan for this sweep to claim, which would hide the failure.
    [Fact]
    public async Task AnObjectNamedByARowWithDeletedBytesIsStillNotAnOrphan()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var documentId = Guid.NewGuid();
        var key = await WriteObjectAsync(
            StorageKeys.ForSource(workspaceId, documentId), nowUtc.AddDays(-30));

        var document = NewDocument(workspaceId, documentId, key);
        document.SourceBytesDeletedAtUtc = nowUtc.AddDays(-1);
        db.SourceDocuments.Add(document);
        await db.SaveChangesAsync();

        var report = await Service(db, new OrphanReconciliationOptions())
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, report.OrphansFound);
    }

    // A partially written upload has no key and no row. Treating it as an
    // orphan would delete a file that is being written at that moment.
    [Fact]
    public async Task APartialWriteInProgressIsIgnored()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var partialPath = Path.Combine(
            _storageRoot, "workspaces", workspaceId.ToString("N"), "sources", "abc.partial");
        Directory.CreateDirectory(Path.GetDirectoryName(partialPath)!);
        await File.WriteAllTextAsync(partialPath, "half a file");
        File.SetLastWriteTimeUtc(partialPath, nowUtc.AddDays(-5));

        var report = await Service(db, new OrphanReconciliationOptions())
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(0, report.OrphansFound);
        Assert.True(File.Exists(partialPath));
    }

    // A bug that made every object look orphaned must not be able to empty the
    // store in one pass.
    [Fact]
    public async Task ExceedingThePerRunLimitDeletesNothingAtAll()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var keys = new List<string>();
        for (var i = 0; i < 5; i++)
        {
            keys.Add(await WriteObjectAsync(
                StorageKeys.ForSource(workspaceId, Guid.NewGuid()), nowUtc.AddDays(-5)));
        }

        var report = await Service(db, new OrphanReconciliationOptions { MaxDeletionsPerRun = 2 })
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.True(report.AbortedBySafetyLimit);
        Assert.Equal(0, report.OrphansDeleted);

        // Every single one survives - the limit is not a cap on how many to
        // delete, it is a refusal to act at all.
        foreach (var key in keys)
        {
            Assert.True(await _storage.ExistsAsync(key, CancellationToken.None));
        }
    }

    [Fact]
    public async Task ReportOnlyModeFindsOrphansWithoutDeletingThem()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var key = await WriteObjectAsync(
            StorageKeys.ForSource(workspaceId, Guid.NewGuid()), nowUtc.AddDays(-5));

        var report = await Service(db, new OrphanReconciliationOptions { ReportOnly = true })
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(1, report.OrphansFound);
        Assert.Equal(0, report.OrphansDeleted);
        Assert.True(report.BytesReclaimed > 0, "report-only should still size the reclaim");
        Assert.True(await _storage.ExistsAsync(key, CancellationToken.None));
    }

    [Fact]
    public async Task ArtifactKeysCountAsReferencesToo()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var documentId = Guid.NewGuid();
        var sourceKey = await WriteObjectAsync(
            StorageKeys.ForSource(workspaceId, documentId), nowUtc.AddDays(-30));
        db.SourceDocuments.Add(NewDocument(workspaceId, documentId, sourceKey));

        var job = new ConversionJob
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            CreatedByUserId = Guid.NewGuid(),
            Status = JobStatus.Completed,
            PresetName = "default",
            CreatedAtUtc = nowUtc
        };
        db.ConversionJobs.Add(job);

        var itemId = Guid.NewGuid();
        db.ConversionJobItems.Add(new ConversionJobItem
        {
            Id = itemId,
            JobId = job.Id,
            WorkspaceId = workspaceId,
            SourceDocumentId = documentId,
            Status = JobStatus.Completed
        });

        var artifactId = Guid.NewGuid();
        var artifactKey = await WriteObjectAsync(
            StorageKeys.ForArtifact(workspaceId, artifactId), nowUtc.AddDays(-30));

        db.Artifacts.Add(new Artifact
        {
            Id = artifactId,
            JobItemId = itemId,
            WorkspaceId = workspaceId,
            Kind = ArtifactKind.Markdown,
            StorageKey = artifactKey,
            FileName = "doc.md",
            SizeBytes = 5,
            CreatedAtUtc = nowUtc
        });
        await db.SaveChangesAsync();

        var report = await Service(db, new OrphanReconciliationOptions())
            .ReconcileAsync(nowUtc, CancellationToken.None);

        Assert.Equal(2, report.ObjectsScanned);
        Assert.Equal(0, report.OrphansFound);
    }
}
