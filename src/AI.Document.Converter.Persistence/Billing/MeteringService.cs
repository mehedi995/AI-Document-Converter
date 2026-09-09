using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Persistence.Billing;

public sealed record ReservationOutcome(
    bool Granted, Guid? ReservationId, long RequestedCredits, long AvailableCredits, string? Refusal);

public sealed record SettlementOutcome(bool Charged, long Credits, bool WasDuplicate);

// SR-BIL-5. Reserve on acceptance, settle on published output, release on
// anything that did not produce output.
//
// The invariant this whole class exists to hold: CONCURRENT JOBS CANNOT
// OVERSPEND. Two uploads arriving at the same instant must not both see the
// full remaining balance and both be granted it.
public sealed class MeteringService
{
    private readonly ConverterDbContext _db;
    private readonly ILogger<MeteringService> _logger;

    public MeteringService(ConverterDbContext db, ILogger<MeteringService> logger)
    {
        _db = db;
        _logger = logger;
    }

    // Read-only view of the current window, for pages that display a balance.
    //
    // Deliberately does NOT create one. GetOrCreateCurrentPeriodAsync starts the
    // clock on a trial, and merely looking at the upload page must not spend a
    // day of somebody's trial. Null means "nothing has been metered yet", which
    // a caller renders from the plan rather than from usage.
    public Task<UsagePeriod?> FindCurrentPeriodAsync(
        Guid workspaceId, DateTime nowUtc, CancellationToken cancellationToken) =>
        _db.UsagePeriods
            .AsNoTracking()
            .Where(p => p.WorkspaceId == workspaceId && p.StartsAtUtc <= nowUtc && p.EndsAtUtc > nowUtc)
            .OrderByDescending(p => p.StartsAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

    // Returns the workspace's current entitlement window, creating it if the
    // previous one has elapsed. Periods are contiguous so usage always lands
    // somewhere; a gap would mean unbilled work.
    public async Task<UsagePeriod> GetOrCreateCurrentPeriodAsync(
        Guid workspaceId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var current = await _db.UsagePeriods
            .Where(p => p.WorkspaceId == workspaceId && p.StartsAtUtc <= nowUtc && p.EndsAtUtc > nowUtc)
            .OrderByDescending(p => p.StartsAtUtc)
            .FirstOrDefaultAsync(cancellationToken);

        if (current is not null)
        {
            return current;
        }

        var subscription = await _db.Subscriptions
            .SingleOrDefaultAsync(s => s.WorkspaceId == workspaceId, cancellationToken);

        // No subscription means the workspace has never been set up
        // commercially. It gets the trial, rather than either unlimited access
        // or a hard failure.
        var plan = subscription is null
            ? PlanCatalog.Trial
            : PlanCatalog.Require(StripVersion(subscription.PlanCode));

        var period = new UsagePeriod
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            StartsAtUtc = nowUtc,
            EndsAtUtc = nowUtc + plan.PeriodLength,
            // Copied, not referenced. A mid-period plan change must not
            // retroactively alter what this period allowed (SR-BIL-6).
            PlanCode = plan.VersionedCode,
            IncludedCredits = plan.IncludedCreditsPerPeriod,
            CreatedAtUtc = nowUtc
        };

        _db.UsagePeriods.Add(period);
        await _db.SaveChangesAsync(cancellationToken);

        return period;
    }

    // Places a hold, atomically.
    //
    // The reservation is applied with a SINGLE conditional UPDATE whose WHERE
    // clause re-checks the balance. Reading the balance and then writing it
    // would leave a window in which two concurrent jobs both read "500
    // available" and both reserve 400. Letting the database evaluate the
    // condition and the update together is what closes that window - there is
    // no application-side moment for the second job to slip through.
    public async Task<ReservationOutcome> TryReserveAsync(
        Guid workspaceId, Guid jobId, long estimatedCredits, DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        if (estimatedCredits <= 0)
        {
            return new ReservationOutcome(false, null, estimatedCredits, 0,
                "A conversion must be estimated at one credit or more.");
        }

        var period = await GetOrCreateCurrentPeriodAsync(workspaceId, nowUtc, cancellationToken);

        var rowsUpdated = await _db.Database.ExecuteSqlAsync($"""
            UPDATE "UsagePeriods"
            SET "ReservedCredits" = "ReservedCredits" + {estimatedCredits}
            WHERE "Id" = {period.Id}
              AND "IncludedCredits" - "ReservedCredits" - "SettledCredits" >= {estimatedCredits}
            """, cancellationToken);

        if (rowsUpdated == 0)
        {
            // Refused for lack of allowance. Deliberately NOT an automatic
            // overage: SR-BIL-5 requires monetary overages to be disabled
            // initially, so the customer is stopped rather than silently billed
            // more than they agreed to.
            await _db.Entry(period).ReloadAsync(cancellationToken);

            return new ReservationOutcome(
                false, null, estimatedCredits, period.AvailableCredits,
                $"This conversion needs {estimatedCredits} credit(s) but only "
                + $"{period.AvailableCredits} remain in the current period.");
        }

        var reservation = new UsageReservation
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UsagePeriodId = period.Id,
            JobId = jobId,
            EstimatedCredits = estimatedCredits,
            Status = ReservationStatus.Held,
            PolicyVersion = ConversionCredits.PolicyVersion,
            CreatedAtUtc = nowUtc
        };

        _db.UsageReservations.Add(reservation);
        await _db.SaveChangesAsync(cancellationToken);
        await _db.Entry(period).ReloadAsync(cancellationToken);

        return new ReservationOutcome(true, reservation.Id, estimatedCredits, period.AvailableCredits, null);
    }

    // Charges for one published output.
    //
    // Idempotent by construction: the ledger's unique index on IdempotencyKey
    // means a duplicate settlement - a replayed queue message, a retried publish
    // - is rejected by the database rather than double-charging. Queue delivery
    // is at-least-once, so this WILL be exercised in production.
    public async Task<SettlementOutcome> SettleAsync(
        Guid workspaceId,
        Guid jobId,
        Guid jobItemId,
        long credits,
        string basisDescription,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Derived from the item, not from a counter or a timestamp, so the same
        // item always produces the same key however many times it is retried.
        var idempotencyKey = $"settle:item:{jobItemId:N}";

        if (await _db.UsageLedgerEntries.AnyAsync(
                e => e.IdempotencyKey == idempotencyKey, cancellationToken))
        {
            _logger.LogInformation(
                "Settlement for item {JobItemId} already recorded; not charging again", jobItemId);
            return new SettlementOutcome(false, 0, WasDuplicate: true);
        }

        var period = await GetOrCreateCurrentPeriodAsync(workspaceId, nowUtc, cancellationToken);

        var entry = new UsageLedgerEntry
        {
            Id = Guid.NewGuid(),
            WorkspaceId = workspaceId,
            UsagePeriodId = period.Id,
            JobId = jobId,
            JobItemId = jobItemId,
            Kind = LedgerEntryKind.Charge,
            Credits = credits,
            BasisDescription = basisDescription,
            PolicyVersion = ConversionCredits.PolicyVersion,
            PlanCode = period.PlanCode,
            IdempotencyKey = idempotencyKey,
            CreatedAtUtc = nowUtc
        };

        _db.UsageLedgerEntries.Add(entry);

        // Settled goes up; the hold comes down by the same amount so the
        // allowance is not consumed twice. Clamped at zero because an actual
        // charge may exceed its estimate, and a negative hold would silently
        // hand back allowance that was never reserved.
        await _db.Database.ExecuteSqlAsync($"""
            UPDATE "UsagePeriods"
            SET "SettledCredits" = "SettledCredits" + {credits},
                "ReservedCredits" = GREATEST("ReservedCredits" - {credits}, 0)
            WHERE "Id" = {period.Id}
            """, cancellationToken);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two workers settled the same item at the same instant. The unique
            // index is the authority; this is a duplicate, not a failure.
            _logger.LogInformation(
                "Concurrent duplicate settlement for item {JobItemId} rejected by the ledger", jobItemId);
            return new SettlementOutcome(false, 0, WasDuplicate: true);
        }

        await RefreshTrackedPeriodAsync(period.Id, cancellationToken);
        return new SettlementOutcome(true, credits, false);
    }

    // Gives the hold back. Called for work that produced nothing - rejected,
    // failed or cancelled. Without this, a customer's allowance would leak a
    // little every time a conversion failed.
    public async Task ReleaseAsync(Guid jobId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var reservation = await _db.UsageReservations
            .SingleOrDefaultAsync(r => r.JobId == jobId && r.Status == ReservationStatus.Held, cancellationToken);

        if (reservation is null)
        {
            // Already settled or released. Releasing twice must be harmless, or
            // a retry would hand back allowance repeatedly.
            return;
        }

        await _db.Database.ExecuteSqlAsync($"""
            UPDATE "UsagePeriods"
            SET "ReservedCredits" = GREATEST("ReservedCredits" - {reservation.EstimatedCredits}, 0)
            WHERE "Id" = {reservation.UsagePeriodId}
            """, cancellationToken);

        reservation.Status = ReservationStatus.Released;
        reservation.ResolvedAtUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);
        await RefreshTrackedPeriodAsync(reservation.UsagePeriodId, cancellationToken);

        _logger.LogInformation(
            "Released {Credits} reserved credit(s) for job {JobId}", reservation.EstimatedCredits, jobId);
    }

    // Marks a job's hold as settled once all its items are done. The numeric
    // adjustment already happened per item in SettleAsync; this closes the
    // reservation so it is never released later and does not linger as Held.
    public async Task CloseReservationAsync(
        Guid jobId, DateTime nowUtc, CancellationToken cancellationToken)
    {
        var reservation = await _db.UsageReservations
            .SingleOrDefaultAsync(r => r.JobId == jobId && r.Status == ReservationStatus.Held, cancellationToken);

        if (reservation is null)
        {
            return;
        }

        // Whatever of the estimate was not consumed by actual settlements goes
        // back: the customer is charged for what was produced, not for what was
        // guessed.
        var actuallyCharged = await _db.UsageLedgerEntries
            .Where(e => e.JobId == jobId && e.Kind == LedgerEntryKind.Charge)
            .SumAsync(e => (long?)e.Credits, cancellationToken) ?? 0;

        var unusedHold = Math.Max(reservation.EstimatedCredits - actuallyCharged, 0);

        if (unusedHold > 0)
        {
            await _db.Database.ExecuteSqlAsync($"""
                UPDATE "UsagePeriods"
                SET "ReservedCredits" = GREATEST("ReservedCredits" - {unusedHold}, 0)
                WHERE "Id" = {reservation.UsagePeriodId}
                """, cancellationToken);
        }

        reservation.Status = ReservationStatus.Settled;
        reservation.ResolvedAtUtc = nowUtc;
        await _db.SaveChangesAsync(cancellationToken);
        await RefreshTrackedPeriodAsync(reservation.UsagePeriodId, cancellationToken);
    }

    // Raw SQL changes the row but not the copy EF is tracking, so anything that
    // reads the period afterwards - including the caller that just settled -
    // would see stale balances. This refreshes whatever is tracked.
    //
    // The same class of mistake as a raw-SQL lease renewal invalidating its own
    // row version: mixing ExecuteSql with tracked entities always needs the
    // tracker told.
    private async Task RefreshTrackedPeriodAsync(Guid usagePeriodId, CancellationToken cancellationToken)
    {
        var tracked = _db.ChangeTracker.Entries<UsagePeriod>()
            .FirstOrDefault(e => e.Entity.Id == usagePeriodId);

        if (tracked is not null)
        {
            await tracked.ReloadAsync(cancellationToken);
        }
    }

    private static string StripVersion(string planCode)
    {
        var separator = planCode.IndexOf('@');
        return separator < 0 ? planCode : planCode[..separator];
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
