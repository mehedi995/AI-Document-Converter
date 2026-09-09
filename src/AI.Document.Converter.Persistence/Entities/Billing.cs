namespace AI.Document.Converter.Persistence.Entities;

public enum SubscriptionStatus
{
    Trialing = 0,
    Active = 1,

    // Payment failed but access continues during a grace window. Distinct from
    // Cancelled because the customer has not left (SR-BIL-3).
    PastDue = 2,

    // Cancelled at period end. Access CONTINUES until CurrentPeriodEndsAtUtc -
    // conflating this with immediate removal is the mistake SR-BIL-3 names
    // explicitly.
    CancellationScheduled = 3,

    Cancelled = 4,
    Expired = 5
}

// One workspace's commercial state. Provider-neutral: no provider is approved
// for a Bangladesh seller yet, so nothing here names one. ProviderSubscriptionId
// is a nullable opaque string precisely so adopting a provider is an additive
// change rather than a schema rewrite.
public sealed class Subscription
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    // Versioned plan code (e.g. "standard@v1"). Stored as text, not a foreign
    // key: a plan referenced by history must remain readable after that plan
    // stops being sold.
    public required string PlanCode { get; set; }

    public required SubscriptionStatus Status { get; set; }

    public required DateTime CurrentPeriodStartsAtUtc { get; set; }

    public required DateTime CurrentPeriodEndsAtUtc { get; set; }

    // When access actually ends. Separate from a cancellation request, because
    // "cancel at period end" must keep working until that date (SR-BIL-3).
    public DateTime? AccessEndsAtUtc { get; set; }

    public DateTime? CancellationRequestedAtUtc { get; set; }

    // Opaque identifier from whichever provider is eventually approved. Null
    // until then, and never interpreted by this system.
    public string? ProviderSubscriptionId { get; set; }

    public string? ProviderName { get; set; }

    public required DateTime CreatedAtUtc { get; init; }

    public uint Version { get; set; }

    public Workspace? Workspace { get; set; }
}

// One entitlement window. Monthly even for an annual subscription, so allowance
// is not front-loaded into the first month (SR-BIL-6).
//
// This row is the concurrency point for the whole metering system: reservations
// and settlements both update it, and it is what stops two simultaneous jobs
// from overspending.
public sealed class UsagePeriod
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required DateTime StartsAtUtc { get; init; }

    public required DateTime EndsAtUtc { get; init; }

    // Copied from the plan at period start, NOT read live. A mid-period plan
    // change must not retroactively alter what this period allowed.
    public required string PlanCode { get; init; }

    public required long IncludedCredits { get; init; }

    // Credits held for accepted-but-unfinished work. Reserved is not yet spent:
    // it is released if the work fails or is cancelled.
    public long ReservedCredits { get; set; }

    // Credits actually consumed by published output.
    public long SettledCredits { get; set; }

    public required DateTime CreatedAtUtc { get; init; }

    public uint Version { get; set; }

    // What remains. Reserved counts against the allowance, otherwise two
    // concurrent jobs could each see the full balance and both proceed.
    public long AvailableCredits => IncludedCredits - ReservedCredits - SettledCredits;
}

public enum ReservationStatus
{
    Held = 0,
    Settled = 1,
    Released = 2
}

// A hold placed when a job is accepted (SR-BIL-5). Every reservation ends up
// either settled or released - never simply forgotten, which would leak
// allowance the customer paid for.
public sealed class UsageReservation
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Guid UsagePeriodId { get; init; }

    public required Guid JobId { get; init; }

    public required long EstimatedCredits { get; init; }

    public required ReservationStatus Status { get; set; }

    public required string PolicyVersion { get; init; }

    public required DateTime CreatedAtUtc { get; init; }

    public DateTime? ResolvedAtUtc { get; set; }

    public UsagePeriod? UsagePeriod { get; set; }
}

public enum LedgerEntryKind
{
    Charge = 0,
    Refund = 1,
    Adjustment = 2
}

// The immutable record of what was actually charged (SR-BIL-5).
//
// Append-only: a correction is a new Refund or Adjustment entry, never an edit.
// An edited ledger cannot be audited, and a customer disputing a charge needs
// to see what happened rather than what the current state implies.
public sealed class UsageLedgerEntry
{
    public Guid Id { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required Guid UsagePeriodId { get; init; }

    public Guid? JobId { get; init; }

    public Guid? JobItemId { get; init; }

    public required LedgerEntryKind Kind { get; init; }

    public required long Credits { get; init; }

    // What was counted, so a charge can be explained: "3 pages", "512 cells".
    public required string BasisDescription { get; init; }

    // The credit policy AND plan version in force when this was priced. Without
    // them, a rule change makes historical entries unexplainable (SR-BIL-6).
    public required string PolicyVersion { get; init; }

    public required string PlanCode { get; init; }

    // The uniqueness key that makes settlement safe under at-least-once
    // delivery. A duplicate worker message, a webhook replay or a retried
    // publish all produce the same key, and the unique index turns the second
    // attempt into a no-op rather than a double charge (SR-BIL-5).
    public required string IdempotencyKey { get; init; }

    public required DateTime CreatedAtUtc { get; init; }
}

// Every provider callback that was actually applied.
//
// Exists solely so a redelivered webhook is recognised as one. Providers retry
// until they get an acknowledgement, so the same event arrives more than once as
// a matter of course - and applying "subscription renewed" twice would extend a
// customer's period by two months for one payment.
//
// Append-only, like the usage ledger: it is the record of what was acted on, and
// editing it would destroy the only evidence of why an entitlement changed.
public sealed class ProviderEventRecord
{
    public Guid Id { get; init; }

    // Scoped by provider as well as by id, because event ids are only unique
    // within the provider that issued them.
    public required string ProviderName { get; init; }

    public required string EventId { get; init; }

    public required string Kind { get; init; }

    public required Guid WorkspaceId { get; init; }

    public required DateTime ReceivedAtUtc { get; init; }
}
