using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Messaging;
using Nop.IntegrationWorker.Options;

namespace Nop.IntegrationWorker.Services;

public class OutboxPollingService : BackgroundService
{
    private readonly WorkerDataService _data;
    private readonly RabbitMqPublisher _publisher;
    private readonly WorkerOptions _workerOpts;
    private readonly ILogger<OutboxPollingService> _logger;

    private const string FulfillmentRoutingKey = "fulfillment.requested";
    private const string ShippingRoutingKey = "shipping.requested";

    public OutboxPollingService(
        WorkerDataService data,
        RabbitMqPublisher publisher,
        IOptions<WorkerOptions> workerOpts,
        ILogger<OutboxPollingService> logger)
    {
        _data = data;
        _publisher = publisher;
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
            _logger.LogWarning("Unknown MessageType '{Type}' on OutboxRecord {Id} — skipping", record.MessageType, record.Id);
            return;
        }

        try
        {
            await _publisher.PublishAsync(routingKey, record.Payload, ct);
            await _data.MarkOutboxPublishedAsync(record.Id);

            _logger.LogInformation("OutboxRecord {Id} (Order={OrderId}) published to '{RoutingKey}'",
                record.Id, record.OrderId, routingKey);
        }
        catch (Exception ex)
        {
            // Linear backoff: 30s × (retryCount + 1). Polly/Pessoa 4 adds exponential later.
            var nextAttempt = DateTime.UtcNow.AddSeconds(30 * (record.RetryCount + 1));
            await _data.MarkOutboxRetryingAsync(record.Id, ex.Message, nextAttempt);

            _logger.LogWarning(ex, "Failed to publish OutboxRecord {Id} — marked Retrying, next attempt at {Next}",
                record.Id, nextAttempt);
        }
    }
}
