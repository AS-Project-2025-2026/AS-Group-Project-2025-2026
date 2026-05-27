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
using Nop.IntegrationWorker.Resilience;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.IntegrationWorker.Services;

public class CustomerSupportConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly CustomerSupportClient _customerSupport;
    private readonly WorkerDataService _data;
    private readonly ResilienceExecutor _resilience;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly ILogger<CustomerSupportConsumerService> _logger;

    private const string AdapterName = "customersupport";

    public CustomerSupportConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        CustomerSupportClient customerSupport,
        WorkerDataService data,
        ResilienceExecutor resilience,
        IOptions<RabbitMqOptions> rmqOpts,
        ILogger<CustomerSupportConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _customerSupport = customerSupport;
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
            queue: _rmqOpts.CustomerSupportQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("CustomerSupportConsumerService listening on '{Queue}'", _rmqOpts.CustomerSupportQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetString(ea.Body.Span);

        CustomerSupportPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<CustomerSupportPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot deserialise customer support message — discarding");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        if (payload is null)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        var idempotencyKey = $"customersupport:{payload.IdempotencyKey}";
        var existing = await _data.GetIdempotencyRecordAsync(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("CustomerSupport already processed (key={Key}) — acking", idempotencyKey);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        _logger.LogInformation(
            "Outbox picked up. Adapter={Adapter} CorrelationId={CorrelationId} IdempotencyKey={IdempotencyKey} OrderId={OrderId} Reason={Reason}",
            AdapterName, payload.CorrelationId, payload.IdempotencyKey, payload.OrderId, payload.Reason);

        var firstAttemptAt = DateTime.UtcNow;
        string? ticketId = null;

        var (success, lastError, attempts) = await _resilience.ExecuteAsync(
            adapter: AdapterName,
            correlationId: payload.CorrelationId,
            idempotencyKey: payload.IdempotencyKey,
            outboxRecordId: null,
            operation: async innerCt =>
            {
                ticketId = await _customerSupport.CreateTicketAsync(body, innerCt);
            },
            ct: ct);

        await _resilience.PersistCircuitStateAsync(_data, AdapterName);

        if (!success)
        {
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

        await _data.InsertIdempotencyRecordAsync(idempotencyKey,
            JsonSerializer.Serialize(new
            {
                status = "processed",
                adapter = AdapterName,
                ticketId,
                processedAtUtc = DateTime.UtcNow
            }));

        _logger.LogInformation(
            "Message published. Adapter={Adapter} CorrelationId={CorrelationId} OrderId={OrderId} TicketId={TicketId} RetryAttempt={Attempts}",
            AdapterName, payload.CorrelationId, payload.OrderId, ticketId, attempts);

        await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
    }
}
