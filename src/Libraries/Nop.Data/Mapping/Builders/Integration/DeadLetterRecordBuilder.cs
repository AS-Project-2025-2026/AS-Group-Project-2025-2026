using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;
using Nop.Data.Extensions;

namespace Nop.Data.Mapping.Builders.Integration;

public partial class DeadLetterRecordBuilder : NopEntityBuilder<DeadLetterRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(DeadLetterRecord.OriginalOutboxRecordId)).AsInt32().Nullable()
            .WithColumn(nameof(DeadLetterRecord.Payload)).AsString(int.MaxValue).NotNullable()
            .WithColumn(nameof(DeadLetterRecord.IdempotencyKey)).AsString(100).NotNullable()
            .WithColumn(nameof(DeadLetterRecord.CorrelationId)).AsString(100).NotNullable()
            .WithColumn(nameof(DeadLetterRecord.Adapter)).AsString(100).NotNullable()
            .WithColumn(nameof(DeadLetterRecord.FailureReason)).AsString(1000).Nullable()
            .WithColumn(nameof(DeadLetterRecord.FirstAttemptAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(DeadLetterRecord.LastAttemptAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(DeadLetterRecord.EscalationState)).AsString(50).NotNullable()
            .WithColumn(nameof(DeadLetterRecord.CreatedAtUtc)).AsNopDateTime2().NotNullable()
            .WithColumn(nameof(DeadLetterRecord.ResolvedAtUtc)).AsNopDateTime2().Nullable();
    }
}
