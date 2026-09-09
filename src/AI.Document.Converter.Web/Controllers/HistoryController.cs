using System.Security.Claims;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Controllers;

// SaaS §5.8: conversion history with search/filter, authorized re-download,
// reconvert, explicit deletion, and expiration states.
[Authorize]
[Route("history")]
public sealed class HistoryController : Controller
{
    private const int PageSize = 25;

    private readonly ConverterDbContext _db;
    private readonly WorkspaceAccessService _workspaceAccess;
    private readonly ConversionIntakeService _intake;
    private readonly ILogger<HistoryController> _logger;

    public HistoryController(
        ConverterDbContext db,
        WorkspaceAccessService workspaceAccess,
        ConversionIntakeService intake,
        ILogger<HistoryController> logger)
    {
        _db = db;
        _workspaceAccess = workspaceAccess;
        _intake = intake;
        _logger = logger;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        string? q, string? status, int page = 1, CancellationToken cancellationToken = default)
    {
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);
        if (workspace is null)
        {
            return View("~/Views/Dashboard/NoWorkspace.cshtml");
        }

        page = Math.Max(1, page);

        // SR-SEC-2: the workspace filter is applied first and is not optional.
        // Search and status only ever narrow within it.
        var query = _db.ConversionJobs.Where(j => j.WorkspaceId == workspace.Id);

        if (!string.IsNullOrWhiteSpace(q))
        {
            var term = q.Trim();

            // Matched against the stored filename via EF.Functions.Like, which
            // is parameterised - the term is never concatenated into SQL. The
            // wildcards are added by us, not taken from the user, so a term
            // containing % cannot widen the search beyond their workspace.
            var pattern = $"%{term.Replace("%", "\\%").Replace("_", "\\_")}%";

            query = query.Where(j => j.Items.Any(i =>
                EF.Functions.Like(i.SourceDocument!.OriginalFileName, pattern)));
        }

        var statusFilter = ParseStatus(status);
        if (statusFilter is not null)
        {
            query = query.Where(j => j.Status == statusFilter);
        }

        var totalCount = await query.CountAsync(cancellationToken);

        var jobs = await query
            .OrderByDescending(j => j.CreatedAtUtc)
            .Skip((page - 1) * PageSize)
            .Take(PageSize)
            .Select(j => new HistoryRow(
                j.Id,
                j.Status.ToString(),
                j.PresetName,
                j.CreatedAtUtc,
                j.Items.Count,
                j.Items.Count(i => i.Status == JobStatus.CompletedWithWarnings),
                j.Items.Count(i => i.Status == JobStatus.Failed),
                j.DeletedAtUtc != null,
                // Whether anything is still downloadable. A job whose bytes
                // have been purged should not offer a link that only 404s.
                j.Items.Any(i => i.Artifacts.Any(a => a.BytesDeletedAtUtc == null)),
                j.Items
                    .OrderBy(i => i.SourceDocument!.OriginalFileName)
                    .Select(i => i.SourceDocument!.OriginalFileName)
                    .Take(3)
                    .ToList()))
            .ToListAsync(cancellationToken);

        return View(new HistoryViewModel(
            jobs, q, status, page, (int)Math.Ceiling(totalCount / (double)PageSize), totalCount));
    }

    // FR-044 is superseded for the cloud edition: a reconvert creates a NEW
    // run rather than overwriting the old one. Cloud runs are immutable, so
    // history stays intact and the two results can be compared.
    //
    // Only possible while the SOURCE bytes still exist - after their retention
    // window there is nothing to convert, and the button is hidden accordingly.
    [HttpPost("{jobId:guid}/reconvert")]
    public async Task<IActionResult> Reconvert(
        Guid jobId, string? preset, CancellationToken cancellationToken)
    {
        var userId = GetUserId();
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(userId, cancellationToken);
        if (workspace is null)
        {
            return NotFound();
        }

        var original = await _db.ConversionJobs
            .Where(j => j.Id == jobId && j.WorkspaceId == workspace.Id)
            .Include(j => j.Items).ThenInclude(i => i.SourceDocument)
            .SingleOrDefaultAsync(cancellationToken);

        if (original is null)
        {
            return NotFound();
        }

        if (original.DeletedAtUtc is not null)
        {
            TempData["Message"] = "That conversion's files were deleted and cannot be converted again.";
            return RedirectToAction(nameof(Index));
        }

        var reusableDocuments = original.Items
            .Select(i => i.SourceDocument!)
            .Where(d => d.SourceBytesDeletedAtUtc == null)
            .ToList();

        if (reusableDocuments.Count == 0)
        {
            TempData["Message"] =
                "The original files have passed their retention period, so this conversion cannot "
                + "be run again. Upload them once more to convert them.";
            return RedirectToAction(nameof(Index));
        }

        var newJobId = await _intake.CreateJobFromExistingDocumentsAsync(
            workspace.Id, userId, preset ?? original.PresetName, reusableDocuments, cancellationToken);

        var skipped = original.Items.Count - reusableDocuments.Count;
        TempData["Message"] = skipped > 0
            ? $"Started a new conversion of {reusableDocuments.Count} file(s). {skipped} could not be "
              + "included because their source files have passed their retention period."
            : $"Started a new conversion of {reusableDocuments.Count} file(s).";

        _logger.LogInformation("Job {JobId} reconverted as {NewJobId}", jobId, newJobId);

        return RedirectToAction("Index", "Results", new { jobId = newJobId });
    }

    private static JobStatus? ParseStatus(string? status) =>
        Enum.TryParse<JobStatus>(status, ignoreCase: true, out var parsed) ? parsed : null;

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record HistoryRow(
    Guid Id,
    string Status,
    string PresetName,
    DateTime CreatedAtUtc,
    int FileCount,
    int WarningCount,
    int FailedCount,
    bool IsDeleted,
    bool HasDownloadableContent,
    IReadOnlyList<string> SampleFileNames);

public sealed record HistoryViewModel(
    IReadOnlyList<HistoryRow> Jobs,
    string? SearchTerm,
    string? StatusFilter,
    int Page,
    int TotalPages,
    int TotalCount);
