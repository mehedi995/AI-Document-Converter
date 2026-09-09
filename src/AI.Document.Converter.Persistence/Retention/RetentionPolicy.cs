namespace AI.Document.Converter.Persistence.Retention;

// SR-SEC-6. These values are DISCLOSED TO THE CUSTOMER before they upload, so
// they are configuration rather than scattered literals - and the upload page
// reads the same values the sweep enforces, so the promise and the behaviour
// cannot drift apart.
//
// The defaults are the ones proposed in the SaaS requirements: 24 hours for
// source bytes, 7 days for output bytes. Job metadata and billing records
// deliberately outlive both; history and invoices still have to make sense
// after the content is gone.
public sealed class RetentionPolicy
{
    public TimeSpan SourceBytes { get; set; } = TimeSpan.FromHours(24);

    public TimeSpan OutputBytes { get; set; } = TimeSpan.FromDays(7);

    // How often the sweep runs. Retention is "not longer than", so a sweep
    // interval adds latency to deletion and must stay well under the shorter
    // of the two windows.
    public TimeSpan SweepInterval { get; set; } = TimeSpan.FromMinutes(15);

    // Bounds one sweep pass so a large backlog is worked through steadily
    // instead of in one long transaction that holds locks and blocks uploads.
    public int MaxJobsPerSweep { get; set; } = 200;

    public string DescribeForCustomer() =>
        $"Source files are deleted after {Describe(SourceBytes)} and converted output after "
        + $"{Describe(OutputBytes)}. The record that a conversion happened is kept for your history.";

    // Hours up to two days, days beyond. The default source window is exactly
    // 24 hours, and "1 day" - while arithmetically identical - is a vaguer
    // promise than "24 hours" in a notice about when someone's files disappear.
    private static string Describe(TimeSpan span) =>
        span.TotalHours < 48
            ? $"{span.TotalHours:0.#} hour{(span.TotalHours == 1 ? string.Empty : "s")}"
            : $"{span.TotalDays:0.#} day{(span.TotalDays == 1 ? string.Empty : "s")}";
}
