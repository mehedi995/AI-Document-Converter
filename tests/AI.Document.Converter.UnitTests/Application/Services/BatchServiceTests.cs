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

    [Fact]
    public async Task RunAsync_Cancelled_StopsStartingNewFilesAndReturnsPartialSummary()
    {
        var startedCount = 0;
        using var cts = new CancellationTokenSource();

        async Task<ConversionResult> Operation(string path, CancellationToken ct)
        {
            Interlocked.Increment(ref startedCount);
            await Task.Delay(100, ct);
            return new ConversionResult { Success = true };
        }

        cts.CancelAfter(120);

        var summary = await _service.RunAsync(
            FilePaths(20),
            Operation,
            maxParallelism: 2,
            new SynchronousProgress<BatchProgressUpdate>(_ => { }),
            cts.Token);

        Assert.Equal(20, summary.TotalFiles);
        Assert.True(startedCount < 20, "Cancellation should have prevented every file from starting.");
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
