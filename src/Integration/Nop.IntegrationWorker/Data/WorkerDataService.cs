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
