using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Options;
using Prometheus;

namespace Nop.IntegrationWorker.Services;

public sealed class MetricsServerService : IHostedService, IDisposable
{
    private readonly MetricsOptions _options;
    private readonly ILogger<MetricsServerService> _logger;
    private KestrelMetricServer? _server;

    public MetricsServerService(
        IOptions<MetricsOptions> options,
        ILogger<MetricsServerService> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _server = new KestrelMetricServer(port: _options.Port);
        _server.Start();

        _logger.LogInformation("Prometheus metrics server listening on port {Port}", _options.Port);
        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _server?.Dispose();
        _server = null;
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _server?.Dispose();
    }
}
