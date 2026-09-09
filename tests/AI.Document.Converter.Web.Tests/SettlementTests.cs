using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Worker;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// The other half of SR-BIL-5: what the customer is actually charged, once
// output exists.
//
// The EXTRACTION is stubbed here on purpose. What is under test is that the
// worker charges for what the engine reported, charges it once, and hands back
// the unused hold - none of which is about whether pdfplumber can read a table.
// Driving the real engine would make this slow and would make a billing test
// fail for extraction reasons.
[Collection(nameof(PostgresCollection))]
public sealed class SettlementTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _storageRoot;
    private readonly LocalFileSystemObjectStorage _storage;

    public SettlementTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _storageRoot = Path.Combine(Path.GetTempPath(), $"adc-settlement-test-{Guid.NewGuid():N}");
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

    // A document the engine "extracted": 7 pages, which is 7 credits.
    private static DocumentModel SevenPageDocument() => new()
    {
        Metadata = new DocumentMetadata
        {
            SourceFilePath = "report.pdf",
            FileType = SupportedFileType.Pdf,
            CreatedDate = DateTime.UtcNow,
            ConvertedDate = DateTime.UtcNow,
            PageCount = 7
        },
        Sections =
        [
            new Section
            {
                Heading = "Report",
                Blocks = [new ParagraphBlock { Text = "Body text." }]
            }
        ]
    };

    private sealed class StubProcessor(DocumentModel document) : IDocumentProcessor
    {
        public bool CanProcess(string filePath) => true;

        public Task<DocumentModel> ExtractAsync(string filePath, CancellationToken cancellationToken) =>
            Task.FromResult(document);
    }

    private sealed class StubResolver(DocumentModel document) : IDocumentProcessorResolver
    {
        public IDocumentProcessor Resolve(string filePath) => new StubProcessor(document);
    }

    private sealed class StubMarkdownGenerator : IMarkdownGenerator
    {
        public string Generate(DocumentModel document) => "# Report\n\nBody text.\n";
    }

    private sealed class StubChunkGenerator : IChunkGenerator
    {
        public Task<ChunkGenerationResult> GenerateChunksAsync(
            DocumentModel document,
            Domain.ValueObjects.ChunkOptions options,
            string sourceFileName,
            CancellationToken cancellationToken) =>
            Task.FromResult(new ChunkGenerationResult { Chunks = [], Warnings = [] });
    }

    private JobProcessor Processor(ConverterDbContext db, DocumentModel extracted) =>
        new(db,
            _storage,
            new StubResolver(extracted),
            new StubMarkdownGenerator(),
            new StubChunkGenerator(),
            new JobClaimer(db),
            new MeteringService(db, NullLogger<MeteringService>.Instance),
            NullLogger<JobProcessor>.Instance);

    // A workspace with one queued item whose source bytes really are in storage,
    // and a hold already placed the way intake would have placed one.
    private async Task<(Guid WorkspaceId, Guid JobId, Guid ItemId)> SeedQueuedJobAsync(
        ConverterDbContext db, long includedCredits, long heldCredits)
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

        var period = new UsagePeriod
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            StartsAtUtc = nowUtc.AddDays(-1),
            EndsAtUtc = nowUtc.AddDays(29),
            PlanCode = "standard@v1",
            IncludedCredits = includedCredits,
            ReservedCredits = heldCredits,
            CreatedAtUtc = nowUtc
        };
        db.UsagePeriods.Add(period);

        db.UsageReservations.Add(new UsageReservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UsagePeriodId = period.Id,
            JobId = jobId,
            EstimatedCredits = heldCredits,
            Status = ReservationStatus.Held,
            PolicyVersion = ConversionCredits.PolicyVersion,
            CreatedAtUtc = nowUtc
        });

        var storageKey = StorageKeys.ForSource(workspaceId, documentId);
        await using (var content = new MemoryStream("%PDF-1.4 stub"u8.ToArray()))
        {
            await _storage.WriteAsync(storageKey, content, CancellationToken.None);
        }

        db.SourceDocuments.Add(new SourceDocument
        {
            Id = documentId,
            WorkspaceId = workspaceId,
            OriginalFileName = "report.pdf",
            ContentType = "application/pdf",
            SizeBytes = 13,
            Sha256 = new string('a', 64),
            StorageKey = storageKey,
            UploadedAtUtc = nowUtc,
            UploadedByUserId = userId
        });

        db.ConversionJobs.Add(new ConversionJob
        {
            Id = jobId,
            WorkspaceId = workspaceId,
            CreatedByUserId = userId,
            Status = JobStatus.Queued,
            PresetName = "markdown-only",
            CreatedAtUtc = nowUtc
        });

        db.ConversionJobItems.Add(new ConversionJobItem
        {
            Id = itemId,
            JobId = jobId,
            WorkspaceId = workspaceId,
            SourceDocumentId = documentId,
            Status = JobStatus.Queued
        });

        await db.SaveChangesAsync();
        return (workspaceId, jobId, itemId);
    }

    private async Task RunAsync(ConverterDbContext db, Guid itemId, DocumentModel extracted)
    {
        // Leased directly rather than through JobClaimer.TryClaimAsync, which
        // claims the globally next claimable item. The fixture database is
        // shared across the collection, so that would hand this test whichever
        // queued row happens to sort first - another test's, most of the time.
        //
        // This writes exactly what a claim writes, so the lease renewal inside
        // PublishAsync still has a real lease of ours to renew.
        await db.Database.ExecuteSqlAsync($"""
            UPDATE "ConversionJobItems"
            SET "Status"            = {(int)JobStatus.Extracting},
                "LeaseOwner"        = 'test-worker',
                "LeaseExpiresAtUtc" = {DateTime.UtcNow.AddMinutes(5)},
                "AttemptCount"      = "AttemptCount" + 1,
                "StartedAtUtc"      = COALESCE("StartedAtUtc", {DateTime.UtcNow})
            WHERE "Id" = {itemId}
            """);

        // The raw UPDATE moved the row on; anything tracked from the seed is now
        // stale and would fail its concurrency check on the next save.
        db.ChangeTracker.Clear();

        await Processor(db, extracted).ProcessAsync(
            itemId, "test-worker", TimeSpan.FromMinutes(5), CancellationToken.None);
    }

    [Fact]
    public async Task PublishingAnItemChargesForWhatTheEngineReported()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId, itemId) = await SeedQueuedJobAsync(db, includedCredits: 100, heldCredits: 10);

        await RunAsync(db, itemId, SevenPageDocument());

        var entry = await db.UsageLedgerEntries.SingleAsync(e => e.JobId == jobId);

        // 7 pages, not the 10 that were held, and not the size of the Markdown.
        Assert.Equal(7, entry.Credits);
        Assert.Equal(LedgerEntryKind.Charge, entry.Kind);
        Assert.Equal("7 pages", entry.BasisDescription);
        Assert.Equal(ConversionCredits.PolicyVersion, entry.PolicyVersion);
        Assert.Equal("standard@v1", entry.PlanCode);
    }

    // The hold was 10, the work cost 7. The customer keeps the difference.
    [Fact]
    public async Task FinishingAJobReturnsTheUnusedPortionOfTheHold()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId, itemId) = await SeedQueuedJobAsync(db, includedCredits: 100, heldCredits: 10);

        await RunAsync(db, itemId, SevenPageDocument());

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);

        Assert.Equal(7, period.SettledCredits);
        Assert.Equal(0, period.ReservedCredits);
        Assert.Equal(93, period.AvailableCredits);

        var reservation = await db.UsageReservations.SingleAsync(r => r.JobId == jobId);
        Assert.Equal(ReservationStatus.Settled, reservation.Status);
    }

    // Queue delivery is at-least-once, so the same item WILL be processed twice
    // in production. It must not be billed twice.
    [Fact]
    public async Task ReprocessingTheSameItemDoesNotChargeTwice()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId, itemId) = await SeedQueuedJobAsync(db, includedCredits: 100, heldCredits: 10);

        await RunAsync(db, itemId, SevenPageDocument());

        // A replay: the item is handed to a worker again, exactly as a
        // redelivered queue message would be.
        var item = await db.ConversionJobItems.SingleAsync(i => i.Id == itemId);
        item.Status = JobStatus.Queued;
        item.CompletedAtUtc = null;
        await db.SaveChangesAsync();

        await RunAsync(db, itemId, SevenPageDocument());

        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(e => e.JobId == jobId));

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(7, period.SettledCredits);
    }

    // A file that produced nothing must not be charged for, and its hold must
    // come back - otherwise a failing document quietly costs the customer.
    [Fact]
    public async Task AFailedItemIsNotChargedAndItsHoldIsReturned()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, jobId, itemId) = await SeedQueuedJobAsync(db, includedCredits: 100, heldCredits: 10);

        // The source bytes are gone, so the worker fails the item rather than
        // publishing anything.
        var document = await db.SourceDocuments.SingleAsync(d => d.WorkspaceId == workspaceId);
        await _storage.DeleteAsync(document.StorageKey, CancellationToken.None);

        await RunAsync(db, itemId, SevenPageDocument());

        Assert.False(await db.UsageLedgerEntries.AnyAsync(e => e.JobId == jobId));

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(0, period.SettledCredits);
        Assert.Equal(0, period.ReservedCredits);
        Assert.Equal(100, period.AvailableCredits);
    }
}
