namespace AI.Document.Converter.Persistence.Billing;

// What the application asks a payment provider to do. Nothing here names a
// provider, and nothing here assumes one is available.
//
// SR-BIL-1: the seller may operate from Bangladesh, and merchant eligibility
// with any particular provider is NOT established. Writing the rest of the
// system against this interface means adopting a provider later is an additive
// change - one implementation and one registration - rather than a rewrite of
// everything that touches subscriptions. It also means the honest current
// answer, "no provider is approved", is expressible as an implementation rather
// than as a pile of half-finished integration code.
public interface IBillingProvider
{
    // Shown to operators and in logs. Never used to branch on behaviour - code
    // that switches on a provider name has stopped being provider-neutral.
    string Name { get; }

    // Whether this provider can currently take money. False keeps the product
    // usable (trials, allowances, conversions) while checkout stays closed.
    bool CanAcceptPayments { get; }

    Task<CheckoutStart> StartCheckoutAsync(CheckoutRequest request, CancellationToken cancellationToken);

    // Verifies that a callback genuinely came from the provider.
    //
    // The ONLY route by which a subscription may be granted or changed. A
    // request that merely claims a payment succeeded is not evidence - anyone
    // can post that - so this returns a rejection rather than a boolean the
    // caller might forget to check.
    Task<ProviderVerification> VerifyCallbackAsync(
        string rawPayload, IReadOnlyDictionary<string, string> headers, CancellationToken cancellationToken);
}

public sealed record CheckoutRequest(Guid WorkspaceId, string PlanCode, Uri ReturnUrl);

// Unavailable is a first-class outcome, not an exception, because "we cannot
// take payments yet" is the expected state today and the UI has to render it.
public sealed record CheckoutStart(Uri? RedirectUrl, string? UnavailableReason)
{
    public bool Started => RedirectUrl is not null;

    public static CheckoutStart Unavailable(string reason) => new(null, reason);
}

public enum ProviderEventKind
{
    SubscriptionActivated = 0,
    SubscriptionRenewed = 1,
    PaymentFailed = 2,

    // Requested, not effective. Access continues to the end of the paid period
    // (SR-BIL-3); treating this as immediate removal is the mistake the
    // separate Ended kind exists to prevent.
    CancellationScheduled = 3,

    SubscriptionEnded = 4
}

// One thing the provider says happened, normalised away from any provider's
// own vocabulary.
public sealed record ProviderEvent(
    // The provider's own id for this event. Carried so a redelivered webhook can
    // be recognised and ignored - providers retry, so duplicates are certain.
    string EventId,
    ProviderEventKind Kind,
    Guid WorkspaceId,
    string PlanCode,
    string ProviderSubscriptionId,
    DateTime PeriodStartsAtUtc,
    DateTime PeriodEndsAtUtc);

public sealed record ProviderVerification(ProviderEvent? Event, string? RejectionReason)
{
    public bool IsVerified => Event is not null;

    public static ProviderVerification Rejected(string reason) => new(null, reason);

    public static ProviderVerification Verified(ProviderEvent providerEvent) => new(providerEvent, null);
}
