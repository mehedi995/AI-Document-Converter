using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace AI.Document.Converter.Persistence.Billing;

public sealed record CallbackOutcome(bool Applied, bool WasDuplicate, string? Rejection);

// Turns verified provider evidence into entitlements (SR-BIL-2, SR-BIL-3).
//
// Two rules hold this together, and both are about not trusting the caller:
//
//   1. Entitlements change ONLY through VerifyCallbackAsync. There is no method
//      here that grants a plan from a request body, because a request body is
//      something anybody can write. The provider's signature is the evidence.
//
//   2. Every applied event is recorded by its provider event id under a unique
//      index. Providers retry deliveries, so the same event WILL arrive twice;
//      the database rejects the second one rather than extending a subscription
//      by another period each time it is redelivered.
public sealed class SubscriptionService
{
    private readonly ConverterDbContext _db;
    private readonly IBillingProvider _provider;
    private readonly ILogger<SubscriptionService> _logger;

    public SubscriptionService(
        ConverterDbContext db, IBillingProvider provider, ILogger<SubscriptionService> logger)
    {
        _db = db;
        _provider = provider;
        _logger = logger;
    }

    public async Task<CheckoutStart> StartCheckoutAsync(
        Guid workspaceId, string planCode, Uri returnUrl, CancellationToken cancellationToken)
    {
        // Checked against the catalogue before the provider is asked for
        // anything. A plan code arrives from a form, and a request for
        // "enterprise@v9" must not become a checkout for something that does
        // not exist.
        var plan = PlanCatalog.Require(planCode);

        if (plan.IsTrial)
        {
            return CheckoutStart.Unavailable("The trial does not require a payment.");
        }

        return await _provider.StartCheckoutAsync(
            new CheckoutRequest(workspaceId, plan.VersionedCode, returnUrl), cancellationToken);
    }

    public async Task<CallbackOutcome> HandleCallbackAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        DateTime nowUtc,
        CancellationToken cancellationToken)
    {
        var verification = await _provider.VerifyCallbackAsync(rawPayload, headers, cancellationToken);

        if (!verification.IsVerified)
        {
            // Logged without the payload. An unverified callback is exactly the
            // kind of thing an attacker sends, and writing its contents into the
            // log would let them choose what appears there.
            _logger.LogWarning(
                "Rejected an unverified billing callback: {Reason}", verification.RejectionReason);

            return new CallbackOutcome(false, false, verification.RejectionReason);
        }

        var providerEvent = verification.Event!;

        if (await _db.ProviderEvents.AnyAsync(
                e => e.ProviderName == _provider.Name && e.EventId == providerEvent.EventId,
                cancellationToken))
        {
            _logger.LogInformation(
                "Provider event {EventId} already applied; ignoring redelivery", providerEvent.EventId);
            return new CallbackOutcome(false, true, null);
        }

        var subscription = await _db.Subscriptions
            .SingleOrDefaultAsync(s => s.WorkspaceId == providerEvent.WorkspaceId, cancellationToken);

        if (subscription is null)
        {
            subscription = new Subscription
            {
                Id = Guid.NewGuid(),
                WorkspaceId = providerEvent.WorkspaceId,
                PlanCode = providerEvent.PlanCode,
                Status = SubscriptionStatus.Active,
                CurrentPeriodStartsAtUtc = providerEvent.PeriodStartsAtUtc,
                CurrentPeriodEndsAtUtc = providerEvent.PeriodEndsAtUtc,
                ProviderName = _provider.Name,
                ProviderSubscriptionId = providerEvent.ProviderSubscriptionId,
                CreatedAtUtc = nowUtc
            };
            _db.Subscriptions.Add(subscription);
        }

        Apply(providerEvent, subscription, nowUtc);

        _db.ProviderEvents.Add(new ProviderEventRecord
        {
            Id = Guid.NewGuid(),
            ProviderName = _provider.Name,
            EventId = providerEvent.EventId,
            Kind = providerEvent.Kind.ToString(),
            WorkspaceId = providerEvent.WorkspaceId,
            ReceivedAtUtc = nowUtc
        });

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Two deliveries of the same event arrived at once. The unique index
            // is the authority; the loser is a duplicate, not a failure.
            _logger.LogInformation(
                "Concurrent duplicate of provider event {EventId} rejected by the database",
                providerEvent.EventId);
            return new CallbackOutcome(false, true, null);
        }

        _logger.LogInformation(
            "Applied {Kind} for workspace {WorkspaceId}", providerEvent.Kind, providerEvent.WorkspaceId);

        return new CallbackOutcome(true, false, null);
    }

    private static void Apply(ProviderEvent providerEvent, Subscription subscription, DateTime nowUtc)
    {
        subscription.ProviderSubscriptionId = providerEvent.ProviderSubscriptionId;

        switch (providerEvent.Kind)
        {
            case ProviderEventKind.SubscriptionActivated:
            case ProviderEventKind.SubscriptionRenewed:
                subscription.PlanCode = providerEvent.PlanCode;
                subscription.Status = SubscriptionStatus.Active;
                subscription.CurrentPeriodStartsAtUtc = providerEvent.PeriodStartsAtUtc;
                subscription.CurrentPeriodEndsAtUtc = providerEvent.PeriodEndsAtUtc;

                // A renewal after a failed payment clears the cancellation that
                // the failure was heading towards.
                subscription.CancellationRequestedAtUtc = null;
                subscription.AccessEndsAtUtc = null;
                break;

            case ProviderEventKind.PaymentFailed:
                // Access CONTINUES. A failed payment is often a expired card,
                // not a departing customer, and cutting them off immediately
                // would destroy a relationship over a retryable problem
                // (SR-BIL-3).
                subscription.Status = SubscriptionStatus.PastDue;
                break;

            case ProviderEventKind.CancellationScheduled:
                // Requested, not effective. They paid for this period and keep
                // it. Conflating this with SubscriptionEnded is precisely the
                // error SR-BIL-3 warns about.
                subscription.Status = SubscriptionStatus.CancellationScheduled;
                subscription.CancellationRequestedAtUtc = nowUtc;
                subscription.AccessEndsAtUtc = subscription.CurrentPeriodEndsAtUtc;
                break;

            case ProviderEventKind.SubscriptionEnded:
                subscription.Status = SubscriptionStatus.Cancelled;
                subscription.AccessEndsAtUtc = nowUtc;
                break;

            default:
                // An unrecognised event must not be silently swallowed as
                // "applied": that would record it as handled and never look at
                // it again.
                throw new NotSupportedException(
                    $"No handling is defined for provider event kind {providerEvent.Kind}.");
        }
    }

    // Whether the workspace may currently use paid features. Reads the dates,
    // not just the status, so a scheduled cancellation keeps working until the
    // period it was paid for actually ends.
    public static bool HasAccess(Subscription? subscription, DateTime nowUtc) => subscription switch
    {
        null => false,
        { AccessEndsAtUtc: { } endsAt } => nowUtc < endsAt,
        { Status: SubscriptionStatus.Active or SubscriptionStatus.Trialing
            or SubscriptionStatus.PastDue } => nowUtc < subscription.CurrentPeriodEndsAtUtc,
        _ => false
    };

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is Npgsql.PostgresException { SqlState: "23505" };
}
