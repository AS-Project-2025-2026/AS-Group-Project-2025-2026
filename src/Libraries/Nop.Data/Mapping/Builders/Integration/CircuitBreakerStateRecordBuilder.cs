using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;
using Nop.Data.Extensions;

namespace Nop.Data.Mapping.Builders.Integration;

public partial class CircuitBreakerStateRecordBuilder : NopEntityBuilder<CircuitBreakerStateRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(CircuitBreakerStateRecord.Adapter)).AsString(100).NotNullable()
            .WithColumn(nameof(CircuitBreakerStateRecord.State)).AsString(20).NotNullable()
            .WithColumn(nameof(CircuitBreakerStateRecord.FailureCount)).AsInt32().NotNullable()
            .WithColumn(nameof(CircuitBreakerStateRecord.OpenedAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(CircuitBreakerStateRecord.NextProbeAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(CircuitBreakerStateRecord.LastError)).AsString(1000).Nullable()
            .WithColumn(nameof(CircuitBreakerStateRecord.UpdatedAtUtc)).AsNopDateTime2().NotNullable();
    }
}
