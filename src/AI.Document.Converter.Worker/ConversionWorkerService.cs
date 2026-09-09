namespace AI.Document.Converter.Worker;

public sealed class ConversionWorkerOptions
{
    // How long a claim is valid before another worker may take the item. Long
    // enough that a slow extraction is not stolen, short enough that a crashed
    // worker's items become claimable again promptly.
    public TimeSpan LeaseDuration { get; set; } = TimeSpan.FromMinutes(5);

    // Only used when the queue is empty. A claim returns immediately when work
    // exists, so this is idle latency, not per-item latency.
    public TimeSpan IdlePollInterval { get; set; } = TimeSpan.FromSeconds(2);

    // Bounds concurrent Python subprocesses on one worker (NFR-011). Each
    // extraction is its own process, so unbounded concurrency would exhaust
    // memory rather than go faster.
    public int MaxConcurrency { get; set; } = 2;
}

// Polls for claimable items and processes them.
//
// Deliberately a database poll rather than a broker subscription: the job
// existing and the job being claimable are the same commit (see
// ConversionIntakeService), so there is no dual-write to get wrong. The cost is
// idle polling, which is cheap next to the correctness this buys.
public sealed class ConversionWorkerService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ConversionWorkerOptions _options;
    private readonly ILogger<ConversionWorkerService> _logger;

    // Identifies this worker in a lease. Includes the machine name so an
    // abandoned lease can be traced to the process that dropped it. Truncated
    // to fit LeaseOwner's 120-character column, using Math.Min because a short
    // machine name makes a fixed-length slice throw.
    private readonly string _leaseOwner = BuildLeaseOwner();

    private static string BuildLeaseOwner()
    {
        var owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        return owner[..Math.Min(owner.Length, 120)];
    }

    public ConversionWorkerService(
        IServiceScopeFactory scopeFactory,
        Microsoft.Extensions.Options.IOptions<ConversionWorkerOptions> options,
        ILogger<ConversionWorkerService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Conversion worker {LeaseOwner} started with concurrency {Concurrency}",
            _leaseOwner, _options.MaxConcurrency);

        using var concurrency = new SemaphoreSlim(_options.MaxConcurrency);
        var running = new List<Task>();

        while (!stoppingToken.IsCancellationRequested)
        {
            await concurrency.WaitAsync(stoppingToken);

            Guid? itemId;
            try
            {
                itemId = await ClaimAsync(stoppingToken);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // A database blip must not kill the worker. Back off and retry.
                _logger.LogError(ex, "Failed to claim work; backing off");
                concurrency.Release();
                await Task.Delay(_options.IdlePollInterval, stoppingToken);
                continue;
            }

            if (itemId is null)
            {
                concurrency.Release();
                await Task.Delay(_options.IdlePollInterval, stoppingToken);
                continue;
            }

            running.Add(ProcessAndReleaseAsync(itemId.Value, concurrency, stoppingToken));
            running.RemoveAll(t => t.IsCompleted);
        }

        // Let in-flight items finish rather than abandoning them mid-write; an
        // interrupted publish is exactly what SR-JOB-3 exists to prevent.
        await Task.WhenAll(running);
    }

    private async Task<Guid?> ClaimAsync(CancellationToken cancellationToken)
    {
        using var scope = _scopeFactory.CreateScope();
        var claimer = scope.ServiceProvider.GetRequiredService<JobClaimer>();
        return await claimer.TryClaimAsync(_leaseOwner, _options.LeaseDuration, cancellationToken);
    }

    private async Task ProcessAndReleaseAsync(
        Guid itemId, SemaphoreSlim concurrency, CancellationToken cancellationToken)
    {
        try
        {
            // A scope per item: the DbContext is scoped, and sharing one across
            // concurrent items would both break its threading contract and let
            // one item's tracked entities leak into another's save.
            using var scope = _scopeFactory.CreateScope();
            var processor = scope.ServiceProvider.GetRequiredService<JobProcessor>();
            await processor.ProcessAsync(itemId, _leaseOwner, _options.LeaseDuration, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // The lease will expire and the item becomes claimable again, so
            // work is not lost even when this path is reached.
            _logger.LogError(ex, "Processing item {ItemId} threw; its lease will expire", itemId);
        }
        finally
        {
            concurrency.Release();
        }
    }
}
