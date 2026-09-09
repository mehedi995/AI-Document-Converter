using AI.Document.Converter.Persistence;
using AI.Document.Converter.Persistence.Billing;
using AI.Document.Converter.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace AI.Document.Converter.Web.Tests;

// SR-BIL-1, SR-BIL-2 and SR-BIL-3.
//
// No payment provider is approved for this seller, so there is no real
// integration to test. What CAN be tested - and matters more - is that the
// boundary refuses to grant anything without verified evidence, that a
// redelivered event does not apply twice, and that a scheduled cancellation
// does not cut access off early. Those rules are provider-independent, and
// getting them wrong costs customers money either way.
[Collection(nameof(PostgresCollection))]
public sealed class SubscriptionTests
{
    private readonly PostgresFixture _fixture;

    public SubscriptionTests(PostgresFixture fixture) => _fixture = fixture;

    private static readonly Dictionary<string, string> NoHeaders = new();

    // Stands in for an approved provider so the rules around verification can be
    // exercised. It is a TEST double, not a shipped implementation - nothing in
    // src registers it, and NoBillingProvider is what the application uses.
    private sealed class FakeProvider(ProviderEvent? verifiedAs) : IBillingProvider
    {
        public string Name => "fake";

        public bool CanAcceptPayments => true;

        public Task<CheckoutStart> StartCheckoutAsync(
            CheckoutRequest request, CancellationToken cancellationToken) =>
            Task.FromResult(new CheckoutStart(new Uri("https://provider.example/checkout"), null));

        public Task<ProviderVerification> VerifyCallbackAsync(
            string rawPayload,
            IReadOnlyDictionary<string, string> headers,
            CancellationToken cancellationToken) =>
            Task.FromResult(verifiedAs is null
                ? ProviderVerification.Rejected("signature did not match")
                : ProviderVerification.Verified(verifiedAs));
    }

    private static SubscriptionService Service(ConverterDbContext db, IBillingProvider provider) =>
        new(db, provider, NullLogger<SubscriptionService>.Instance);

    private static async Task<Guid> SeedWorkspaceAsync(ConverterDbContext db)
    {
        var workspaceId = Guid.NewGuid();
        db.Workspaces.Add(new Workspace
        {
            Id = workspaceId,
            Name = "w",
            Slug = $"ws-{Guid.NewGuid():N}"[..15],
            CreatedAtUtc = DateTime.UtcNow
        });
        await db.SaveChangesAsync();
        return workspaceId;
    }

    private static ProviderEvent Event(
        Guid workspaceId, ProviderEventKind kind, string eventId, DateTime nowUtc) =>
        new(eventId, kind, workspaceId, "standard@v1", "sub_123", nowUtc, nowUtc.AddDays(30));

    // SR-BIL-1. The shipped provider must refuse, not pretend.
    [Fact]
    public async Task CheckoutIsDisabledUntilAProviderIsApproved()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);

        var outcome = await Service(db, new NoBillingProvider()).StartCheckoutAsync(
            workspaceId, "standard", new Uri("https://app.example/done"), CancellationToken.None);

        Assert.False(outcome.Started);
        Assert.Null(outcome.RedirectUrl);
        Assert.Contains("No payment provider has been approved", outcome.UnavailableReason);
    }

    // The worst available failure: granting a paid plan to someone who posted a
    // convincing-looking request.
    [Fact]
    public async Task AnUnverifiedCallbackGrantsNothing()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);

        var outcome = await Service(db, new NoBillingProvider()).HandleCallbackAsync(
            """{"type":"subscription.activated","workspace":"whatever"}""",
            NoHeaders,
            DateTime.UtcNow,
            CancellationToken.None);

        Assert.False(outcome.Applied);
        Assert.NotNull(outcome.Rejection);

        Assert.False(await db.Subscriptions.AnyAsync(s => s.WorkspaceId == workspaceId));
        // Scoped: the fixture database is shared, so an unfiltered query here
        // sees every other test's events.
        Assert.False(await db.ProviderEvents.AnyAsync(e => e.WorkspaceId == workspaceId));
    }

    [Fact]
    public async Task AVerifiedActivationGrantsThePlan()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var outcome = await Service(
                db,
                new FakeProvider(Event(workspaceId, ProviderEventKind.SubscriptionActivated, "evt_1", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        Assert.True(outcome.Applied);

        var subscription = await db.Subscriptions.SingleAsync(s => s.WorkspaceId == workspaceId);
        Assert.Equal(SubscriptionStatus.Active, subscription.Status);
        Assert.Equal("standard@v1", subscription.PlanCode);
        Assert.Equal("sub_123", subscription.ProviderSubscriptionId);
    }

    // Providers retry until acknowledged, so this is routine, not exotic.
    // Applying a renewal twice would give a customer two periods for one payment.
    [Fact]
    public async Task ARedeliveredEventIsNotAppliedTwice()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        var providerEvent = Event(workspaceId, ProviderEventKind.SubscriptionRenewed, "evt_dup", nowUtc);
        var service = Service(db, new FakeProvider(providerEvent));

        var first = await service.HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);
        var second = await service.HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        Assert.True(first.Applied);
        Assert.False(second.Applied);
        Assert.True(second.WasDuplicate);

        Assert.Equal(1, await db.ProviderEvents.CountAsync(e => e.EventId == "evt_dup"));
    }

    // SR-BIL-3, stated explicitly because conflating these two is the named
    // mistake: a customer who cancels keeps what they already paid for.
    [Fact]
    public async Task AScheduledCancellationKeepsAccessUntilThePeriodEnds()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        await Service(db, new FakeProvider(Event(workspaceId, ProviderEventKind.SubscriptionActivated, "evt_a", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        await Service(db, new FakeProvider(Event(workspaceId, ProviderEventKind.CancellationScheduled, "evt_c", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        var subscription = await db.Subscriptions.SingleAsync(s => s.WorkspaceId == workspaceId);

        Assert.Equal(SubscriptionStatus.CancellationScheduled, subscription.Status);
        Assert.Equal(subscription.CurrentPeriodEndsAtUtc, subscription.AccessEndsAtUtc);

        // Still theirs today; gone once the paid period is over.
        Assert.True(SubscriptionService.HasAccess(subscription, nowUtc.AddDays(1)));
        Assert.False(SubscriptionService.HasAccess(subscription, nowUtc.AddDays(31)));
    }

    // A failed payment is usually an expired card, not a departing customer.
    [Fact]
    public async Task AFailedPaymentDoesNotImmediatelyRemoveAccess()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        await Service(db, new FakeProvider(Event(workspaceId, ProviderEventKind.SubscriptionActivated, "evt_a2", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        await Service(db, new FakeProvider(Event(workspaceId, ProviderEventKind.PaymentFailed, "evt_f", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        var subscription = await db.Subscriptions.SingleAsync(s => s.WorkspaceId == workspaceId);

        Assert.Equal(SubscriptionStatus.PastDue, subscription.Status);
        Assert.True(SubscriptionService.HasAccess(subscription, nowUtc.AddDays(1)));
    }

    // Ending is the event that actually removes access, and it is a different
    // event from scheduling a cancellation.
    [Fact]
    public async Task AnEndedSubscriptionLosesAccess()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);
        var nowUtc = DateTime.UtcNow;

        await Service(db, new FakeProvider(Event(workspaceId, ProviderEventKind.SubscriptionActivated, "evt_a3", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        await Service(db, new FakeProvider(Event(workspaceId, ProviderEventKind.SubscriptionEnded, "evt_e", nowUtc)))
            .HandleCallbackAsync("{}", NoHeaders, nowUtc, CancellationToken.None);

        var subscription = await db.Subscriptions.SingleAsync(s => s.WorkspaceId == workspaceId);

        Assert.Equal(SubscriptionStatus.Cancelled, subscription.Status);
        Assert.False(SubscriptionService.HasAccess(subscription, nowUtc.AddMinutes(1)));
    }

    // A plan code arrives from a form. An unknown one is a data problem and must
    // not quietly become a checkout for something that does not exist.
    [Fact]
    public async Task CheckoutRefusesAnUnknownPlan()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            Service(db, new FakeProvider(null)).StartCheckoutAsync(
                workspaceId, "enterprise", new Uri("https://app.example/done"), CancellationToken.None));
    }

    [Fact]
    public async Task TheTrialDoesNotGoThroughCheckout()
    {
        await using var db = _fixture.CreateContext();
        var workspaceId = await SeedWorkspaceAsync(db);

        var outcome = await Service(db, new FakeProvider(null)).StartCheckoutAsync(
            workspaceId, PlanCatalog.TrialCode, new Uri("https://app.example/done"), CancellationToken.None);

        Assert.False(outcome.Started);
        Assert.Contains("does not require a payment", outcome.UnavailableReason);
    }
}
