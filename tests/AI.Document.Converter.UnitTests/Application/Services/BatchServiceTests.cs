using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Application.Services;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;
using Xunit;

namespace AI.Document.Converter.UnitTests.Application.Services;

public class BatchServiceTests
{
    private readonly BatchService _service = new();

    // Progress<T> marshals via a captured SynchronizationContext and posts
    // asynchronously even with no UI thread present, which would make
    // ordering assertions flaky here - this test double invokes synchronously
    // and immediately instead, a standard pattern for testing IProgress<T>
    // consumers deterministically.
    private sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _callback;

        public SynchronousProgress(Action<T> callback) => _callback = callback;

        public void Report(T value) => _callback(value);
    }

    private static List<string> FilePaths(int count) =>
        Enumerable.Range(1, count).Select(i => $"file{i}.txt").ToList();

    [Fact]
    public async Task RunAsync_AllSucceed_ReturnsMatchingSummary()
    {
        var filePaths = FilePaths(5);

        var summary = await _service.RunAsync(
            filePaths,
            (_, _) => Task.FromResult(new ConversionResult { Success = true }),
            maxParallelism: 2,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.Equal(5, summary.TotalFiles);
        Assert.Equal(5, summary.ProcessedFiles);
        Assert.Equal(5, summary.SuccessCount);
        Assert.Equal(0, summary.FailureCount);
    }

    // SR-JOB-4 (audit D-03). Before this, a file that completed but lost
    // content to a scanned page was counted as a plain success, so a batch
    // could report "10 converted" when several outputs were incomplete.
    [Fact]
    public async Task RunAsync_FileCompletedWithErrorSeverityWarning_CountsAsWarningNotSuccess()
    {
        var incomplete = new ConversionResult
        {
            Success = true,
            Warnings =
            [
                new ExtractionWarning
                {
                    Code = WarningCode.NoExtractableText,
                    Severity = WarningSeverity.Error,
                    Message = "Scanned page, no text recovered."
                }
            ]
        };

        var summary = await _service.RunAsync(
            ["a.pdf", "b.pdf"],
            (path, _) => Task.FromResult(
                path == "a.pdf" ? incomplete : new ConversionResult { Success = true }),
            maxParallelism: 1,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.Equal(2, summary.ProcessedFiles);
        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(1, summary.WarningCount);
        Assert.Equal(0, summary.FailureCount);
        // The four buckets are mutually exclusive and must account for every
        // processed file, or a summary can silently lose one.
        Assert.Equal(
            summary.ProcessedFiles,
            summary.SuccessCount + summary.WarningCount + summary.FailureCount);
    }

    // An Info/Warning-severity note (e.g. an omitted image) means nothing was
    // lost, so it must NOT demote the file out of the success count.
    [Fact]
    public async Task RunAsync_FileWithNonErrorWarning_StillCountsAsSuccess()
    {
        var withNote = new ConversionResult
        {
            Success = true,
            Warnings =
            [
                new ExtractionWarning
                {
                    Code = WarningCode.ImageOmitted,
                    Severity = WarningSeverity.Info,
                    Message = "1 image was not extracted."
                }
            ]
        };

        var summary = await _service.RunAsync(
            ["a.pdf"],
            (_, _) => Task.FromResult(withNote),
            maxParallelism: 1,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.Equal(1, summary.SuccessCount);
        Assert.Equal(0, summary.WarningCount);
    }

    [Fact]
    public async Task RunAsync_MixedResults_CountsSuccessAndFailureSeparately()
    {
        var filePaths = FilePaths(6);
        var index = 0;

        var summary = await _service.RunAsync(
            filePaths,
            (_, _) =>
            {
                var succeed = Interlocked.Increment(ref index) % 2 == 0;
                return Task.FromResult(new ConversionResult
                {
                    Success = succeed,
                    Error = succeed ? null : ErrorCategory.CorruptedDocument
                });
            },
            maxParallelism: 3,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.Equal(6, summary.TotalFiles);
        Assert.Equal(6, summary.ProcessedFiles);
        Assert.Equal(3, summary.SuccessCount);
        Assert.Equal(3, summary.FailureCount);
    }

    [Fact]
    public async Task RunAsync_OneFileFails_DoesNotStopTheRest()
    {
        // BR-006, at the batch-orchestration layer specifically.
        var filePaths = FilePaths(4);

        var summary = await _service.RunAsync(
            filePaths,
            (path, _) => Task.FromResult(new ConversionResult
            {
                Success = path != "file2.txt",
                Error = path == "file2.txt" ? ErrorCategory.CorruptedDocument : null
            }),
            maxParallelism: 4,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.Equal(4, summary.ProcessedFiles);
        Assert.Equal(3, summary.SuccessCount);
        Assert.Equal(1, summary.FailureCount);
    }

    [Fact]
    public async Task RunAsync_NeverExceedsConfiguredMaxParallelism()
    {
        var currentConcurrency = 0;
        var maxObservedConcurrency = 0;
        var concurrencyLock = new object();

        async Task<ConversionResult> Operation(string path, CancellationToken ct)
        {
            lock (concurrencyLock)
            {
                currentConcurrency++;
                maxObservedConcurrency = Math.Max(maxObservedConcurrency, currentConcurrency);
            }

            await Task.Delay(50, ct);

            lock (concurrencyLock)
            {
                currentConcurrency--;
            }

            return new ConversionResult { Success = true };
        }

        await _service.RunAsync(
            FilePaths(10),
            Operation,
            maxParallelism: 3,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.True(maxObservedConcurrency <= 3, $"Observed {maxObservedConcurrency} concurrent operations.");
        Assert.True(maxObservedConcurrency > 1, "Expected some real parallelism to have occurred.");
    }

    [Fact]
    public async Task RunAsync_ReportsStartedThenCompletedForEachFile()
    {
        var updates = new List<BatchProgressUpdate>();

        await _service.RunAsync(
            ["a.txt"],
            (_, _) => Task.FromResult(new ConversionResult { Success = true }),
            maxParallelism: 1,
            new SynchronousProgress<BatchProgressUpdate>(u => updates.Add(u)),
            CancellationToken.None);

        Assert.Equal(2, updates.Count);
        Assert.Equal(BatchItemState.Started, updates[0].State);
        Assert.Equal(BatchItemState.Completed, updates[1].State);
        Assert.Equal(1, updates[1].ProcessedCount);
        Assert.Equal(1, updates[1].SuccessCount);
    }

    // FR-037: cancelling must stop files that have not started yet.
    //
    // Cancellation is triggered by the work itself once a set number of files
    // have begun, NOT by a wall-clock timer. The original version used
    // CancelAfter(120) racing a 20-file batch, which made the assertion depend
    // on machine load - it was observed failing once on a loaded machine and
    // passing on five consecutive re-runs. A timing race in a test eventually
    // trains people to re-run instead of investigate, so the trigger is now
    // deterministic.
    [Fact]
    public async Task RunAsync_Cancelled_StopsStartingNewFilesAndReturnsPartialSummary()
    {
        const int totalFiles = 20;
        const int cancelAfterStarted = 2;

        var startedCount = 0;
        using var cts = new CancellationTokenSource();

        async Task<ConversionResult> Operation(string path, CancellationToken ct)
        {
            if (Interlocked.Increment(ref startedCount) >= cancelAfterStarted)
            {
                await cts.CancelAsync();
            }

            ct.ThrowIfCancellationRequested();
            return new ConversionResult { Success = true };
        }

        var summary = await _service.RunAsync(
            FilePaths(totalFiles),
            Operation,
            maxParallelism: 1,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            cts.Token);

        Assert.Equal(totalFiles, summary.TotalFiles);

        // The point of the requirement: files queued behind the cancellation
        // never begin at all.
        Assert.True(
            startedCount < totalFiles,
            $"Cancellation should have prevented every file from starting, but {startedCount} of {totalFiles} began.");
        Assert.True(summary.ProcessedFiles <= startedCount);
    }

    [Fact]
    public async Task RunAsync_CancelledMidOperation_ReportsCancelledState()
    {
        using var cts = new CancellationTokenSource();
        var updates = new List<BatchProgressUpdate>();

        async Task<ConversionResult> Operation(string path, CancellationToken ct)
        {
            await Task.Delay(50, ct);
            return new ConversionResult { Success = true };
        }

        cts.CancelAfter(10);

        await _service.RunAsync(
            ["a.txt"],
            Operation,
            maxParallelism: 1,
            new SynchronousProgress<BatchProgressUpdate>(u => updates.Add(u)),
            cts.Token);

        Assert.Contains(updates, u => u.State == BatchItemState.Cancelled);
    }

    [Fact]
    public async Task RunAsync_EmptyFileList_ReturnsZeroedSummary()
    {
        var summary = await _service.RunAsync(
            [],
            (_, _) => Task.FromResult(new ConversionResult { Success = true }),
            maxParallelism: 4,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            CancellationToken.None);

        Assert.Equal(0, summary.TotalFiles);
        Assert.Equal(0, summary.ProcessedFiles);
    }
}
