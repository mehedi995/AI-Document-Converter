using AI.Document.Converter.Application.Interfaces;
using AI.Document.Converter.Application.Models;
using AI.Document.Converter.Domain.Entities;
using AI.Document.Converter.Domain.Enums;

namespace AI.Document.Converter.Application.Services;

public sealed class BatchService : IBatchService
{
    public async Task<BatchSummary> RunAsync(
        IReadOnlyList<string> filePaths,
        Func<string, CancellationToken, Task<ConversionResult>> operation,
        int maxParallelism,
        IProgress<BatchProgressUpdate> progress,
        CancellationToken cancellationToken)
    {
        var total = filePaths.Count;
        var processed = 0;
        var successCount = 0;
        var failureCount = 0;
        var counterLock = new object();

        BatchProgressUpdate Snapshot(string filePath, BatchItemState state, ConversionResult? result)
        {
            lock (counterLock)
            {
                return new BatchProgressUpdate
                {
                    FilePath = filePath,
                    State = state,
                    Result = result,
                    TotalCount = total,
                    ProcessedCount = processed,
                    SuccessCount = successCount,
                    FailureCount = failureCount
                };
            }
        }

        using var semaphore = new SemaphoreSlim(Math.Max(1, maxParallelism));

        var tasks = filePaths.Select(async filePath =>
        {
            // NFR-011: bounds concurrent Python subprocesses/extraction work.
            // Throws OperationCanceledException here (queued but not yet
            // started) if cancellation happens before this file's turn -
            // FR-037's "no new files begin processing" is satisfied simply by
            // never proceeding past this line.
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                progress.Report(Snapshot(filePath, BatchItemState.Started, null));

                ConversionResult result;
                try
                {
                    result = await operation(filePath, cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    progress.Report(Snapshot(filePath, BatchItemState.Cancelled, null));
                    throw;
                }

                BatchProgressUpdate update;
                lock (counterLock)
                {
                    processed++;
                    if (result.Success)
                    {
                        successCount++;
                    }
                    else
                    {
                        failureCount++;
                    }

                    update = new BatchProgressUpdate
                    {
                        FilePath = filePath,
                        State = BatchItemState.Completed,
                        Result = result,
                        TotalCount = total,
                        ProcessedCount = processed,
                        SuccessCount = successCount,
                        FailureCount = failureCount
                    };
                }

                progress.Report(update);
            }
            finally
            {
                semaphore.Release();
            }
        }).ToList();

        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // BR-006/AC-025: cancellation stops the batch, not the caller -
            // fall through and return whatever was actually completed.
        }

        lock (counterLock)
        {
            return new BatchSummary
            {
                TotalFiles = total,
                ProcessedFiles = processed,
                SuccessCount = successCount,
                FailureCount = failureCount
            };
        }
    }
}
