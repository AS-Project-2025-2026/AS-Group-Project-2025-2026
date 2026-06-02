using System.Text.Json;
using Nop.Core;
using Nop.Core.Domain.Integration;
using Nop.Core.Domain.Orders;
using Nop.Data;
using Nop.Services.Orders;

namespace Nop.Services.Integration;

public partial class IntegrationRecordService : IIntegrationRecordService
{
    #region Fields

    protected readonly IRepository<OutboxRecord> _outboxRepository;
    protected readonly IRepository<DeadLetterRecord> _deadLetterRepository;
    protected readonly IRepository<IdempotencyRecord> _idempotencyRepository;
    protected readonly IRepository<CircuitBreakerStateRecord> _circuitBreakerRepository;
    protected readonly IRepository<InventoryProjectionRecord> _inventoryProjectionRepository;
    protected readonly IOrderService _orderService;

    #endregion

    #region Ctor

    public IntegrationRecordService(
        IRepository<OutboxRecord> outboxRepository,
        IRepository<DeadLetterRecord> deadLetterRepository,
        IRepository<IdempotencyRecord> idempotencyRepository,
        IRepository<CircuitBreakerStateRecord> circuitBreakerRepository,
        IRepository<InventoryProjectionRecord> inventoryProjectionRepository,
        IOrderService orderService)
    {
        _outboxRepository = outboxRepository;
        _deadLetterRepository = deadLetterRepository;
        _idempotencyRepository = idempotencyRepository;
        _circuitBreakerRepository = circuitBreakerRepository;
        _inventoryProjectionRepository = inventoryProjectionRepository;
        _orderService = orderService;
    }

    #endregion

    #region Methods

    public virtual async Task<OutboxRecord> CreateFulfillmentOutboxRecordAsync(Order order)
    {
        var idempotencyKey = Guid.NewGuid().ToString();
        var correlationId = $"order-{order.Id}";

        var items = await _orderService.GetOrderItemsAsync(order.Id);
        var payloadItems = items.Select(i => new { productId = i.ProductId, quantity = i.Quantity }).ToList();

        var payload = JsonSerializer.Serialize(new
        {
            orderId = order.Id,
            correlationId,
            idempotencyKey,
            items = payloadItems
        });

        var record = new OutboxRecord
        {
            OrderId = order.Id,
            MessageType = IntegrationDefaults.MessageType.FulfillmentRequested,
            Payload = payload,
            CorrelationId = correlationId,
            IdempotencyKey = idempotencyKey,
            Status = IntegrationDefaults.OutboxStatus.Pending,
            RetryCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow
        };

        await _outboxRepository.InsertAsync(record);
        return record;
    }

    public virtual async Task<IPagedList<OutboxRecord>> SearchOutboxRecordsAsync(string status = null, int pageIndex = 0, int pageSize = int.MaxValue)
    {
        var query = _outboxRepository.Table;

        if (!string.IsNullOrEmpty(status))
            query = query.Where(r => r.Status == status);

        query = query.OrderByDescending(r => r.CreatedAtUtc);

        return await query.ToPagedListAsync(pageIndex, pageSize);
    }

    public virtual async Task<IPagedList<DeadLetterRecord>> SearchDeadLetterRecordsAsync(string escalationState = null, int pageIndex = 0, int pageSize = int.MaxValue)
    {
        var query = _deadLetterRepository.Table;

        if (!string.IsNullOrEmpty(escalationState))
            query = query.Where(r => r.EscalationState == escalationState);

        query = query.OrderByDescending(r => r.CreatedAtUtc);

        return await query.ToPagedListAsync(pageIndex, pageSize);
    }

    public virtual async Task<IList<CircuitBreakerStateRecord>> GetCircuitBreakerStatesAsync()
    {
        return await _circuitBreakerRepository.Table
            .OrderBy(r => r.Adapter)
            .ToListAsync();
    }

    public virtual async Task RequeueOutboxRecordAsync(int outboxRecordId)
    {
        var record = await _outboxRepository.GetByIdAsync(outboxRecordId)
            ?? throw new ArgumentException($"OutboxRecord {outboxRecordId} not found");

        record.Status = IntegrationDefaults.OutboxStatus.Pending;
        record.RetryCount = 0;
        record.NextAttemptAtUtc = DateTime.UtcNow;
        record.LastError = null;
        record.PublishedAtUtc = null;

        await _outboxRepository.UpdateAsync(record);
    }

    public virtual async Task RequeueDeadLetterRecordAsync(int deadLetterRecordId)
    {
        var dl = await _deadLetterRepository.GetByIdAsync(deadLetterRecordId)
            ?? throw new ArgumentException($"DeadLetterRecord {deadLetterRecordId} not found");

        var newOutbox = new OutboxRecord
        {
            OrderId = 0, // correlation id carries the reference; orderId not always recoverable here
            MessageType = IntegrationDefaults.MessageType.FulfillmentRequested,
            Payload = dl.Payload,
            CorrelationId = dl.CorrelationId,
            IdempotencyKey = dl.IdempotencyKey,
            Status = IntegrationDefaults.OutboxStatus.Pending,
            RetryCount = 0,
            CreatedAtUtc = DateTime.UtcNow,
            NextAttemptAtUtc = DateTime.UtcNow
        };

        await _outboxRepository.InsertAsync(newOutbox);

        dl.EscalationState = IntegrationDefaults.DlEscalationState.Requeued;
        dl.ResolvedAtUtc = DateTime.UtcNow;
        await _deadLetterRepository.UpdateAsync(dl);
    }

    public virtual async Task<IdempotencyRecord> GetIdempotencyRecordByKeyAsync(string key)
    {
        return await _idempotencyRepository.Table
            .FirstOrDefaultAsync(r => r.Key == key);
    }

    public virtual async Task InsertIdempotencyRecordAsync(IdempotencyRecord record)
    {
        await _idempotencyRepository.InsertAsync(record);
    }

    public virtual async Task<IList<InventoryProjectionRecord>> GetStaleOrConflictedProjectionsAsync()
    {
        return await _inventoryProjectionRepository.Table
            .Where(r => r.IsStale || r.ConflictFlag || r.PendingReconciliation)
            .OrderByDescending(r => r.UpdatedAtUtc)
            .ToListAsync();
    }

    public virtual async Task<IntegrationMetricsSnapshot> GetIntegrationMetricsSnapshotAsync()
    {
        var now = DateTime.UtcNow;
        var last24Hours = now.AddHours(-24);

        var outboxRecords = await _outboxRepository.Table.ToListAsync();
        var deadLetterRecords = await _deadLetterRepository.Table.ToListAsync();
        var circuitBreakerRecords = await _circuitBreakerRepository.Table.ToListAsync();
        var inventoryProjectionRecords = await _inventoryProjectionRepository.Table.ToListAsync();

        var publishedLatencies = outboxRecords
            .Where(r => r.PublishedAtUtc.HasValue && r.CreatedAtUtc >= last24Hours)
            .Select(r => (decimal)(r.PublishedAtUtc.Value - r.CreatedAtUtc).TotalSeconds)
            .Where(seconds => seconds >= decimal.Zero)
            .OrderBy(seconds => seconds)
            .ToList();

        var outboxBacklog = outboxRecords.Count(r =>
            r.Status == IntegrationDefaults.OutboxStatus.Pending ||
            r.Status == IntegrationDefaults.OutboxStatus.Retrying);
        var outboxRetrying = outboxRecords.Count(r => r.Status == IntegrationDefaults.OutboxStatus.Retrying);
        var outboxFailed = outboxRecords.Count(r => r.Status == IntegrationDefaults.OutboxStatus.Failed);
        var newDeadLetters = deadLetterRecords.Count(r => r.EscalationState == IntegrationDefaults.DlEscalationState.New);
        var openCircuitBreakers = circuitBreakerRecords.Count(r => string.Equals(r.State, "Open", StringComparison.OrdinalIgnoreCase));
        var halfOpenCircuitBreakers = circuitBreakerRecords.Count(r => string.Equals(r.State, "HalfOpen", StringComparison.OrdinalIgnoreCase));
        var staleInventoryProjections = inventoryProjectionRecords.Count(r => r.IsStale);
        var conflictedInventoryProjections = inventoryProjectionRecords.Count(r => r.ConflictFlag);
        var pendingInventoryReconciliations = inventoryProjectionRecords.Count(r => r.PendingReconciliation);

        var availabilityStatus = outboxFailed > 0 ? "Degraded" : outboxBacklog > 0 ? "Watch" : "Healthy";
        var resilienceStatus = openCircuitBreakers > 0 ? "Degraded" : halfOpenCircuitBreakers > 0 || outboxRetrying > 0 ? "Watch" : "Healthy";
        var consistencyStatus = conflictedInventoryProjections > 0 ? "Degraded" : staleInventoryProjections > 0 || pendingInventoryReconciliations > 0 ? "Watch" : "Healthy";
        var operabilityStatus = newDeadLetters > 0 ? "Degraded" : "Healthy";

        return new IntegrationMetricsSnapshot
        {
            GeneratedAtUtc = now,
            OverallStatus = new[] { availabilityStatus, resilienceStatus, consistencyStatus, operabilityStatus }.Contains("Degraded")
                ? "Degraded"
                : new[] { availabilityStatus, resilienceStatus, consistencyStatus, operabilityStatus }.Contains("Watch") ? "Watch" : "Healthy",

            OutboxBacklog = outboxBacklog,
            OutboxRetrying = outboxRetrying,
            OutboxFailed = outboxFailed,
            PublishedLast24Hours = outboxRecords.Count(r =>
                r.Status == IntegrationDefaults.OutboxStatus.Published &&
                r.PublishedAtUtc.HasValue &&
                r.PublishedAtUtc.Value >= last24Hours),
            AveragePublishLatencySecondsLast24Hours = publishedLatencies.Count == 0 ? null : decimal.Round(publishedLatencies.Average(), 1),
            P95PublishLatencySecondsLast24Hours = CalculatePercentile(publishedLatencies, 0.95m),

            NewDeadLetters = newDeadLetters,
            RequeuedDeadLettersLast24Hours = deadLetterRecords.Count(r =>
                r.EscalationState == IntegrationDefaults.DlEscalationState.Requeued &&
                r.ResolvedAtUtc.HasValue &&
                r.ResolvedAtUtc.Value >= last24Hours),
            OldestNewDeadLetterAgeMinutes = deadLetterRecords
                .Where(r => r.EscalationState == IntegrationDefaults.DlEscalationState.New)
                .Select(r => (decimal?)(now - r.CreatedAtUtc).TotalMinutes)
                .OrderByDescending(minutes => minutes)
                .FirstOrDefault(),

            OpenCircuitBreakers = openCircuitBreakers,
            HalfOpenCircuitBreakers = halfOpenCircuitBreakers,
            CircuitBreakerFailures = circuitBreakerRecords.Sum(r => r.FailureCount),

            StaleInventoryProjections = staleInventoryProjections,
            ConflictedInventoryProjections = conflictedInventoryProjections,
            PendingInventoryReconciliations = pendingInventoryReconciliations,

            AvailabilityStatus = availabilityStatus,
            ResilienceStatus = resilienceStatus,
            ConsistencyStatus = consistencyStatus,
            OperabilityStatus = operabilityStatus
        };
    }

    protected virtual decimal? CalculatePercentile(IList<decimal> sortedValues, decimal percentile)
    {
        if (!sortedValues.Any())
            return null;

        var index = (int)Math.Ceiling(sortedValues.Count * percentile) - 1;
        index = Math.Clamp(index, 0, sortedValues.Count - 1);

        return decimal.Round(sortedValues[index], 1);
    }

    #endregion
}
