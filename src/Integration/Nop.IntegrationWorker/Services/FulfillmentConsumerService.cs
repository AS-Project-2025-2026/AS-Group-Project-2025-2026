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

public class FulfillmentConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly WarehouseClient _warehouse;
    private readonly WorkerDataService _data;
    private readonly RabbitMqPublisher _publisher;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly ILogger<FulfillmentConsumerService> _logger;

    private const string ShippingRoutingKey = "shipping.requested";
    private const string StoreOpsRoutingKey = "storeops.requested";

    public FulfillmentConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        WarehouseClient warehouse,
        WorkerDataService data,
        RabbitMqPublisher publisher,
        IOptions<RabbitMqOptions> rmqOpts,
        ILogger<FulfillmentConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _warehouse = warehouse;
        _data = data;
        _publisher = publisher;
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
            queue: _rmqOpts.FulfillmentQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("FulfillmentConsumerService listening on '{Queue}'", _rmqOpts.FulfillmentQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetString(ea.Body.Span);
        _logger.LogDebug("Fulfillment message received: {Body}", body);

        FulfillmentPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<FulfillmentPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot deserialise fulfillment message — discarding");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        if (payload is null)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        // Idempotency guard
        var fulfillmentKey = $"fulfillment:{payload.IdempotencyKey}";
        var existing = await _data.GetIdempotencyRecordAsync(fulfillmentKey);
        if (existing is not null)
        {
            _logger.LogInformation("Fulfillment already processed (key={Key}) — acking", fulfillmentKey);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        try
        {
            var warehouseRef = await _warehouse.RequestFulfillmentAsync(body, ct);

            // Record success before publishing shipping request (idempotent boundary)
            await _data.InsertIdempotencyRecordAsync(fulfillmentKey,
                JsonSerializer.Serialize(new { status = "processed", adapter = "warehouse", processedAtUtc = DateTime.UtcNow }));

            // Publish shipping request
            var shippingPayload = new ShippingPayload
            {
                OrderId = payload.OrderId,
                CorrelationId = payload.CorrelationId,
                IdempotencyKey = Guid.NewGuid().ToString(),
                WarehouseReference = warehouseRef
            };
            await _publisher.PublishAsync(ShippingRoutingKey, JsonSerializer.Serialize(shippingPayload), ct);

            // Notify store POS to prepare for cross-channel pickup
            var storeOpsPayload = new StoreOpsPayload
            {
                OrderId = payload.OrderId,
                CorrelationId = payload.CorrelationId,
                IdempotencyKey = Guid.NewGuid().ToString(),
                WarehouseReference = warehouseRef
            };
            await _publisher.PublishAsync(StoreOpsRoutingKey, JsonSerializer.Serialize(storeOpsPayload), ct);

            _logger.LogInformation("Fulfillment succeeded for Order={OrderId}, shipping + storeops requests published", payload.OrderId);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Warehouse call failed for Order={OrderId} — nacking (requeue)", payload.OrderId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }
}
