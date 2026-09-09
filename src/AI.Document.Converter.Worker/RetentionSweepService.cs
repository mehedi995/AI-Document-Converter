using AI.Document.Converter.Persistence.Retention;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Worker;

// Runs the retention sweep on a timer (SR-SEC-6).
//
// Hosted in the worker rather than the web host: it is background work, and a
// web host may be scaled to several instances that would all sweep at once.
// The sweep is idempotent, so overlap is safe rather than harmful, but there is
// no reason to invite it.
public sealed class RetentionSweepService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly RetentionPolicy _policy;
    private readonly ILogger<RetentionSweepService> _logger;

    public RetentionSweepService(
        IServiceScopeFactory scopeFactory,
        IOptions<RetentionPolicy> policy,
        ILogger<RetentionSweepService> logger)
    {
        _scopeFactory = scopeFactory;
        _policy = policy.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Retention sweep every {Interval}. {Policy}",
            _policy.SweepInterval, _policy.DescribeForCustomer());

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var retention = scope.ServiceProvider.GetRequiredService<RetentionService>();
                await retention.SweepAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // A failed sweep must not kill the worker, but it does mean
                // content is being kept longer than promised - so it is logged
                // at Error, not swallowed quietly.
                _logger.LogError(
                    ex, "Retention sweep failed. Content may be retained beyond the disclosed period.");
            }

            try
            {
                await Task.Delay(_policy.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }
}
