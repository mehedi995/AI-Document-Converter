using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Storage;
using AI.Document.Converter.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Web.Tests;

// SR-BIL-5 end to end at the point of acceptance: a job cannot be queued
// without an allowance behind it, and refusing one must leave nothing behind.
//
// MeteringTests proves the arithmetic in isolation. This proves it is actually
// WIRED - a metering service that works perfectly but is never called would
// pass every test in that file.
[Collection(nameof(PostgresCollection))]
public sealed class MeteredIntakeTests : IDisposable
{
    private readonly PostgresFixture _fixture;
    private readonly string _storageRoot;
    private readonly LocalFileSystemObjectStorage _storage;

    public MeteredIntakeTests(PostgresFixture fixture)
    {
        _fixture = fixture;
        _storageRoot = Path.Combine(Path.GetTempPath(), $"adc-metered-intake-{Guid.NewGuid():N}");
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

    private static async Task<(Guid WorkspaceId, Guid UserId)> SeedWorkspaceAsync(
        ConverterDbContext db, long includedCredits)
    {
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId, Name = "w", Slug = $"ws-{Guid.NewGuid():N}"[..15], CreatedAtUtc = nowUtc
        });

        db.UsagePeriods.Add(new UsagePeriod
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            StartsAtUtc = nowUtc.AddDays(-1),
            EndsAtUtc = nowUtc.AddDays(29),
            PlanCode = "standard@v1",
            IncludedCredits = includedCredits,
            CreatedAtUtc = nowUtc
        });

        await db.SaveChangesAsync();
        return (workspaceId, userId);
    }

    // A real sample, so the estimate comes from a container the estimator has to
    // actually understand rather than from a byte array shaped to suit it.
    private static IntakeFile SampleFile(string fileName)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);

        while (directory is not null
               && !File.Exists(Path.Combine(directory.FullName, "AI.Document.Converter.sln")))
        {
            directory = directory.Parent;
        }

        Assert.NotNull(directory);

        var bytes = File.ReadAllBytes(Path.Combine(directory.FullName, "samples", fileName));
        return new IntakeFile(fileName, "application/octet-stream", bytes.LongLength, new MemoryStream(bytes));
    }

    [Fact]
    public async Task AcceptingAJobHoldsCreditsAgainstTheAllowance()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, userId) = await SeedWorkspaceAsync(db, 100);

        var result = await Intake(db).AcceptAsync(
            workspaceId, userId, "default", [SampleFile("sample.pdf")], CancellationToken.None);

        Assert.NotNull(result.JobId);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);

        // sample.pdf is 3 pages, so 3 credits are held - not spent. Settlement
        // happens when output is published, not when work is accepted.
        Assert.Equal(3, period.ReservedCredits);
        Assert.Equal(0, period.SettledCredits);
        Assert.Equal(97, period.AvailableCredits);

        var reservation = await db.UsageReservations.SingleAsync(r => r.JobId == result.JobId);
        Assert.Equal(ReservationStatus.Held, reservation.Status);
    }

    // No automatic overage (SR-BIL-5). The customer is stopped, and told why.
    [Fact]
    public async Task AnUploadBeyondTheAllowanceIsRefusedAndNoJobIsCreated()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, userId) = await SeedWorkspaceAsync(db, 2);

        // 3 pages against an allowance of 2.
        var result = await Intake(db).AcceptAsync(
            workspaceId, userId, "default", [SampleFile("sample.pdf")], CancellationToken.None);

        Assert.Null(result.JobId);
        Assert.Equal(0, result.AcceptedCount);
        Assert.Equal("insufficientCredits", result.Files.Single().RejectionCode);
        Assert.Contains("3 credit(s)", result.Files.Single().RejectionMessage);

        Assert.False(await db.ConversionJobs.AnyAsync(j => j.WorkspaceId == workspaceId));

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(0, period.ReservedCredits);
    }

    // The refusal rolls back a transaction that had already inserted rows, and
    // the bytes were written before it. Both have to be gone, or a refused
    // upload leaks storage nothing references and no retention sweep can find.
    [Fact]
    public async Task ARefusedUploadLeavesNoRowsAndNoStoredBytes()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, userId) = await SeedWorkspaceAsync(db, 1);

        await Intake(db).AcceptAsync(
            workspaceId, userId, "default", [SampleFile("sample.pdf")], CancellationToken.None);

        Assert.False(await db.SourceDocuments.AnyAsync(d => d.WorkspaceId == workspaceId));
        Assert.False(await db.ConversionJobItems.AnyAsync(i => i.WorkspaceId == workspaceId));
        // Checked against the filesystem rather than through ListAsync, which
        // rightly refuses a prefix that StorageKeys did not produce. This also
        // asserts something stronger: no bytes were left ANYWHERE, not merely
        // none under the key this upload was expected to use.
        var leftBehind = Directory.Exists(_storageRoot)
            ? Directory.GetFiles(_storageRoot, "*", SearchOption.AllDirectories)
            : [];

        Assert.Empty(leftBehind);
    }

    // Files are held together: the hold has to cover the whole job, because the
    // whole job is what gets queued.
    [Fact]
    public async Task TheHoldCoversEveryFileInTheJob()
    {
        await using var db = _fixture.CreateContext();
        var (workspaceId, userId) = await SeedWorkspaceAsync(db, 100);

        // 3 PDF pages + 2 slides + 1 text file.
        var result = await Intake(db).AcceptAsync(
            workspaceId,
            userId,
            "default",
            [SampleFile("sample.pdf"), SampleFile("sample.pptx"), SampleFile("sample.txt")],
            CancellationToken.None);

        Assert.NotNull(result.JobId);
        Assert.Equal(3, result.AcceptedCount);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(6, period.ReservedCredits);
    }

    // A workspace that has never been set up commercially still gets a period,
    // so usage always lands somewhere rather than being silently unmetered.
    [Fact]
    public async Task AWorkspaceWithNoPeriodIsGivenTheTrialRatherThanUnmeteredAccess()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = Guid.NewGuid();

        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId, Name = "w", Slug = $"ws-{Guid.NewGuid():N}"[..15], CreatedAtUtc = nowUtc
        });
        await db.SaveChangesAsync();

        var result = await Intake(db).AcceptAsync(
            workspaceId, Guid.NewGuid(), "default", [SampleFile("sample.pdf")], CancellationToken.None);

        Assert.NotNull(result.JobId);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(PlanCatalog.Trial.VersionedCode, period.PlanCode);
        Assert.Equal(3, period.ReservedCredits);
    }
}
