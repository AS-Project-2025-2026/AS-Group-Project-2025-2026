using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Clients;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Messaging;
using Nop.IntegrationWorker.Metrics;
using Nop.IntegrationWorker.Models;
using Nop.IntegrationWorker.Options;
using Nop.IntegrationWorker.Resilience;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.IntegrationWorker.Services;

public class FulfillmentConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly WarehouseClient _warehouse;
    private readonly WorkerDataService _data;
    private readonly RabbitMqPublisher _publisher;
    private readonly ResilienceExecutor _resilience;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly ILogger<FulfillmentConsumerService> _logger;

    private const string AdapterName = "warehouse";
    private const string ShippingRoutingKey = "shipping.requested";
    private const string StoreOpsRoutingKey = "storeops.requested";
    private const string CustomerSupportRoutingKey = "customersupport.requested";

    public FulfillmentConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        WarehouseClient warehouse,
        WorkerDataService data,
        RabbitMqPublisher publisher,
        ResilienceExecutor resilience,
        IOptions<RabbitMqOptions> rmqOpts,
        ILogger<FulfillmentConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _warehouse = warehouse;
        _data = data;
        _publisher = publisher;
        _resilience = resilience;
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
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "discarded");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        if (payload is null)
        {
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "discarded");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        var fulfillmentKey = $"fulfillment:{payload.IdempotencyKey}";
        var existing = await _data.GetIdempotencyRecordAsync(fulfillmentKey);
        if (existing is not null)
        {
            _logger.LogInformation("Fulfillment already processed (key={Key}) — acking", fulfillmentKey);
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "idempotent");
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        _logger.LogInformation(
            "Outbox picked up. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OrderId={OrderId}",
            AdapterName, payload.CorrelationId, payload.IdempotencyKey, payload.OrderId);

        var firstAttemptAt = DateTime.UtcNow;
        string? warehouseRef = null;

        var (success, lastError, attempts) = await _resilience.ExecuteAsync(
            adapter: AdapterName,
            correlationId: payload.CorrelationId,
            idempotencyKey: payload.IdempotencyKey,
            outboxRecordId: null,
            operation: async innerCt =>
            {
                warehouseRef = await _warehouse.RequestFulfillmentAsync(body, innerCt);
            },
            ct: ct);

        await _resilience.PersistCircuitStateAsync(_data, AdapterName);

        if (!success)
        {
            WorkerMetrics.ObserveDeadLetter(AdapterName);
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "dead_letter");
            _logger.LogWarning(
                "Dead-letter created. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OrderId={OrderId} FailureReason={FailureReason}",
                AdapterName, payload.CorrelationId, payload.IdempotencyKey, payload.OrderId, lastError);

            await _data.InsertDeadLetterAsync(
                originalOutboxRecordId: null,
                payload: body,
                idempotencyKey: payload.IdempotencyKey,
                correlationId: payload.CorrelationId,
                adapter: AdapterName,
                failureReason: lastError ?? "unknown",
                firstAttemptAtUtc: firstAttemptAt,
                lastAttemptAtUtc: DateTime.UtcNow);

            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        await _data.InsertIdempotencyRecordAsync(fulfillmentKey,
            JsonSerializer.Serialize(new { status = "processed", adapter = AdapterName, processedAtUtc = DateTime.UtcNow }));

        var shippingPayload = new ShippingPayload
        {
            OrderId = payload.OrderId,
            CorrelationId = payload.CorrelationId,
            IdempotencyKey = Guid.NewGuid().ToString(),
            WarehouseReference = warehouseRef ?? string.Empty
        };
        await _publisher.PublishAsync(ShippingRoutingKey, JsonSerializer.Serialize(shippingPayload), ct);

        var storeOpsPayload = new StoreOpsPayload
        {
            OrderId = payload.OrderId,
            CorrelationId = payload.CorrelationId,
            IdempotencyKey = Guid.NewGuid().ToString(),
            WarehouseReference = warehouseRef ?? string.Empty
        };
        await _publisher.PublishAsync(StoreOpsRoutingKey, JsonSerializer.Serialize(storeOpsPayload), ct);

        var customerSupportPayload = new CustomerSupportPayload
        {
            OrderId = payload.OrderId,
            CorrelationId = payload.CorrelationId,
            IdempotencyKey = Guid.NewGuid().ToString(),
            Reason = "fulfillment_confirmed"
        };
        await _publisher.PublishAsync(CustomerSupportRoutingKey, JsonSerializer.Serialize(customerSupportPayload), ct);

        _logger.LogInformation(
            "Message published. Adapter={Adapter} CorrelationId={CorrelationId} OrderId={OrderId} RetryAttempt={Attempts}",
            AdapterName, payload.CorrelationId, payload.OrderId, attempts);

        WorkerMetrics.ObserveWorkerMessage(AdapterName, "processed");
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
    }
}
