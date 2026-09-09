using System.Security.Claims;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Web.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.Document.Converter.Web.Controllers;

// Shows a workspace what it is entitled to and what it has used.
//
// It does NOT show prices. PlanCatalog deliberately holds none: no commercial
// terms have been approved, and a number rendered on a page is a number
// customers will hold us to. Allowances are what the system enforces, so
// allowances are what this page states.
[Authorize]
[Route("billing")]
public sealed class BillingController : Controller
{
    private readonly WorkspaceAccessService _workspaceAccess;
    private readonly MeteringService _metering;
    private readonly IBillingProvider _provider;

    public BillingController(
        WorkspaceAccessService workspaceAccess, MeteringService metering, IBillingProvider provider)
    {
        _workspaceAccess = workspaceAccess;
        _metering = metering;
        _provider = provider;
    }

    [HttpGet("")]
    public async Task<IActionResult> Index(CancellationToken cancellationToken)
    {
        var workspace = await _workspaceAccess.GetDefaultWorkspaceAsync(GetUserId(), cancellationToken);

        if (workspace is null)
        {
            return View("~/Views/Dashboard/NoWorkspace.cshtml");
        }

        var period = await _metering.FindCurrentPeriodAsync(
            workspace.Id, DateTime.UtcNow, cancellationToken);

        return View(new BillingPageModel(
            PlanCatalog.All.OrderBy(p => p.IncludedCreditsPerPeriod).ToList(),
            period?.PlanCode,
            period?.IncludedCredits,
            period?.AvailableCredits,
            period?.SettledCredits,
            period?.EndsAtUtc,
            ConversionCredits.Describe(),
            // Rendered from the provider itself rather than from a setting, so
            // the page cannot say "buy now" while the boundary refuses
            // (SR-BIL-1).
            _provider.CanAcceptPayments,
            NoBillingProvider.UnavailableMessage));
    }

    private Guid GetUserId() =>
        Guid.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException("Authenticated request has no user id claim."));
}

public sealed record BillingPageModel(
    IReadOnlyList<Plan> Plans,
    string? CurrentPlanCode,
    long? IncludedCredits,
    long? AvailableCredits,
    long? UsedCredits,
    DateTime? PeriodEndsAtUtc,
    string CreditRule,
    bool CheckoutAvailable,
    string CheckoutUnavailableMessage);
