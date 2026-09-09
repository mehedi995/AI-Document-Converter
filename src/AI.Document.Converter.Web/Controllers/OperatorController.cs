using System.Security.Claims;
using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Persistence.Operations;
using AI.Document.Converter.Web.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Document.Converter.Web.Controllers;

// The operator console (SaaS 5.11).
//
// Everything here crosses tenant boundaries by design, which is exactly why it
// is the most tightly gated part of the application:
//
//   - The policy demands the Operator role AND a session established with a
//      second factor (SR-SEC-7). See OperatorPolicy.
//   - The status pages return no filenames, no document text and no warning
//      messages. Running the service does not require reading anyone's
//      documents (SR-SEC-7: "Operator status alone must not reveal documents").
//   - The one page that shows customer-identifying detail demands a written
//      reason and records it before showing anything.
//
// It is deliberately read-only. Cancelling or retrying another tenant's job
// from here would be an action taken on a customer's behalf without their
// knowledge; if support needs that, it should be a separate, explicitly
// designed and audited capability rather than a convenient button next to a
// diagnostic table.
[Authorize(Policy = OperatorPolicy.Name)]
[Route("operator")]
public sealed class OperatorController : Controller
{
    private const int JobsShown = 50;
    private const int WorkspacesShown = 25;
    private const int AuditEntriesShown = 100;

    private readonly OperatorConsoleService _console;
    private readonly OperatorInspectionService _inspection;

    public OperatorController(OperatorConsoleService console, OperatorInspectionService inspection)
    {
        _console = console;
        _inspection = inspection;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(
        JobStatus? status, Guid? workspace, CancellationToken cancellationToken)
    {
        var nowUtc = DateTime.UtcNow;

        return View(new OperatorConsoleViewModel(
            await _console.GetQueueHealthAsync(nowUtc, cancellationToken),
            await _console.GetRecentJobsAsync(status, workspace, JobsShown, cancellationToken),
            await _console.GetWorkspaceUsageAsync(nowUtc, WorkspacesShown, cancellationToken),
            await _console.GetRetentionPostureAsync(nowUtc, cancellationToken),
            status,
            workspace));
    }

    // Deliberately a GET that shows only a form. The inspection itself is a
    // POST, so a job's details cannot be opened by following a link - which
    // also means an inspection cannot be triggered by a URL someone was sent.
    [HttpGet("jobs/{jobId:guid}/inspect")]
    public IActionResult Inspect(Guid jobId) =>
        View(new JobInspectionViewModel(jobId, null, null));

    [HttpPost("jobs/{jobId:guid}/inspect")]
    public async Task<IActionResult> Inspect(Guid jobId, string? reason, CancellationToken cancellationToken)
    {
        var (inspection, refusal) = await _inspection.InspectJobAsync(
            jobId,
            GetUserId(),
            User.FindFirstValue(ClaimTypes.Name) ?? User.Identity?.Name ?? "unknown",
            reason,
            HttpContext.Connection.RemoteIpAddress?.ToString(),
            DateTime.UtcNow,
            cancellationToken);

        return View(new JobInspectionViewModel(jobId, inspection, refusal?.Reason));
    }

    // Visible to the people it records, on purpose. An audit trail its subjects
    // cannot see is surveillance; one they can see is a deterrent.
    [HttpGet("audit")]
    public async Task<IActionResult> Audit(CancellationToken cancellationToken) =>
        View(await _inspection.GetAuditTrailAsync(AuditEntriesShown, cancellationToken));

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record OperatorConsoleViewModel(
    QueueHealth Queue,
    IReadOnlyList<OperatorJobRow> Jobs,
    IReadOnlyList<WorkspaceUsageRow> Workspaces,
    RetentionPosture Retention,
    JobStatus? StatusFilter,
    Guid? WorkspaceFilter);

public sealed record JobInspectionViewModel(Guid JobId, JobInspection? Inspection, string? Refusal);
