using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Nop.Services.Integration;

namespace Nop.Web.Infrastructure.Metrics;

public class IntegrationMetricsHostedService : BackgroundService
{
    private static readonly TimeSpan RefreshInterval = TimeSpan.FromSeconds(10);

    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<IntegrationMetricsHostedService> _logger;

    public IntegrationMetricsHostedService(
        IServiceScopeFactory scopeFactory,
        ILogger<IntegrationMetricsHostedService> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await RefreshAsync(stoppingToken);

        using var timer = new PeriodicTimer(RefreshInterval);
        while (await timer.WaitForNextTickAsync(stoppingToken))
            await RefreshAsync(stoppingToken);
    }

    private async Task RefreshAsync(CancellationToken ct)
    {
        try
        {
            await using var scope = _scopeFactory.CreateAsyncScope();
            var integrationRecordService = scope.ServiceProvider.GetRequiredService<IIntegrationRecordService>();
            var snapshot = await integrationRecordService.GetIntegrationMetricsSnapshotAsync();
            IntegrationPrometheusMetrics.Observe(snapshot);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to refresh integration Prometheus metrics");
        }
    }
}
