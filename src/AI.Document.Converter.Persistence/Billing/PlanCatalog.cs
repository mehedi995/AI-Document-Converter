namespace AI.Document.Converter.Persistence.Billing;

public sealed record Plan(
    string Code,
    string Version,
    string DisplayName,
    string Description,
    long IncludedCreditsPerPeriod,
    int MaxFilesPerJob,
    long MaxFileSizeBytes,
    int IncludedSeats,
    bool IsTrial,
    TimeSpan PeriodLength)
{
    // Code plus version. A ledger entry records this, so a plan change never
    // retroactively rewrites how earlier usage was priced (SR-BIL-6).
    public string VersionedCode => $"{Code}@{Version}";
}

// SR-BIL-6: plans, allowances, seats and limits live in SERVER-CONTROLLED,
// VERSIONED configuration.
//
// PRICES ARE DELIBERATELY ABSENT. The SaaS requirements are explicit that
// figures in a blueprint are illustrative and must not be published as live
// commercial terms, and no provider has been approved for a Bangladesh seller
// yet. Allowances here define what the SYSTEM enforces; what any of it costs is
// a commercial decision that has not been made, and inventing a number here
// would be the fastest way for one to end up on a pricing page.
//
// A static table rather than database rows: plans change with a deployment, and
// keeping them in code means a plan referenced by an old ledger entry cannot be
// edited out of existence.
public static class PlanCatalog
{
    public const string TrialCode = "trial";

    private static readonly Dictionary<string, Plan> ByCode = new(StringComparer.OrdinalIgnoreCase)
    {
        [TrialCode] = new(
            TrialCode,
            "v1",
            "Trial",
            "Try the converter with a limited allowance.",
            IncludedCreditsPerPeriod: 50,
            MaxFilesPerJob: 5,
            MaxFileSizeBytes: 25L * 1024 * 1024,
            IncludedSeats: 1,
            IsTrial: true,
            PeriodLength: TimeSpan.FromDays(14)),

        ["standard"] = new(
            "standard",
            "v1",
            "Standard",
            "For regular document preparation.",
            IncludedCreditsPerPeriod: 2_000,
            MaxFilesPerJob: 25,
            MaxFileSizeBytes: 100L * 1024 * 1024,
            IncludedSeats: 1,
            IsTrial: false,
            // Monthly entitlement periods, defined even for an annual
            // subscription so allowance is not front-loaded into month one
            // (SR-BIL-6).
            PeriodLength: TimeSpan.FromDays(30)),

        ["team"] = new(
            "team",
            "v1",
            "Team",
            "Shared workspace with a larger allowance.",
            IncludedCreditsPerPeriod: 10_000,
            MaxFilesPerJob: 100,
            MaxFileSizeBytes: 100L * 1024 * 1024,
            IncludedSeats: 5,
            IsTrial: false,
            PeriodLength: TimeSpan.FromDays(30))
    };

    public static IReadOnlyCollection<Plan> All => ByCode.Values;

    public static Plan Trial => ByCode[TrialCode];

    // Throws rather than falling back. A subscription referencing an unknown
    // plan is a data problem, and quietly substituting the trial would either
    // over-restrict a paying customer or under-charge them - both worse than a
    // loud failure.
    public static Plan Require(string code) =>
        ByCode.TryGetValue(code, out var plan)
            ? plan
            : throw new InvalidOperationException(
                $"Unknown plan code '{code}'. Plans are versioned in PlanCatalog and must not be "
                + "removed while any subscription or ledger entry still references them.");

    public static bool IsKnown(string? code) => code is not null && ByCode.ContainsKey(code);
}
