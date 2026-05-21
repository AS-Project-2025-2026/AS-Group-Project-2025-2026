using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;
using Nop.Data.Extensions;

namespace Nop.Data.Mapping.Builders.Integration;

public partial class OutboxRecordBuilder : NopEntityBuilder<OutboxRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(OutboxRecord.OrderId)).AsInt32().NotNullable()
            .WithColumn(nameof(OutboxRecord.MessageType)).AsString(100).NotNullable()
            .WithColumn(nameof(OutboxRecord.Payload)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(OutboxRecord.CorrelationId)).AsString(100).NotNullable()
            .WithColumn(nameof(OutboxRecord.IdempotencyKey)).AsString(100).NotNullable()
            .WithColumn(nameof(OutboxRecord.Status)).AsString(50).NotNullable()
            .WithColumn(nameof(OutboxRecord.RetryCount)).AsInt32().NotNullable()
            .WithColumn(nameof(OutboxRecord.CreatedAtUtc)).AsNopDateTime2().NotNullable()
            .WithColumn(nameof(OutboxRecord.NextAttemptAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(OutboxRecord.PublishedAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(OutboxRecord.LastError)).AsString(1000).Nullable();
    }
}
