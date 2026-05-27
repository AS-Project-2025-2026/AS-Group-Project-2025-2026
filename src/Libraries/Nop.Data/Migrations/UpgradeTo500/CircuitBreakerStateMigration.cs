using FluentMigrator;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.UpgradeTo500;

[NopSchemaMigration("2026-05-27 00:00:01", "Circuit breaker state table (CircuitBreakerStateRecord)", MigrationProcessType.NoMatter)]
public class CircuitBreakerStateMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        if (!Schema.Table("CircuitBreakerStateRecord").Exists())
        {
            Create.Table("CircuitBreakerStateRecord")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("Adapter").AsString(100).NotNullable()
                .WithColumn("State").AsString(20).NotNullable()
                .WithColumn("FailureCount").AsInt32().NotNullable()
                .WithColumn("OpenedAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("NextProbeAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("LastError").AsString(1000).Nullable()
                .WithColumn("UpdatedAtUtc").AsNopDateTime2().NotNullable();

            Create.Index("IX_CircuitBreakerStateRecord_Adapter")
                .OnTable("CircuitBreakerStateRecord").OnColumn("Adapter").Unique();
        }
    }
}
