using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Nop.IntegrationWorker.Data;
using Nop.IntegrationWorker.Messaging;
using Nop.IntegrationWorker.Metrics;
using Nop.IntegrationWorker.Models;
using Nop.IntegrationWorker.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Nop.IntegrationWorker.Services;

public class PosStockConsumerService : BackgroundService
{
    private const string AdapterName = "storepos";

    private readonly RabbitMqConnectionFactory _connectionFactory;
    private readonly WorkerDataService _data;
    private readonly RabbitMqOptions _rmqOpts;
    private readonly InventoryOptions _inventoryOpts;
    private readonly ILogger<PosStockConsumerService> _logger;

    public PosStockConsumerService(
        RabbitMqConnectionFactory connectionFactory,
        WorkerDataService data,
        IOptions<RabbitMqOptions> rmqOpts,
        IOptions<InventoryOptions> inventoryOpts,
        ILogger<PosStockConsumerService> logger)
    {
        _connectionFactory = connectionFactory;
        _data = data;
        _rmqOpts = rmqOpts.Value;
        _inventoryOpts = inventoryOpts.Value;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var connection = await _connectionFactory.GetConnectionAsync(stoppingToken);
        using var channel = await connection.CreateChannelAsync(cancellationToken: stoppingToken);
        await EnsureTopologyAsync(channel, stoppingToken);
        await channel.BasicQosAsync(prefetchSize: 0, prefetchCount: 1, global: false, cancellationToken: stoppingToken);

        var consumer = new AsyncEventingBasicConsumer(channel);
        consumer.ReceivedAsync += (_, ea) => HandleAsync(channel, ea, stoppingToken);

        await channel.BasicConsumeAsync(
            queue: _rmqOpts.PosStockQueue,
            autoAck: false,
            consumer: consumer,
            cancellationToken: stoppingToken);

        _logger.LogInformation("PosStockConsumerService listening on '{Queue}'", _rmqOpts.PosStockQueue);

        await Task.Delay(Timeout.Infinite, stoppingToken);
    }

    private async Task EnsureTopologyAsync(IChannel channel, CancellationToken ct)
    {
        await channel.ExchangeDeclareAsync(
            _rmqOpts.Exchange,
            ExchangeType.Direct,
            durable: true,
            autoDelete: false,
            arguments: null,
            passive: false,
            noWait: false,
            cancellationToken: ct);

        await channel.QueueDeclareAsync(
            _rmqOpts.PosStockQueue,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: new Dictionary<string, object?>
            {
                ["x-dead-letter-exchange"] = $"{_rmqOpts.Exchange}.dlx",
                ["x-dead-letter-routing-key"] = "pos.stock.dead"
            },
            passive: false,
            noWait: false,
            cancellationToken: ct);

        await channel.QueueBindAsync(
            _rmqOpts.PosStockQueue,
            _rmqOpts.Exchange,
            routingKey: "pos.stock.reported",
            arguments: null,
            noWait: false,
            cancellationToken: ct);
    }

    private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs ea, CancellationToken ct)
    {
        var body = Encoding.UTF8.GetString(ea.Body.Span);
        _logger.LogDebug("POS stock message received: {Body}", body);

        PosStockPayload? payload;
        try
        {
            payload = JsonSerializer.Deserialize<PosStockPayload>(body,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Cannot deserialise POS stock message — discarding");
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "discarded");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        if (payload is null || payload.ProductId <= 0 || payload.Quantity < 0)
        {
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "discarded");
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: false, cancellationToken: ct);
            return;
        }

        var idempotencyKey = string.IsNullOrWhiteSpace(payload.IdempotencyKey)
            ? $"pos-stock:{payload.ProductId}:{payload.ReportedAtUtc:O}:{payload.Quantity}"
            : $"pos-stock:{payload.IdempotencyKey}";

        var existing = await _data.GetIdempotencyRecordAsync(idempotencyKey);
        if (existing is not null)
        {
            _logger.LogInformation("POS stock update already processed (key={Key}) — acking", idempotencyKey);
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "idempotent");
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
            return;
        }

        try
        {
            await ProcessAsync(payload, ct);

            await _data.InsertIdempotencyRecordAsync(idempotencyKey,
                JsonSerializer.Serialize(new
                {
                    status = "processed",
                    adapter = AdapterName,
                    productId = payload.ProductId,
                    quantity = payload.Quantity,
                    processedAtUtc = DateTime.UtcNow
                }));

            WorkerMetrics.ObserveWorkerMessage(AdapterName, "processed");
            await channel.BasicAckAsync(ea.DeliveryTag, multiple: false, cancellationToken: ct);
        }
        catch (Exception ex)
        {
            WorkerMetrics.ObserveWorkerMessage(AdapterName, "requeued");
            _logger.LogWarning(ex, "POS stock update failed for ProductId={ProductId} — nacking (requeue)", payload.ProductId);
            await channel.BasicNackAsync(ea.DeliveryTag, multiple: false, requeue: true, cancellationToken: ct);
        }
    }

    private async Task ProcessAsync(PosStockPayload payload, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();

        await _data.UpsertInventoryProjectionAsync(
            payload.ProductId, "pos", payload.Quantity,
            isStale: false, conflictFlag: false, pendingReconciliation: false);

        var projections = await _data.GetProjectionsByProductAsync(payload.ProductId);
        var wms = projections.FirstOrDefault(p => p.SourceSystem == _inventoryOpts.SourceSystem);

        if (wms is null)
        {
            _logger.LogInformation(
                "POS stock recorded for ProductId={ProductId}, but no WMS projection exists yet",
                payload.ProductId);
            return;
        }

        var diff = Math.Abs(wms.ReportedQuantity - payload.Quantity);
        var conflict = diff > _inventoryOpts.ConflictToleranceUnits;
        var checkoutQty = conflict
            ? Math.Min(wms.ReportedQuantity, payload.Quantity)
            : wms.ReportedQuantity;

        await _data.SetInventoryProjectionConflictAsync(
            payload.ProductId,
            conflictFlag: conflict,
            pendingReconciliation: conflict);

        await _data.UpdateProductStockAsync(payload.ProductId, checkoutQty);

        if (conflict)
        {
            _logger.LogWarning(
                "POS/WMS inventory conflict on ProductId={ProductId}: wms={WmsQty} vs pos={PosQty} (diff={Diff}, tolerance={Tolerance}) — checkout capped at {CheckoutQty}",
                payload.ProductId, wms.ReportedQuantity, payload.Quantity,
                diff, _inventoryOpts.ConflictToleranceUnits, checkoutQty);
        }
        else
        {
            _logger.LogInformation(
                "POS stock accepted for ProductId={ProductId}: pos={PosQty}, wms={WmsQty}, checkout={CheckoutQty}",
                payload.ProductId, payload.Quantity, wms.ReportedQuantity, checkoutQty);
        }
    }
}
