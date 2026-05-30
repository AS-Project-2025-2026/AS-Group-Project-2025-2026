using Nop.Core;
using Nop.Core.Domain.Integration;

namespace Nop.Services.Integration;

public partial interface IIntegrationRecordService
{
    Task<OutboxRecord> CreateFulfillmentOutboxRecordAsync(Nop.Core.Domain.Orders.Order order);

    Task<IPagedList<OutboxRecord>> SearchOutboxRecordsAsync(string status = null, int pageIndex = 0, int pageSize = int.MaxValue);

    Task<IPagedList<DeadLetterRecord>> SearchDeadLetterRecordsAsync(string escalationState = null, int pageIndex = 0, int pageSize = int.MaxValue);

    Task<IList<CircuitBreakerStateRecord>> GetCircuitBreakerStatesAsync();

    Task RequeueOutboxRecordAsync(int outboxRecordId);

    Task RequeueDeadLetterRecordAsync(int deadLetterRecordId);

    Task<IdempotencyRecord> GetIdempotencyRecordByKeyAsync(string key);

    Task InsertIdempotencyRecordAsync(IdempotencyRecord record);

    Task<IList<InventoryProjectionRecord>> GetStaleOrConflictedProjectionsAsync();
}
