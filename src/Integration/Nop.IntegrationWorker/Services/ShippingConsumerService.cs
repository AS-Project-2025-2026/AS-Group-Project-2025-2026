using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Clients;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Messaging;
using Nop.IntegrationWorker.Models;
using Nop.IntegrationWorker.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.IntegrationWorker.Services;

public class ShippingConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly ShippingClient _shipping;
    private readonly WorkerDataService _data;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly ILogger<ShippingConsumerService> _logger;

    public ShippingConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        ShippingClient shipping,
        WorkerDataService data,
        IOptions<RabbitMqOptions> rmqOpts,
        ILogger<ShippingConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _shipping = shipping;
        _data = data;
        _rmqOpts = rmqOpts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionFactory.GetConnectionAsync(stoppingToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: _rmqOpts.ShippingQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("ShippingConsumerService listening on '{Queue}'", _rmqOpts.ShippingQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetString(ea.Body.Span);
        _logger.LogDebug("Shipping message received: {Body}", body);

        ShippingPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<ShippingPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot deserialise shipping message — discarding");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        if (payload is null)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        // Idempotency guard
        var shippingKey = $"shipping:{payload.IdempotencyKey}";
        var existing = await _data.GetIdempotencyRecordAsync(shippingKey);
        if (existing is not null)
        {
            _logger.LogInformation("Shipping already processed (key={Key}) — acking", shippingKey);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        try
        {
            var trackingNumber = await _shipping.CreateLabelAsync(body, ct);

            await _data.InsertIdempotencyRecordAsync(shippingKey,
                JsonSerializer.Serialize(new
                {
                    status = "processed",
                    adapter = "shipping",
                    trackingNumber,
                    processedAtUtc = DateTime.UtcNow
                }));

            _logger.LogInformation("Shipping label created for Order={OrderId}, tracking={Tracking}",
                payload.OrderId, trackingNumber);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Shipping call failed for Order={OrderId} — nacking (requeue)", payload.OrderId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }
}
