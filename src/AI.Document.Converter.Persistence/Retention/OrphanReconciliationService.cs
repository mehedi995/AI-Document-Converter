using AI.Document.Converter.Persistence.Storage;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Persistence.Retention;

public sealed class OrphanReconciliationOptions
{
    // How old an object must be before it can be considered orphaned.
    //
    // This is the single most important setting here. Uploads write bytes
    // BEFORE committing the row (so a failed commit cannot leave a row pointing
    // at nothing), which means a just-written object legitimately has no row for
    // a moment. Without a generous grace period, reconciliation would race
    // ordinary uploads and delete customers' files while they were being
    // accepted.
    //
    // Two hours is far longer than any upload or conversion, and orphans are
    // wasted storage rather than an emergency - there is no reason to be hasty.
    public TimeSpan MinimumAge { get; set; } = TimeSpan.FromHours(2);

    // Report what would be deleted without deleting it. The intended way to
    // introduce this to a production system: run it in report-only mode, read
    // the logs, and only then let it delete.
    public bool ReportOnly { get; set; }

    // A single pass that wants to delete more than this refuses and reports
    // instead. A bug that made every object look orphaned would otherwise wipe
    // the store in one run; this turns that catastrophe into a loud log line.
    public int MaxDeletionsPerRun { get; set; } = 500;

    public TimeSpan Interval { get; set; } = TimeSpan.FromHours(6);
}

public sealed record ReconciliationReport(
    int ObjectsScanned, int OrphansFound, int OrphansDeleted, long BytesReclaimed, bool AbortedBySafetyLimit);

// Finds stored objects that no database row points at (the gap recorded in
// increment 9).
//
// Everything else in this system reasons from rows outward: retention walks
// SourceDocument and Artifact rows and deletes the bytes they name. That can
// never find an object whose row was lost - a failed commit, a manual database
// edit, a restored backup that is older than the object store. Those objects
// are invisible, retained forever, and paid for.
//
// Reconciliation is the only process that reasons from storage inward, and it is
// therefore the only one that can delete something nothing knows about. Hence
// the deliberate caution: a minimum age, a per-run cap, and a report-only mode.
public sealed class OrphanReconciliationService
{
    private readonly ConverterDbContext _db;
    private readonly IObjectStorage _storage;
    private readonly OrphanReconciliationOptions _options;
    private readonly ILogger<OrphanReconciliationService> _logger;

    public OrphanReconciliationService(
        ConverterDbContext db,
        IObjectStorage storage,
        IOptions<OrphanReconciliationOptions> options,
        ILogger<OrphanReconciliationService> logger)
    {
        _db = db;
        _storage = storage;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReconciliationReport> ReconcileAsync(
        DateTime nowUtc, CancellationToken cancellationToken)
    {
        var cutoff = nowUtc - _options.MinimumAge;

        // Both key sets, loaded once. A per-object query would turn a sweep of
        // a large store into one round trip per file.
        //
        // Deliberately includes rows whose bytes are already marked deleted:
        // such a row still NAMES its key, and if the byte-deletion did not
        // actually happen, that object is retention's problem to retry - not an
        // orphan for this sweep to claim.
        var referencedKeys = new HashSet<string>(StringComparer.Ordinal);

        await foreach (var key in _db.SourceDocuments
            .Select(d => d.StorageKey).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            referencedKeys.Add(key);
        }

        await foreach (var key in _db.Artifacts
            .Select(a => a.StorageKey).AsAsyncEnumerable().WithCancellation(cancellationToken))
        {
            referencedKeys.Add(key);
        }

        var scanned = 0;
        var orphans = new List<StoredObject>();

        await foreach (var stored in _storage.ListAsync("workspaces", cancellationToken))
        {
            scanned++;

            if (referencedKeys.Contains(stored.Key))
            {
                continue;
            }

            // Too young to judge. This is the upload window, not an orphan.
            if (stored.LastModifiedUtc > cutoff)
            {
                continue;
            }

            orphans.Add(stored);
        }

        if (orphans.Count > _options.MaxDeletionsPerRun)
        {
            // Refuse rather than proceed. A number this large is far more likely
            // to mean the reference set failed to load than that thousands of
            // objects genuinely leaked.
            _logger.LogError(
                "Orphan reconciliation found {OrphanCount} candidate(s), above the {Limit} per-run "
                + "safety limit. NOTHING was deleted. This usually means the reference set is "
                + "incomplete rather than that this many objects leaked - investigate before raising "
                + "the limit.",
                orphans.Count, _options.MaxDeletionsPerRun);

            return new ReconciliationReport(
                scanned, orphans.Count, 0, 0, AbortedBySafetyLimit: true);
        }

        if (_options.ReportOnly)
        {
            if (orphans.Count > 0)
            {
                _logger.LogWarning(
                    "Orphan reconciliation (REPORT ONLY): {OrphanCount} object(s) totalling "
                    + "{Bytes} bytes have no database row. Nothing was deleted.",
                    orphans.Count, orphans.Sum(o => o.SizeBytes));
            }

            return new ReconciliationReport(
                scanned, orphans.Count, 0, orphans.Sum(o => o.SizeBytes), false);
        }

        long reclaimed = 0;
        var deleted = 0;

        foreach (var orphan in orphans)
        {
            await _storage.DeleteAsync(orphan.Key, cancellationToken);
            reclaimed += orphan.SizeBytes;
            deleted++;
        }

        if (deleted > 0)
        {
            // Counts and bytes only - never a key, which embeds a workspace id
            // (FR-035).
            _logger.LogWarning(
                "Orphan reconciliation deleted {DeletedCount} object(s) with no database row, "
                + "reclaiming {Bytes} bytes. Orphans indicate a failed write path - worth "
                + "investigating if this is not zero.",
                deleted, reclaimed);
        }

        return new ReconciliationReport(scanned, orphans.Count, deleted, reclaimed, false);
    }
}
