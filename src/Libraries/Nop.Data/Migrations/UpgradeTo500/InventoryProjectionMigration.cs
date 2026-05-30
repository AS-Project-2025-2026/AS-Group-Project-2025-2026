using FluentMigrator;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.UpgradeTo500;

[NopSchemaMigration("2026-05-27 00:00:02", "Inventory projection table (InventoryProjectionRecord)", MigrationProcessType.NoMatter)]
public class InventoryProjectionMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        if (!Schema.Table("InventoryProjectionRecord").Exists())
        {
            Create.Table("InventoryProjectionRecord")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("ProductId").AsInt32().NotNullable()
                .WithColumn("SourceSystem").AsString(50).NotNullable()
                .WithColumn("ReportedQuantity").AsInt32().NotNullable()
                .WithColumn("LastConfirmedUtc").AsNopDateTime2().NotNullable()
                .WithColumn("IsStale").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("ConflictFlag").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("PendingReconciliation").AsBoolean().NotNullable().WithDefaultValue(false)
                .WithColumn("ResolvedAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("UpdatedAtUtc").AsNopDateTime2().NotNullable();

            Create.Index("UX_InventoryProjection_ProductSource")
                .OnTable("InventoryProjectionRecord")
                .OnColumn("ProductId").Ascending()
                .OnColumn("SourceSystem").Ascending()
                .WithOptions().Unique();
        }
    }
}
