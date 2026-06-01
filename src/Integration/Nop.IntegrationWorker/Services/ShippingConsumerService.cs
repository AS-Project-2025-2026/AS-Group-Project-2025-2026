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

public class ShippingConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly ShippingClient _shipping;
    private readonly WorkerDataService _data;
    private readonly ResilienceExecutor _resilience;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly ILogger<ShippingConsumerService> _logger;

    private const string AdapterName = "shipping";

    public ShippingConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        ShippingClient shipping,
        WorkerDataService data,
        ResilienceExecutor resilience,
        IOptions<RabbitMqOptions> rmqOpts,
        ILogger<ShippingConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _shipping = shipping;
        _data = data;
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

        var shippingKey = $"shipping:{payload.IdempotencyKey}";
        var existing = await _data.GetIdempotencyRecordAsync(shippingKey);
        if (existing is not null)
        {
            _logger.LogInformation("Shipping already processed (key={Key}) — acking", shippingKey);
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "idempotent");
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        _logger.LogInformation(
            "Outbox picked up. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OrderId={OrderId}",
            AdapterName, payload.CorrelationId, payload.IdempotencyKey, payload.OrderId);

        var firstAttemptAt = DateTime.UtcNow;
        string? trackingNumber = null;

        var (success, lastError, attempts) = await _resilience.ExecuteAsync(
            adapter: AdapterName,
            correlationId: payload.CorrelationId,
            idempotencyKey: payload.IdempotencyKey,
            outboxRecordId: null,
            operation: async innerCt =>
            {
                trackingNumber = await _shipping.CreateLabelAsync(body, innerCt);
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

        await _data.InsertIdempotencyRecordAsync(shippingKey,
            JsonSerializer.Serialize(new
            {
                status = "processed",
                adapter = AdapterName,
                trackingNumber,
                processedAtUtc = DateTime.UtcNow
            }));

        _logger.LogInformation(
            "Message published. Adapter={Adapter} CorrelationId={CorrelationId} OrderId={OrderId} TrackingNumber={TrackingNumber} RetryAttempt={Attempts}",
            AdapterName, payload.CorrelationId, payload.OrderId, trackingNumber, attempts);

        WorkerMetrics.ObserveWorkerMessage(AdapterName, "processed");
        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
    }
}
