using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.Web.Tests;

// SR-BIL-5. Against real PostgreSQL, because the guarantees under test are
// enforced by the database - a conditional UPDATE and a unique index. An
// in-memory provider would let both of them pass while proving nothing.
[Collection(nameof(PostgresCollection))]
public sealed class MeteringTests
{
    private readonly PostgresFixture _fixture;

    public MeteringTests(PostgresFixture fixture) => _fixture = fixture;

    private static MeteringService Service(ConverterDbContext db) =>
        new(db, NullLogger<MeteringService>.Instance);

    private static async Task<Guid> SeedWorkspaceAsync(
        ConverterDbContext db, long includedCredits, DateTime nowUtc)
    {
        var workspaceId = Guid.NewGuid();
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
        return workspaceId;
    }

    [Fact]
    public async Task AReservationWithinTheAllowanceIsGranted()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 100, nowUtc);

        var outcome = await Service(db).TryReserveAsync(
            workspaceId, Guid.NewGuid(), 40, nowUtc, CancellationToken.None);

        Assert.True(outcome.Granted);
        Assert.Equal(60, outcome.AvailableCredits);
    }

    // No automatic overage. SR-BIL-5 requires monetary overages disabled
    // initially, so the customer is stopped rather than silently billed beyond
    // what they agreed to.
    [Fact]
    public async Task AReservationBeyondTheAllowanceIsRefusedRatherThanOverdrawn()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 10, nowUtc);

        var outcome = await Service(db).TryReserveAsync(
            workspaceId, Guid.NewGuid(), 50, nowUtc, CancellationToken.None);

        Assert.False(outcome.Granted);
        Assert.Contains("50 credit(s)", outcome.Refusal);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(0, period.ReservedCredits);
    }

    // THE test for this class. Ten jobs of 20 credits race for an allowance of
    // 100: exactly five must win. Reading the balance and then writing it would
    // let more through, because every reader would see the full balance before
    // any writer committed.
    [Fact]
    public async Task ConcurrentReservationsCannotOverspendTheAllowance()
    {
        var nowUtc = DateTime.UtcNow;
        Guid workspaceId;

        await using (var setup = _fixture.CreateContext())
        {
            workspaceId = await SeedWorkspaceAsync(setup, 100, nowUtc);
        }

        // Each attempt gets its own DbContext and connection, so this is real
        // database concurrency rather than serialised calls on one context.
        var attempts = Enumerable.Range(0, 10).Select(async _ =>
        {
            await using var db = _fixture.CreateContext();
            return await Service(db).TryReserveAsync(
                workspaceId, Guid.NewGuid(), 20, nowUtc, CancellationToken.None);
        });

        var outcomes = await Task.WhenAll(attempts);

        Assert.Equal(5, outcomes.Count(o => o.Granted));

        await using var verify = _fixture.CreateContext();
        var period = await verify.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);

        Assert.Equal(100, period.ReservedCredits);
        Assert.True(period.AvailableCredits >= 0, "the allowance must never go negative");
    }

    // Queue delivery is at-least-once, so a duplicate settlement is not a
    // hypothetical - it will happen.
    [Fact]
    public async Task SettlingTheSameItemTwiceChargesOnlyOnce()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 100, nowUtc);
        var jobId = Guid.NewGuid();
        var itemId = Guid.NewGuid();

        var service = Service(db);

        var first = await service.SettleAsync(
            workspaceId, jobId, itemId, 7, "3 pages", nowUtc, CancellationToken.None);
        var second = await service.SettleAsync(
            workspaceId, jobId, itemId, 7, "3 pages", nowUtc, CancellationToken.None);

        Assert.True(first.Charged);
        Assert.False(second.Charged);
        Assert.True(second.WasDuplicate);

        Assert.Equal(1, await db.UsageLedgerEntries.CountAsync(e => e.JobItemId == itemId));

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(7, period.SettledCredits);
    }

    // Work that produced nothing must give the hold back, or a customer's
    // allowance leaks a little on every failure.
    [Fact]
    public async Task ReleasingAHoldReturnsTheAllowance()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 100, nowUtc);
        var jobId = Guid.NewGuid();

        var service = Service(db);
        await service.TryReserveAsync(workspaceId, jobId, 30, nowUtc, CancellationToken.None);
        await service.ReleaseAsync(jobId, nowUtc, CancellationToken.None);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(0, period.ReservedCredits);
        Assert.Equal(100, period.AvailableCredits);
    }

    [Fact]
    public async Task ReleasingTwiceDoesNotHandBackTheAllowanceTwice()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 100, nowUtc);
        var jobId = Guid.NewGuid();

        var service = Service(db);
        await service.TryReserveAsync(workspaceId, jobId, 30, nowUtc, CancellationToken.None);
        await service.ReleaseAsync(jobId, nowUtc, CancellationToken.None);
        await service.ReleaseAsync(jobId, nowUtc, CancellationToken.None);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);
        Assert.Equal(100, period.AvailableCredits);
    }

    // The customer is charged for what was produced, not for what was
    // estimated. An over-generous estimate must not quietly consume allowance.
    [Fact]
    public async Task ClosingAReservationReturnsTheUnusedPortionOfTheEstimate()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 100, nowUtc);
        var jobId = Guid.NewGuid();

        var service = Service(db);
        await service.TryReserveAsync(workspaceId, jobId, 50, nowUtc, CancellationToken.None);
        await service.SettleAsync(
            workspaceId, jobId, Guid.NewGuid(), 12, "12 pages", nowUtc, CancellationToken.None);
        await service.CloseReservationAsync(jobId, nowUtc, CancellationToken.None);

        var period = await db.UsagePeriods.SingleAsync(p => p.WorkspaceId == workspaceId);

        Assert.Equal(12, period.SettledCredits);
        Assert.Equal(0, period.ReservedCredits);
        Assert.Equal(88, period.AvailableCredits);
    }

    // Every ledger entry has to remain explainable after the rules change.
    [Fact]
    public async Task LedgerEntriesRecordThePolicyAndPlanThatPricedThem()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;
        var workspaceId = await SeedWorkspaceAsync(db, 100, nowUtc);

        await Service(db).SettleAsync(
            workspaceId, Guid.NewGuid(), Guid.NewGuid(), 3, "3 pages", nowUtc, CancellationToken.None);

        // Scoped to this workspace. The fixture database is shared across the
        // collection, so an unfiltered SingleAsync picks up every other test's
        // ledger entries and fails for a reason that has nothing to do with
        // what is under test.
        var entry = await db.UsageLedgerEntries.SingleAsync(e => e.WorkspaceId == workspaceId);

        Assert.Equal(ConversionCredits.PolicyVersion, entry.PolicyVersion);
        Assert.Equal("standard@v1", entry.PlanCode);
        Assert.Equal("3 pages", entry.BasisDescription);
    }

    [Fact]
    public async Task AWorkspaceWithNoSubscriptionGetsTheTrialAllowance()
    {
        await using var db = _fixture.CreateContext();
        var nowUtc = DateTime.UtcNow;

        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId, Name = "w", Slug = $"ws-{Guid.NewGuid():N}"[..15], CreatedAtUtc = nowUtc
        });
        await db.SaveChangesAsync();

        var period = await Service(db).GetOrCreateCurrentPeriodAsync(
            workspaceId, nowUtc, CancellationToken.None);

        Assert.Equal(PlanCatalog.Trial.IncludedCreditsPerPeriod, period.IncludedCredits);
        Assert.Equal(PlanCatalog.Trial.VersionedCode, period.PlanCode);
    }
}
