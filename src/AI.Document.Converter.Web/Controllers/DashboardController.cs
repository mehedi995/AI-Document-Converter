using System.Security.Claims;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Controllers;

[Authorize]
[Route("dashboard")]
public sealed class DashboardController : Controller
{
    private const int RecentJobCount = 20;

    private readonly ConverterDbContext _db;
    private readonly WorkspaceAccessService _workspaceAccess;
    private readonly MeteringService _metering;

    public DashboardController(
        ConverterDbContext db, WorkspaceAccessService workspaceAccess, MeteringService metering)
    {
        _db = db;
        _workspaceAccess = workspaceAccess;
        _metering = metering;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var userId = GetUserId();

        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(userId, cancellationToken);
        if (workspace is null)
        {
            // Registration creates the workspace in the same transaction as the
            // user, so this should be unreachable. Failing visibly beats
            // rendering an empty dashboard that looks like "you have no data".
            return View("NoWorkspace");
        }

        var jobs = await LoadRecentJobsAsync(workspace.Id, cancellationToken);

        // Read-only: looking at the dashboard must not start a trial period.
        var period = await _metering.FindCurrentPeriodAsync(
            workspace.Id, DateTime.UtcNow, cancellationToken);

        return View(new DashboardViewModel(
            workspace.Name,
            workspace.Slug,
            jobs,
            period?.AvailableCredits,
            period?.IncludedCredits,
            period?.ReservedCredits));
    }

    // Polled by the page only while something is actually in flight. Returns the
    // same numbers the server-rendered page shows, so a client with scripting
    // disabled sees identical (if less frequently updated) information.
    [HttpGet("status")]
    public async Task<IActionResult> Status(CancellationToken cancellationToken)
    {
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);
        if (workspace is null)
        {
            return NotFound();
        }

        var jobs = await LoadRecentJobsAsync(workspace.Id, cancellationToken);
        return Json(new { jobs, anyInFlight = jobs.Any(j => j.IsInFlight) });
    }

    private async Task<List<DashboardJobRow>> LoadRecentJobsAsync(
        Guid workspaceId, CancellationToken cancellationToken)
    {
        // SR-SEC-2: scoped by the workspace resolved from membership, never by
        // an id taken off the request.
        //
        // Counts are computed in SQL rather than by loading every item: a
        // workspace with a long history should not pull thousands of rows into
        // memory to render twenty.
        return await _db.ConversionJobs
            .Where(j => j.WorkspaceId == workspaceId)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(RecentJobCount)
            .Select(j => new DashboardJobRow(
                j.Id,
                j.Status.ToString(),
                j.PresetName,
                j.CreatedAtUtc,
                j.Items.Count,
                j.Items.Count(i => i.Status == JobStatus.Completed),
                j.Items.Count(i => i.Status == JobStatus.CompletedWithWarnings),
                j.Items.Count(i => i.Status == JobStatus.Failed),
                j.Items.Count(i => i.Status == JobStatus.Cancelled),
                // "In progress" means genuinely claimed and running, not merely
                // "not finished" - a queued item has not started.
                j.Items.Count(i => i.Status == JobStatus.Extracting
                                   || i.Status == JobStatus.Formatting
                                   || i.Status == JobStatus.Chunking
                                   || i.Status == JobStatus.Exporting),
                j.Items.Count(i => i.Status == JobStatus.Queued),
                j.DeletedAtUtc != null))
            .ToListAsync(cancellationToken);
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record DashboardJobRow(
    Guid Id,
    string Status,
    string PresetName,
    DateTime CreatedAtUtc,
    int FileCount,
    int CompletedCount,
    int WarningCount,
    int FailedCount,
    int CancelledCount,
    int InProgressCount,
    int QueuedCount,
    bool IsDeleted)
{
    public int FinishedCount => CompletedCount + WarningCount + FailedCount + CancelledCount;

    public bool IsInFlight => InProgressCount > 0 || QueuedCount > 0;

    // SaaS §10: "Show real stage progress rather than fabricated percentages."
    //
    // This is a count of finished files over total files - a fact, not an
    // estimate. It deliberately says nothing about how far through the CURRENT
    // file the engine is, because nothing knows that: extraction time depends
    // on the document, and a bar that creeps forward on a timer is a lie the
    // user will believe.
    public string ProgressDescription => IsInFlight
        ? $"{FinishedCount} of {FileCount} files finished"
        : $"{FileCount} file{(FileCount == 1 ? string.Empty : "s")}";
}

public sealed record DashboardViewModel(
    string WorkspaceName,
    string WorkspaceSlug,
    IReadOnlyList<DashboardJobRow> RecentJobs,
    // Null until something has been metered this period.
    long? AvailableCredits,
    long? IncludedCredits,
    long? HeldCredits);
