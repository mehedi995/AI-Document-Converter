using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Worker;

// Hands out exactly one job item to exactly one worker (SR-JOB-2).
//
// The claim is a single atomic statement using PostgreSQL's
// `FOR UPDATE SKIP LOCKED`, which is the standard way to build a queue on a
// relational table: concurrent workers each lock a different row instead of
// contending for the same one, and no row is ever handed out twice. Doing this
// as "SELECT then UPDATE" from application code would leave a window where two
// workers both read the same Queued row and both start work on it.
//
// A claim also covers items whose lease has expired. An expired lease means the
// worker holding it died - that is what stops a crash from stranding work
// forever, and it is why leases are used rather than a simple in-progress flag.
public sealed class JobClaimer
{
    private readonly ConverterDbContext _db;

    public JobClaimer(ConverterDbContext db) => _db = db;

    public async Task<Guid?> TryClaimAsync(
        string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;
        var leaseExpiry = nowUtc + leaseDuration;

        // Claimable = never started, or previously leased by a worker that is
        // now overdue. Terminal states are excluded so a finished item cannot be
        // picked up again by a late-arriving duplicate message.
        var claimed = await _db.Database
            .SqlQuery<Guid>($"""
                UPDATE "ConversionJobItems" AS target
                SET "Status"            = {(int)JobStatus.Extracting},
                    "LeaseOwner"        = {leaseOwner},
                    "LeaseExpiresAtUtc" = {leaseExpiry},
                    "AttemptCount"      = target."AttemptCount" + 1,
                    "StartedAtUtc"      = COALESCE(target."StartedAtUtc", {nowUtc})
                WHERE target."Id" = (
                    SELECT candidate."Id"
                    FROM "ConversionJobItems" AS candidate
                    WHERE
                        (
                            candidate."Status" = {(int)JobStatus.Queued}
                            OR (
                                candidate."LeaseExpiresAtUtc" IS NOT NULL
                                AND candidate."LeaseExpiresAtUtc" < {nowUtc}
                                AND candidate."Status" NOT IN (
                                    {(int)JobStatus.Completed},
                                    {(int)JobStatus.CompletedWithWarnings},
                                    {(int)JobStatus.Failed},
                                    {(int)JobStatus.Cancelled},
                                    {(int)JobStatus.Expired}
                                )
                            )
                        )
                    ORDER BY candidate."Id"
                    FOR UPDATE SKIP LOCKED
                    LIMIT 1
                )
                RETURNING target."Id"
                """)
            .ToListAsync(cancellationToken);

        return claimed.Count > 0 ? claimed[0] : null;
    }

    // Extends a lease that is still held by this worker. Long extractions must
    // not have their work stolen mid-flight simply for taking a while.
    public async Task<bool> TryRenewLeaseAsync(
        Guid itemId, string leaseOwner, TimeSpan leaseDuration, CancellationToken cancellationToken)
    {
        var rows = await _db.Database.ExecuteSqlAsync($"""
            UPDATE "ConversionJobItems"
            SET "LeaseExpiresAtUtc" = {DateTime.UtcNow + leaseDuration}
            WHERE "Id" = {itemId} AND "LeaseOwner" = {leaseOwner}
            """, cancellationToken);

        // False means another worker took over after our lease expired. The
        // caller must then abandon its result rather than publish it, or two
        // workers would both write artifacts for the same item.
        return rows == 1;
    }
}
