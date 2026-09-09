using AI.Document.Converter.Persistence.Entities;
using AI.Document.Converter.Web.Controllers;

namespace AI.Document.Converter.Web.Tests;

// SaaS §10: "Show real stage progress rather than fabricated percentages or
// animated success."
//
// These test the row's own reasoning, which is where the honesty lives - the
// database query that feeds it is covered by the cross-tenant tests.
public sealed class DashboardProgressTests
{
    private static DashboardJobRow Row(
        int files, int completed = 0, int warnings = 0, int failed = 0,
        int cancelled = 0, int inProgress = 0, int queued = 0,
        string status = "Queued", bool deleted = false) =>
        new(Guid.NewGuid(), status, "default", DateTime.UtcNow,
            files, completed, warnings, failed, cancelled, inProgress, queued, deleted);

    // The progress text is a COUNT of finished files, never a percentage of
    // elapsed work. Nothing knows how far through the current document the
    // engine is, so nothing claims to.
    [Fact]
    public void ProgressWhileRunningReportsFinishedFilesOutOfTotal()
    {
        var row = Row(files: 5, completed: 2, inProgress: 1, queued: 2);

        Assert.Equal("2 of 5 files finished", row.ProgressDescription);
        Assert.True(row.IsInFlight);
    }

    [Fact]
    public void EveryTerminalOutcomeCountsTowardsFinished()
    {
        // A cancelled or failed file is finished - it will not change again.
        // Counting only successes would leave a job stuck at "2 of 5" forever.
        var row = Row(files: 5, completed: 2, warnings: 1, failed: 1, cancelled: 1);

        Assert.Equal(5, row.FinishedCount);
        Assert.False(row.IsInFlight);
    }

    [Fact]
    public void AFinishedJobDescribesItsSizeRatherThanProgress()
    {
        Assert.Equal("2 files", Row(files: 2, completed: 2, status: "Completed").ProgressDescription);
        Assert.Equal("1 file", Row(files: 1, completed: 1, status: "Completed").ProgressDescription);
    }

    // A queued item has not started. Reporting it as "converting" would show
    // activity that is not happening.
    [Fact]
    public void QueuedCountsAsInFlightButNotAsInProgress()
    {
        var row = Row(files: 3, queued: 3);

        Assert.True(row.IsInFlight);
        Assert.Equal(0, row.InProgressCount);
        Assert.Equal("0 of 3 files finished", row.ProgressDescription);
    }

    [Fact]
    public void AJobWithNothingLeftToDoIsNotInFlight()
    {
        Assert.False(Row(files: 2, completed: 1, failed: 1, status: "CompletedWithWarnings").IsInFlight);
    }

    // The counts the view renders as separate badges. Folding warnings into the
    // completed total is the exact misreport the warnings channel exists to
    // prevent.
    [Fact]
    public void WarningsAreCountedSeparatelyFromCleanCompletions()
    {
        var row = Row(files: 3, completed: 1, warnings: 2, status: "CompletedWithWarnings");

        Assert.Equal(1, row.CompletedCount);
        Assert.Equal(2, row.WarningCount);
        Assert.NotEqual(row.FinishedCount, row.CompletedCount);
    }
}
