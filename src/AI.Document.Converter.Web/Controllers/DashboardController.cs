using System.Security.Claims;
using AI.Document.Converter.Persistence;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace AI.Document.Converter.Web.Controllers;

[Authorize]
[Route("dashboard")]
public sealed class DashboardController : Controller
{
    private readonly ConverterDbContext _db;
    private readonly WorkspaceAccessService _workspaceAccess;

    public DashboardController(ConverterDbContext db, WorkspaceAccessService workspaceAccess)
    {
        _db = db;
        _workspaceAccess = workspaceAccess;
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

        // SR-SEC-2: scoped by the workspace resolved from the signed-in user's
        // membership, never by an id taken off the request.
        var recentJobs = await _db.ConversionJobs
            .Where(j => j.WorkspaceId == workspace.Id)
            .OrderByDescending(j => j.CreatedAtUtc)
            .Take(10)
            .Select(j => new DashboardJobRow(
                j.Id,
                j.Status.ToString(),
                j.PresetName,
                j.CreatedAtUtc,
                j.Items.Count))
            .ToListAsync(cancellationToken);

        return View(new DashboardViewModel(workspace.Name, workspace.Slug, recentJobs));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record DashboardJobRow(
    Guid Id, string Status, string PresetName, DateTime CreatedAtUtc, int FileCount);

public sealed record DashboardViewModel(
    string WorkspaceName, string WorkspaceSlug, IReadOnlyList<DashboardJobRow> RecentJobs);
