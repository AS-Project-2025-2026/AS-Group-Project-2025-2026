using FluentMigrator.Builders.Create.Table;
using Nop.Core.Domain.Integration;
using Nop.Data.Extensions;

namespace Nop.Data.Mapping.Builders.Integration;

public partial class InventoryProjectionRecordBuilder : NopEntityBuilder<InventoryProjectionRecord>
{
    public override void MapEntity(CreateTableExpressionBuilder table)
    {
        table
            .WithColumn(nameof(InventoryProjectionRecord.ProductId)).AsInt32().NotNullable()
            .WithColumn(nameof(InventoryProjectionRecord.SourceSystem)).AsString(50).NotNullable()
            .WithColumn(nameof(InventoryProjectionRecord.ReportedQuantity)).AsInt32().NotNullable()
            .WithColumn(nameof(InventoryProjectionRecord.LastConfirmedUtc)).AsNopDateTime2().NotNullable()
            .WithColumn(nameof(InventoryProjectionRecord.IsStale)).AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn(nameof(InventoryProjectionRecord.ConflictFlag)).AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn(nameof(InventoryProjectionRecord.PendingReconciliation)).AsBoolean().NotNullable().WithDefaultValue(false)
            .WithColumn(nameof(InventoryProjectionRecord.ResolvedAtUtc)).AsNopDateTime2().Nullable()
            .WithColumn(nameof(InventoryProjectionRecord.UpdatedAtUtc)).AsNopDateTime2().NotNullable();
    }
}
