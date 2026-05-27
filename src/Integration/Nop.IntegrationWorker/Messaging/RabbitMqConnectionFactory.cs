using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Options;
using RabbitMQ.Client;

namespace Nop.IntegrationWorker.Messaging;

public class RabbitMqConnectionFactory : IAsyncDisposable
{
    private IConnection? _connection;
    private readonly SemaphoreSlim _lock = new(1, 1);
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqConnectionFactory> _logger;

    public RabbitMqConnectionFactory(IOptions<RabbitMqOptions> options, ILogger<RabbitMqConnectionFactory> logger)
    {
        _options = options.Value;
        _logger = logger;
    }

    public async Task<IConnection> GetConnectionAsync(CancellationToken ct = default)
    {
        if (_connection?.IsOpen == true)
            return _connection;

        await _lock.WaitAsync(ct);
        try
        {
            if (_connection?.IsOpen == true)
                return _connection;

            var factory = new ConnectionFactory
            {
                HostName = _options.Host,
                Port = _options.Port,
                UserName = _options.Username,
                Password = _options.Password
            };

            // Retry loop — RabbitMQ may not be ready immediately on startup.
            while (true)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    _connection = await factory.CreateConnectionAsync(cancellationToken: ct);
                    _logger.LogInformation("Connected to RabbitMQ at {Host}:{Port}", _options.Host, _options.Port);
                    return _connection;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning("RabbitMQ unavailable, retrying in 5 s: {Message}", ex.Message);
                    await Task.Delay(5_000, ct);
                }
            }
        }
        finally
        {
            _lock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_connection is not null)
        {
            await _connection.CloseAsync();
            _connection.Dispose();
        }
        _lock.Dispose();
    }
}
