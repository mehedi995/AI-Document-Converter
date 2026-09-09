using AI.Document.Converter.Persistence.Retention;
using Microsoft.Extensions.Options;

namespace AI.Document.Converter.Worker;

// Runs orphan reconciliation on a slow timer.
//
// Far less often than the retention sweep: an orphan is wasted storage, not a
// broken promise to a customer, and this is the one process that can delete
// something no row knows about. Rare and cautious beats frequent and eager.
public sealed class OrphanReconciliationHostedService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly OrphanReconciliationOptions _options;
    private readonly ILogger<OrphanReconciliationHostedService> _logger;

    public OrphanReconciliationHostedService(
        IServiceScopeFactory scopeFactory,
        IOptions<OrphanReconciliationOptions> options,
        ILogger<OrphanReconciliationHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _options = options.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation(
            "Orphan reconciliation every {Interval}, minimum object age {MinimumAge}, reportOnly={ReportOnly}",
            _options.Interval, _options.MinimumAge, _options.ReportOnly);

        // Deliberately does NOT run immediately at startup. A worker restarting
        // during a busy period would otherwise scan while uploads are in
        // flight, and although the minimum age protects them, there is no
        // reason to add load at the worst moment.
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_options.Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                using var scope = _scopeFactory.CreateScope();
                var service = scope.ServiceProvider.GetRequiredService<OrphanReconciliationService>();
                await service.ReconcileAsync(DateTime.UtcNow, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // Never kills the worker. A missed reconciliation costs storage,
                // not correctness.
                _logger.LogError(ex, "Orphan reconciliation failed; will retry on the next interval");
            }
        }
    }
}
