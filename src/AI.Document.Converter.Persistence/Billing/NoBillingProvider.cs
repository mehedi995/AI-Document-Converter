namespace AI.Document.Converter.Persistence.Billing;

// The provider registered today, because there is no approved one.
//
// SR-BIL-1 requires live checkout to be clearly disabled until a provider is
// verified as available to this seller. This makes that state explicit and
// impossible to switch on by accident: there is no configuration flag that
// turns it into a working integration, and no half-written provider code
// sitting behind an `if` waiting to be enabled by mistake.
//
// It refuses rather than pretends. A stub that returned a fake success would
// grant entitlements nobody paid for, which is the single worst failure mode
// available to a billing system.
public sealed class NoBillingProvider : IBillingProvider
{
    public const string UnavailableMessage =
        "Paid plans are not available yet. No payment provider has been approved for this "
        + "seller, so checkout is disabled. Existing allowances and conversions are unaffected.";

    public string Name => "none";

    public bool CanAcceptPayments => false;

    public Task<CheckoutStart> StartCheckoutAsync(
        CheckoutRequest request, CancellationToken cancellationToken) =>
        Task.FromResult(CheckoutStart.Unavailable(UnavailableMessage));

    // Refuses everything. With no provider there is no signing secret, so
    // nothing CAN be verified - and an unverifiable callback must never be
    // treated as evidence of payment. An endpoint that is reachable but always
    // rejects is safer than one that is absent and might be added later without
    // verification.
    public Task<ProviderVerification> VerifyCallbackAsync(
        string rawPayload,
        IReadOnlyDictionary<string, string> headers,
        CancellationToken cancellationToken) =>
        Task.FromResult(ProviderVerification.Rejected(
            "No payment provider is configured, so no callback can be verified as genuine."));
}
