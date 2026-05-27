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
}
