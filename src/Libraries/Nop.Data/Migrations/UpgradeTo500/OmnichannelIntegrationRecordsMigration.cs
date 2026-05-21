using FluentMigrator;
using Nop.Data.Extensions;

namespace Nop.Data.Migrations.UpgradeTo500;

[NopSchemaMigration("2026-05-21 00:00:03", "Omnichannel integration tables (OutboxRecord, DeadLetterRecord, IdempotencyRecord)", MigrationProcessType.NoMatter)]
public class OmnichannelIntegrationRecordsMigration : ForwardOnlyMigration
{
    public override void Up()
    {
        if (!Schema.Table("OutboxRecord").Exists())
        {
            Create.Table("OutboxRecord")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("OrderId").AsInt32().NotNullable()
                .WithColumn("MessageType").AsString(100).NotNullable()
                .WithColumn("Payload").AsString(int.MaxValue).NotNullable()
                .WithColumn("CorrelationId").AsString(100).NotNullable()
                .WithColumn("IdempotencyKey").AsString(100).NotNullable()
                .WithColumn("Status").AsString(50).NotNullable()
                .WithColumn("RetryCount").AsInt32().NotNullable()
                .WithColumn("CreatedAtUtc").AsNopDateTime2().NotNullable()
                .WithColumn("NextAttemptAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("PublishedAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("LastError").AsString(1000).Nullable();

            Create.Index("IX_OutboxRecord_Status").OnTable("OutboxRecord").OnColumn("Status");
            Create.Index("IX_OutboxRecord_OrderId").OnTable("OutboxRecord").OnColumn("OrderId");
            Create.Index("IX_OutboxRecord_NextAttemptAtUtc").OnTable("OutboxRecord").OnColumn("NextAttemptAtUtc");
        }

        if (!Schema.Table("DeadLetterRecord").Exists())
        {
            Create.Table("DeadLetterRecord")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("OriginalOutboxRecordId").AsInt32().Nullable()
                .WithColumn("Payload").AsString(int.MaxValue).NotNullable()
                .WithColumn("IdempotencyKey").AsString(100).NotNullable()
                .WithColumn("CorrelationId").AsString(100).NotNullable()
                .WithColumn("Adapter").AsString(100).NotNullable()
                .WithColumn("FailureReason").AsString(1000).Nullable()
                .WithColumn("FirstAttemptAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("LastAttemptAtUtc").AsNopDateTime2().Nullable()
                .WithColumn("EscalationState").AsString(50).NotNullable()
                .WithColumn("CreatedAtUtc").AsNopDateTime2().NotNullable()
                .WithColumn("ResolvedAtUtc").AsNopDateTime2().Nullable();

            Create.Index("IX_DeadLetterRecord_EscalationState").OnTable("DeadLetterRecord").OnColumn("EscalationState");
            Create.Index("IX_DeadLetterRecord_CorrelationId").OnTable("DeadLetterRecord").OnColumn("CorrelationId");
        }

        if (!Schema.Table("IdempotencyRecord").Exists())
        {
            Create.Table("IdempotencyRecord")
                .WithColumn("Id").AsInt32().NotNullable().PrimaryKey().Identity()
                .WithColumn("Key").AsString(100).NotNullable()
                .WithColumn("Outcome").AsString(500).NotNullable()
                .WithColumn("CreatedAtUtc").AsNopDateTime2().NotNullable()
                .WithColumn("ExpiresAtUtc").AsNopDateTime2().Nullable();

            Create.Index("IX_IdempotencyRecord_Key").OnTable("IdempotencyRecord").OnColumn("Key").Unique();
        }
    }
}
