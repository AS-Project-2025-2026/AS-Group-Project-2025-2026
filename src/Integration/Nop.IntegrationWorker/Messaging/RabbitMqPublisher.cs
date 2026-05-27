using System.Text;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Options;
using RabbitMQ.Client;

namespace Nop.IntegrationWorker.Messaging;

public class RabbitMqPublisher
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly RabbitMqOptions _options;
    private readonly ILogger<RabbitMqPublisher> _logger;

    private IChannel? _channel;
    private readonly SemaphoreSlim _lock = new(1, 1);

    public RabbitMqPublisher(
        RabbitMqConnectionFactory connectionFactory,
        IOptions<RabbitMqOptions> options,
        ILogger<RabbitMqPublisher> logger)
    {
        _connectionFactory = connectionFactory;
        _options = options.Value;
        _logger = logger;
    }

    public async Task PublishAsync(string routingKey, string messageJson, CancellationToken ct = default)
    {
        var channel = await GetChannelAsync(ct);
        var body = Encoding.UTF8.GetBytes(messageJson);
        var props = new BasicProperties { Persistent = true };

        await channel.BasicPublishAsync(
            exchange: _options.Exchange,
            routingKey: routingKey,
            mandatory: false,
            basicProperties: props,
            body: body,
            cancellationToken: ct);

        _logger.LogDebug("Published to {Exchange}/{RoutingKey}: {Body}", _options.Exchange, routingKey, messageJson);
    }

    private async Task<IChannel> GetChannelAsync(CancellationToken ct)
    {
        if (_channel?.IsOpen == true)
            return _channel;

        await _lock.WaitAsync(ct);
        try
        {
            if (_channel?.IsOpen == true)
                return _channel;

            var connection = await _connectionFactory.GetConnectionAsync(ct);
            _channel = await connection.CreateChannelAsync(cancellationToken: ct);
            return _channel;
        }
        finally
        {
            _lock.Release();
        }
    }
}
