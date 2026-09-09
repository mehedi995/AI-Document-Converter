using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Persistence.Operations;

public sealed record InspectedItem(
    Guid ItemId,
    string FileName,
    long SizeBytes,
    JobStatus Status,
    int AttemptCount,
    string? ErrorCategory,
    string? ErrorMessage,
    IReadOnlyList<InspectedWarning> Warnings);

public sealed record InspectedWarning(string Code, string Severity, string? BlockId, int? PageNumber);

public sealed record JobInspection(
    Guid JobId,
    string WorkspaceSlug,
    JobStatus Status,
    string PresetName,
    DateTime CreatedAtUtc,
    bool IsDeleted,
    IReadOnlyList<InspectedItem> Items);

public sealed record InspectionRefusal(string Reason);

// The narrow, audited access flow SR-SEC-7 requires.
//
// Three things make it "narrowly scoped" rather than a general-purpose window
// into customer data:
//
//   1. It is ONE JOB at a time, named by id. There is no listing, no search by
//      filename, and no way to sweep across a workspace with it.
//   2. A REASON is mandatory and is stored. Requiring a support engineer to
//      write down why, before the data appears, is most of what stops casual
//      browsing - and it is the field an auditor actually reads.
//   3. The audit entry is committed BEFORE the data is returned. If the write
//      fails, the caller sees nothing. Recording access after serving it would
//      mean a crash could hand over customer data with no trace.
//
// It still does NOT return document content. There is no method here that reads
// an artifact's bytes or an extracted block's text, because "support can read
// your documents" is a different and much larger promise than "support can see
// why your conversion failed". Warning MESSAGES are excluded for the same
// reason - they can quote the text that caused them. Filenames and error
// messages ARE returned, because diagnosing a real failure usually needs them,
// and that is precisely why this path is audited and the console is not.
public sealed class OperatorInspectionService
{
    public const int MinimumReasonLength = 10;

    private readonly ConverterDbContext _db;
    private readonly ILogger<OperatorInspectionService> _logger;

    public OperatorInspectionService(ConverterDbContext db, ILogger<OperatorInspectionService> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task<(JobInspection? Inspection, InspectionRefusal? Refusal)> InspectJobAsync(
        Guid jobId,
        Guid actorUserId,
        string actorEmail,
        string? reason,
        string? ipAddress,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        // Checked before anything is read, so a request with no reason never
        // causes customer data to be loaded at all.
        if (string.IsNullOrWhiteSpace(reason) || reason.Trim().Length < MinimumReasonLength)
        {
            return (null, new InspectionRefusal(
                $"State why this job needs inspecting, in at least {MinimumReasonLength} characters. "
                + "The reason is recorded against your account."));
        }

        var jobExists = await _db.ConversionJobs
            .AsNoTracking()
            .AnyAsync(j => j.Id == jobId, cancellationToken);

        if (!jobExists)
        {
            // Not audited as an inspection, because nothing was inspected -
            // but also not silently ignored: a run of these is somebody
            // guessing at job ids.
            _logger.LogWarning(
                "Operator {ActorUserId} attempted to inspect job {JobId}, which does not exist",
                actorUserId, jobId);

            return (null, new InspectionRefusal("No job with that id exists."));
        }

        var workspaceId = await _db.ConversionJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => j.WorkspaceId)
            .SingleAsync(cancellationToken);

        // Written and committed first. The data is only fetched afterwards, so
        // there is no path on which somebody sees a customer's filenames
        // without this row existing.
        _db.OperatorAuditEntries.Add(new OperatorAuditEntry
        {
            Id = Guid.NewGuid(),
            ActorUserId = actorUserId,
            ActorEmail = actorEmail,
            Action = OperatorAuditActions.InspectJob,
            TargetJobId = jobId,
            TargetWorkspaceId = workspaceId,
            Reason = reason.Trim(),
            IpAddress = ipAddress,
            OccurredAtUtc = nowUtc
        });

        await _db.SaveChangesAsync(cancellationToken);

        // Logged without the filenames it is about to return, and without the
        // reason text, which is free-form operator input.
        _logger.LogInformation(
            "Operator {ActorUserId} inspected job {JobId} in workspace {WorkspaceId}",
            actorUserId, jobId, workspaceId);

        var inspection = await _db.ConversionJobs
            .AsNoTracking()
            .Where(j => j.Id == jobId)
            .Select(j => new JobInspection(
                j.Id,
                j.Workspace!.Slug,
                j.Status,
                j.PresetName,
                j.CreatedAtUtc,
                j.DeletedAtUtc != null,
                j.Items
                    .OrderBy(i => i.SourceDocument!.OriginalFileName)
                    .Select(i => new InspectedItem(
                        i.Id,
                        i.SourceDocument!.OriginalFileName,
                        i.SourceDocument.SizeBytes,
                        i.Status,
                        i.AttemptCount,
                        i.ErrorCategory,
                        i.ErrorMessage,
                        _db.JobItemWarnings
                            .Where(w => w.JobItemId == i.Id)
                            // Code, severity and location only. The warning
                            // MESSAGE can quote the document, so it stays out
                            // even here.
                            .Select(w => new InspectedWarning(
                                w.Code, w.Severity, w.BlockId, w.PageNumber))
                            .ToList()))
                    .ToList()))
            .SingleAsync(cancellationToken);

        return (inspection, null);
    }

    // The audit trail itself, readable by operators. Deliberately visible to
    // the people it records: an audit log that only its subjects cannot see is
    // a surveillance tool, and one everybody can see is a deterrent.
    public async Task<IReadOnlyList<OperatorAuditEntry>> GetAuditTrailAsync(
        int take, CancellationToken cancellationToken) =>
        await _db.OperatorAuditEntries
            .AsNoTracking()
            .OrderByDescending(e => e.OccurredAtUtc)
            .Take(take)
            .ToListAsync(cancellationToken);
}

// A fixed vocabulary rather than free text at each call site, so the trail can
// be queried and so a typo cannot quietly create a new, unsearchable category.
public static class OperatorAuditActions
{
    public const string InspectJob = "inspectJob";
    public const string ViewConsole = "viewConsole";
    public const string ViewAuditTrail = "viewAuditTrail";
}
