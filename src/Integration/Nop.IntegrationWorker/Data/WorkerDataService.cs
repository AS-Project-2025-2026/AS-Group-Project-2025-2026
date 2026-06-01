using Dapper;
using Microsoft.Data.SqlClient;

namespace Nop.IntegrationWorker.Data;

public class WorkerDataService
{
    private readonly string _connectionString;

    public WorkerDataService(string connectionString)
    {
        _connectionString = connectionString;
    }

    private SqlConnection OpenConnection() => new(_connectionString);

    public async Task<List<OutboxRecord>> GetPendingOutboxRecordsAsync(int batchSize)
    {
        const string sql = """
            SELECT TOP (@batchSize)
                Id, OrderId, MessageType, Payload, CorrelationId, IdempotencyKey,
                Status, RetryCount, CreatedAtUtc, NextAttemptAtUtc, PublishedAtUtc, LastError
            FROM OutboxRecord
            WHERE Status = 'Pending'
              AND (NextAttemptAtUtc IS NULL OR NextAttemptAtUtc <= @now)
            ORDER BY NextAttemptAtUtc
            """;

        using var conn = OpenConnection();
        var results = await conn.QueryAsync<OutboxRecord>(sql, new { batchSize, now = DateTime.UtcNow });
        return results.ToList();
    }

    public async Task MarkOutboxPublishedAsync(int id)
    {
        const string sql = """
            UPDATE OutboxRecord
            SET Status = 'Published', PublishedAtUtc = @now
            WHERE Id = @id
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new { id, now = DateTime.UtcNow });
    }

    public async Task MarkOutboxRetryingAsync(int id, string error, DateTime nextAttempt)
    {
        const string sql = """
            UPDATE OutboxRecord
            SET Status = 'Retrying',
                RetryCount = RetryCount + 1,
                LastError = @error,
                NextAttemptAtUtc = @nextAttempt
            WHERE Id = @id
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new { id, error = error[..Math.Min(error.Length, 1000)], nextAttempt });
    }

    public async Task<IdempotencyRecord?> GetIdempotencyRecordAsync(string key)
    {
        const string sql = "SELECT Id, [Key], Outcome, CreatedAtUtc, ExpiresAtUtc FROM IdempotencyRecord WHERE [Key] = @key";

        using var conn = OpenConnection();
        return await conn.QueryFirstOrDefaultAsync<IdempotencyRecord>(sql, new { key });
    }

    public async Task InsertIdempotencyRecordAsync(string key, string outcome)
    {
        const string sql = """
            INSERT INTO IdempotencyRecord ([Key], Outcome, CreatedAtUtc, ExpiresAtUtc)
            VALUES (@key, @outcome, @now, @expires)
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new
        {
            key,
            outcome,
            now = DateTime.UtcNow,
            expires = (DateTime?)DateTime.UtcNow.AddHours(48)
        });
    }

    public async Task MarkOutboxFailedAsync(int id, string error)
    {
        const string sql = """
            UPDATE OutboxRecord
            SET Status = 'Failed',
                LastError = @error
            WHERE Id = @id
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new { id, error = error[..Math.Min(error.Length, 1000)] });
    }

    public async Task InsertDeadLetterAsync(
        int? originalOutboxRecordId,
        string payload,
        string idempotencyKey,
        string correlationId,
        string adapter,
        string failureReason,
        DateTime firstAttemptAtUtc,
        DateTime lastAttemptAtUtc)
    {
        const string sql = """
            INSERT INTO DeadLetterRecord
                (OriginalOutboxRecordId, Payload, IdempotencyKey, CorrelationId, Adapter,
                 FailureReason, FirstAttemptAtUtc, LastAttemptAtUtc, EscalationState, CreatedAtUtc)
            VALUES
                (@originalOutboxRecordId, @payload, @idempotencyKey, @correlationId, @adapter,
                 @failureReason, @firstAttemptAtUtc, @lastAttemptAtUtc, 'New', @now)
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new
        {
            originalOutboxRecordId,
            payload,
            idempotencyKey,
            correlationId,
            adapter,
            failureReason = failureReason[..Math.Min(failureReason.Length, 1000)],
            firstAttemptAtUtc,
            lastAttemptAtUtc,
            now = DateTime.UtcNow
        });
    }

    public async Task UpsertCircuitBreakerStateAsync(
        string adapter, string state, int failureCount,
        DateTime? openedAtUtc, DateTime? nextProbeAtUtc, string? lastError)
    {
        const string sql = """
            MERGE CircuitBreakerStateRecord AS target
            USING (SELECT @adapter AS Adapter) AS source ON target.Adapter = source.Adapter
            WHEN MATCHED THEN
                UPDATE SET State = @state, FailureCount = @failureCount,
                           OpenedAtUtc = @openedAtUtc, NextProbeAtUtc = @nextProbeAtUtc,
                           LastError = @lastError, UpdatedAtUtc = @now
            WHEN NOT MATCHED THEN
                INSERT (Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc)
                VALUES (@adapter, @state, @failureCount, @openedAtUtc, @nextProbeAtUtc, @lastError, @now);
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new
        {
            adapter,
            state,
            failureCount,
            openedAtUtc,
            nextProbeAtUtc,
            lastError,
            now = DateTime.UtcNow
        });
    }

    public async Task<List<CircuitBreakerStateDto>> GetCircuitBreakerStatesAsync()
    {
        const string sql = """
            SELECT Adapter, State, FailureCount, OpenedAtUtc, NextProbeAtUtc, LastError, UpdatedAtUtc
            FROM CircuitBreakerStateRecord
            ORDER BY Adapter
            """;

        using var conn = OpenConnection();
        var results = await conn.QueryAsync<CircuitBreakerStateDto>(sql);
        return results.ToList();
    }

    /// <summary>
    /// Updates nopCommerce Product.StockQuantity for a given product.
    /// Only affects products using direct stock management (ManageInventoryMethodId = 1).
    /// Returns the number of rows updated (0 if product not found or not managed).
    /// </summary>
    public async Task<int> UpdateProductStockAsync(int productId, int quantity)
    {
        const string sql = """
            UPDATE Product
            SET StockQuantity = @quantity
            WHERE Id = @productId
              AND ManageInventoryMethodId = 1
              AND UseMultipleWarehouses = 0
            """;

        using var conn = OpenConnection();
        return await conn.ExecuteAsync(sql, new { productId, quantity });
    }

    public async Task UpsertInventoryProjectionAsync(
        int productId, string sourceSystem, int quantity,
        bool isStale, bool conflictFlag, bool pendingReconciliation)
    {
        const string sql = """
            MERGE InventoryProjectionRecord AS target
            USING (SELECT @productId AS ProductId, @sourceSystem AS SourceSystem) AS source
                ON target.ProductId = source.ProductId AND target.SourceSystem = source.SourceSystem
            WHEN MATCHED THEN
                UPDATE SET ReportedQuantity = @quantity,
                           LastConfirmedUtc = @now,
                           IsStale = @isStale,
                           ConflictFlag = @conflictFlag,
                           PendingReconciliation = @pendingReconciliation,
                           UpdatedAtUtc = @now
            WHEN NOT MATCHED THEN
                INSERT (ProductId, SourceSystem, ReportedQuantity, LastConfirmedUtc,
                        IsStale, ConflictFlag, PendingReconciliation, UpdatedAtUtc)
                VALUES (@productId, @sourceSystem, @quantity, @now,
                        @isStale, @conflictFlag, @pendingReconciliation, @now);
            """;

        using var conn = OpenConnection();
        await conn.ExecuteAsync(sql, new
        {
            productId, sourceSystem, quantity,
            isStale, conflictFlag, pendingReconciliation,
            now = DateTime.UtcNow
        });
    }

    public async Task<List<InventoryProjectionDto>> GetProjectionsByProductAsync(int productId)
    {
        const string sql = """
            SELECT Id, ProductId, SourceSystem, ReportedQuantity, LastConfirmedUtc,
                   IsStale, ConflictFlag, PendingReconciliation, ResolvedAtUtc, UpdatedAtUtc
            FROM InventoryProjectionRecord
            WHERE ProductId = @productId
            """;

        using var conn = OpenConnection();
        var results = await conn.QueryAsync<InventoryProjectionDto>(sql, new { productId });
        return results.ToList();
    }

    public async Task<List<InventoryProjectionDto>> GetStaleOrConflictedProjectionsAsync()
    {
        const string sql = """
            SELECT Id, ProductId, SourceSystem, ReportedQuantity, LastConfirmedUtc,
                   IsStale, ConflictFlag, PendingReconciliation, ResolvedAtUtc, UpdatedAtUtc
            FROM InventoryProjectionRecord
            WHERE IsStale = 1 OR ConflictFlag = 1 OR PendingReconciliation = 1
            ORDER BY UpdatedAtUtc DESC
            """;

        using var conn = OpenConnection();
        var results = await conn.QueryAsync<InventoryProjectionDto>(sql);
        return results.ToList();
    }

    public async Task<int> MarkStaleProjectionsAsync(int stalenessThresholdSeconds)
    {
        const string sql = """
            UPDATE InventoryProjectionRecord
            SET IsStale = 1, PendingReconciliation = 1, UpdatedAtUtc = @now
            WHERE IsStale = 0
              AND DATEDIFF(SECOND, LastConfirmedUtc, @now) >= @threshold
            """;

        using var conn = OpenConnection();
        return await conn.ExecuteAsync(sql, new { now = DateTime.UtcNow, threshold = stalenessThresholdSeconds });
    }

    public async Task<int> ResolveConflictsAsync(int toleranceUnits, int autoResolveMins = 30)
    {
        const string sql = """
            UPDATE p1
            SET p1.ConflictFlag = 0, p1.PendingReconciliation = 0,
                p1.ResolvedAtUtc = @now, p1.UpdatedAtUtc = @now
            FROM InventoryProjectionRecord p1
            JOIN InventoryProjectionRecord p2
                ON p1.ProductId = p2.ProductId AND p1.SourceSystem != p2.SourceSystem
            WHERE p1.ConflictFlag = 1
              AND ABS(p1.ReportedQuantity - p2.ReportedQuantity) <= @tolerance
              AND DATEDIFF(MINUTE, p1.UpdatedAtUtc, @now) >= @autoResolveMins
            """;

        using var conn = OpenConnection();
        return await conn.ExecuteAsync(sql, new { now = DateTime.UtcNow, tolerance = toleranceUnits, autoResolveMins });
    }
}
