using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;
using Nop.Data.Extensions;

namespace Nop.Data.Mapping.Builders.Integration;

public partial class IdempotencyRecordBuilder : NopEntityBuilder<IdempotencyRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(IdempotencyRecord.Key)).AsString(100).NotNullable()
            .WithColumn(nameof(IdempotencyRecord.Outcome)).AsString(500).NotNullable()
            .WithColumn(nameof(IdempotencyRecord.CreatedAtUtc)).AsNopDateTime2().NotNullable()
            .WithColumn(nameof(IdempotencyRecord.ExpiresAtUtc)).AsNopDateTime2().Nullable();
    }
}
