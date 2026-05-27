using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Messaging;
using Nop.IntegrationWorker.Options;
using Nop.IntegrationWorker.Resilience;

namespace Nop.IntegrationWorker.Services;

public class OutboxPollingService : BackgroundService
{
    private readonly WorkerDataService _data;
    private readonly RabbitMqPublisher _publisher;
    private readonly ResilienceExecutor _resilience;
    private readonly WorkerOptions _workerOpts;
    private readonly ILogger<OutboxPollingService> _logger;

    private const string AdapterName = "rabbitmq";
    private const string FulfillmentRoutingKey = "fulfillment.requested";
    private const string ShippingRoutingKey = "shipping.requested";

    public OutboxPollingService(
        WorkerDataService data,
        RabbitMqPublisher publisher,
        ResilienceExecutor resilience,
        IOptions<WorkerOptions> workerOpts,
        ILogger<OutboxPollingService> logger)
    {
        _data = data;
        _publisher = publisher;
        _resilience = resilience;
        _workerOpts = workerOpts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("OutboxPollingService started (interval={Interval}s, batch={Batch})",
            _workerOpts.PollingIntervalSeconds, _workerOpts.BatchSize);

        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await PollOnceAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Unexpected error in polling loop");
            }

            await Task.Delay(TimeSpan.FromSeconds(_workerOpts.PollingIntervalSeconds), stoppingToken);
        }

        _logger.LogInformation("OutboxPollingService stopped");
    }

    private async Task PollOnceAsync(CancellationToken ct)
    {
        var records = await _data.GetPendingOutboxRecordsAsync(_workerOpts.BatchSize);

        if (records.Count == 0)
            return;

        _logger.LogInformation("Found {Count} pending outbox record(s)", records.Count);

        foreach (var record in records)
        {
            ct.ThrowIfCancellationRequested();
            await ProcessRecordAsync(record, ct);
        }
    }

    private async Task ProcessRecordAsync(OutboxRecord record, CancellationToken ct)
    {
        var routingKey = record.MessageType switch
        {
            "FulfillmentRequested" => FulfillmentRoutingKey,
            "ShippingRequested" => ShippingRoutingKey,
            _ => null
        };

        if (routingKey is null)
        {
            _logger.LogWarning(
                "Unknown MessageType '{Type}' on OutboxRecord {Id} — skipping",
                record.MessageType, record.Id);
            return;
        }

        _logger.LogInformation(
            "Outbox picked up. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OutboxRecordId={OutboxRecordId} OrderId={OrderId}",
            AdapterName, record.CorrelationId, record.IdempotencyKey, record.Id, record.OrderId);

        var firstAttemptAt = DateTime.UtcNow;

        var (success, lastError, _) = await _resilience.ExecuteAsync(
            adapter: AdapterName,
            correlationId: record.CorrelationId,
            idempotencyKey: record.IdempotencyKey,
            outboxRecordId: record.Id,
            operation: async innerCt =>
            {
                await _publisher.PublishAsync(routingKey, record.Payload, innerCt);
            },
            ct: ct);

        await _resilience.PersistCircuitStateAsync(_data, AdapterName);

        if (success)
        {
            await _data.MarkOutboxPublishedAsync(record.Id);
            _logger.LogInformation(
                "Message published. Adapter={Adapter} CorrelationId={CorrelationId} OutboxRecordId={OutboxRecordId} OrderId={OrderId} RoutingKey={RoutingKey}",
                AdapterName, record.CorrelationId, record.Id, record.OrderId, routingKey);
        }
        else
        {
            await _data.MarkOutboxFailedAsync(record.Id, lastError ?? "exhausted retries");

            _logger.LogWarning(
                "Dead-letter created. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OutboxRecordId={OutboxRecordId} OrderId={OrderId} FailureReason={FailureReason}",
                AdapterName, record.CorrelationId, record.IdempotencyKey, record.Id, record.OrderId, lastError);

            await _data.InsertDeadLetterAsync(
                originalOutboxRecordId: record.Id,
                payload: record.Payload,
                idempotencyKey: record.IdempotencyKey,
                correlationId: record.CorrelationId,
                adapter: AdapterName,
                failureReason: lastError ?? "exhausted retries",
                firstAttemptAtUtc: firstAttemptAt,
                lastAttemptAtUtc: DateTime.UtcNow);
        }
    }
}
