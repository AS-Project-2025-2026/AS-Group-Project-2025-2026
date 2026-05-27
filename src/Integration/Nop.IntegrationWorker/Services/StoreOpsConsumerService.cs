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

public class StoreOpsConsumerService : BackgroundService
{
    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly StorePosClient _storePos;
    private readonly WorkerDataService _data;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly ILogger<StoreOpsConsumerService> _logger;

    public StoreOpsConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        StorePosClient storePos,
        WorkerDataService data,
        IOptions<RabbitMqOptions> rmqOpts,
        ILogger<StoreOpsConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _storePos = storePos;
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
            queue: _rmqOpts.StoreOpsQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("StoreOpsConsumerService listening on '{Queue}'", _rmqOpts.StoreOpsQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetString(ea.Body.Span);
        _logger.LogDebug("StoreOps message received: {Body}", body);

        StoreOpsPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<StoreOpsPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot deserialise storeops message — discarding");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        if (payload is null)
        {
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        // Idempotency guard
        var storeOpsKey = $"storeops:{payload.IdempotencyKey}";
        var existing = await _data.GetIdempotencyRecordAsync(storeOpsKey);
        if (existing is not null)
        {
            _logger.LogInformation("StoreOps already processed (key={Key}) — acking", storeOpsKey);
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        try
        {
            var pickupRef = await _storePos.ConfirmPickupAsync(body, ct);

            await _data.InsertIdempotencyRecordAsync(storeOpsKey,
                JsonSerializer.Serialize(new
                {
                    status = "processed",
                    adapter = "storepos",
                    pickupReference = pickupRef,
                    processedAtUtc = DateTime.UtcNow
                }));

            _logger.LogInformation("Store pickup confirmed for Order={OrderId}, pickupRef={Ref}",
                payload.OrderId, pickupRef);

            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "StorePOS call failed for Order={OrderId} — nacking (requeue)", payload.OrderId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }
}
