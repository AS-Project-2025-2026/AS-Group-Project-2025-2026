using System.Text.Json;
using AwesomeAssertions;
using Nop.Core.Domain.Integration;
using Nop.Data;
using Nop.Services.Integration;
using Nop.Services.Orders;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Integration;

[TestFixture]
public class IntegrationRecordServiceTests : ServiceTest
{
    private IIntegrationRecordService _integrationRecordService;
    private IOrderService _orderService;

    [OneTimeSetUp]
    public void SetUp()
    {
        _integrationRecordService = GetService<IIntegrationRecordService>();
        _orderService = GetService<IOrderService>();
    }

    // ── OutboxRecord creation ────────────────────────────────────────────────

    [Test]
    public async Task CreateFulfillmentOutboxRecord_SetsStatusPending()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.Status.Should().Be(IntegrationDefaults.OutboxStatus.Pending);
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_SetsRetryCountToZero()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.RetryCount.Should().Be(0);
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_SetsCorrectOrderId()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.OrderId.Should().Be(order.Id);
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_SetsMessageTypeFulfillmentRequested()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.MessageType.Should().Be(IntegrationDefaults.MessageType.FulfillmentRequested);
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_IdempotencyKeyIsValidUuid()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.IdempotencyKey.Should().NotBeNullOrEmpty();
        Guid.TryParse(record.IdempotencyKey, out _).Should().BeTrue();
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_CorrelationIdContainsOrderId()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.CorrelationId.Should().Be($"order-{order.Id}");
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_PayloadContainsOrderId()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        var doc = JsonDocument.Parse(record.Payload);
        doc.RootElement.GetProperty("orderId").GetInt32().Should().Be(order.Id);
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_TwoCallsProduceDifferentIdempotencyKeys()
    {
        var order = await _orderService.GetOrderByIdAsync(1);

        var r1 = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);
        var r2 = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        r1.IdempotencyKey.Should().NotBe(r2.IdempotencyKey);
    }

    [Test]
    public async Task CreateFulfillmentOutboxRecord_PersistsWithPositiveId()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        record.Id.Should().BeGreaterThan(0);

        // cleanup
        await GetService<IRepository<OutboxRecord>>().DeleteAsync(record);
    }

    // ── SearchOutboxRecords ──────────────────────────────────────────────────

    [Test]
    public async Task SearchOutboxRecords_WithNoFilter_ReturnsAllRecords()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        var results = await _integrationRecordService.SearchOutboxRecordsAsync();

        results.Should().NotBeEmpty();

        await GetService<IRepository<OutboxRecord>>().DeleteAsync(record);
    }

    [Test]
    public async Task SearchOutboxRecords_FilterByPendingStatus_ReturnsOnlyPending()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        var results = await _integrationRecordService.SearchOutboxRecordsAsync(IntegrationDefaults.OutboxStatus.Pending);

        results.All(r => r.Status == IntegrationDefaults.OutboxStatus.Pending).Should().BeTrue();

        await GetService<IRepository<OutboxRecord>>().DeleteAsync(record);
    }

    [Test]
    public async Task SearchOutboxRecords_FilterByUnusedStatus_ReturnsEmpty()
    {
        var results = await _integrationRecordService.SearchOutboxRecordsAsync("NonExistentStatus_XYZ");

        results.Should().BeEmpty();
    }

    // ── RequeueOutboxRecord ──────────────────────────────────────────────────

    [Test]
    public async Task RequeueOutboxRecord_ResetsStatusToPending()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        // Simulate failure state
        record.Status = IntegrationDefaults.OutboxStatus.Failed;
        record.RetryCount = 5;
        record.LastError = "timeout";
        await GetService<IRepository<OutboxRecord>>().UpdateAsync(record);

        await _integrationRecordService.RequeueOutboxRecordAsync(record.Id);

        var updated = await GetService<IRepository<OutboxRecord>>().GetByIdAsync(record.Id);
        updated.Status.Should().Be(IntegrationDefaults.OutboxStatus.Pending);
        updated.RetryCount.Should().Be(0);
        updated.LastError.Should().BeNullOrEmpty();

        await GetService<IRepository<OutboxRecord>>().DeleteAsync(updated);
    }

    [Test]
    public async Task RequeueOutboxRecord_SetsNextAttemptToNow()
    {
        var order = await _orderService.GetOrderByIdAsync(1);
        var record = await _integrationRecordService.CreateFulfillmentOutboxRecordAsync(order);

        await _integrationRecordService.RequeueOutboxRecordAsync(record.Id);

        var updated = await GetService<IRepository<OutboxRecord>>().GetByIdAsync(record.Id);
        updated.NextAttemptAtUtc.Should().NotBeNull();
        updated.NextAttemptAtUtc!.Value.Should().BeCloseTo(DateTime.UtcNow, TimeSpan.FromSeconds(5));

        await GetService<IRepository<OutboxRecord>>().DeleteAsync(updated);
    }

    [Test]
    public void RequeueOutboxRecord_WithInvalidId_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _integrationRecordService.RequeueOutboxRecordAsync(int.MaxValue));
    }

    // ── DeadLetterRecord + RequeueDeadLetter ────────────────────────────────

    [Test]
    public async Task RequeueDeadLetterRecord_CreatesNewOutboxRecord()
    {
        var dl = new DeadLetterRecord
        {
            Payload = "{\"orderId\":1}",
            IdempotencyKey = Guid.NewGuid().ToString(),
            CorrelationId = "order-1",
            Adapter = "WarehouseAdapter",
            FailureReason = "timeout after 10 retries",
            EscalationState = IntegrationDefaults.DlEscalationState.New,
            CreatedAtUtc = DateTime.UtcNow
        };
        await GetService<IRepository<DeadLetterRecord>>().InsertAsync(dl);

        var outboxCountBefore = (await _integrationRecordService.SearchOutboxRecordsAsync()).Count;

        await _integrationRecordService.RequeueDeadLetterRecordAsync(dl.Id);

        var outboxCountAfter = (await _integrationRecordService.SearchOutboxRecordsAsync()).Count;
        outboxCountAfter.Should().Be(outboxCountBefore + 1);

        await GetService<IRepository<DeadLetterRecord>>().DeleteAsync(dl);
    }

    [Test]
    public async Task RequeueDeadLetterRecord_MarksDLAsRequeued()
    {
        var dl = new DeadLetterRecord
        {
            Payload = "{\"orderId\":1}",
            IdempotencyKey = Guid.NewGuid().ToString(),
            CorrelationId = "order-1",
            Adapter = "WarehouseAdapter",
            EscalationState = IntegrationDefaults.DlEscalationState.New,
            CreatedAtUtc = DateTime.UtcNow
        };
        await GetService<IRepository<DeadLetterRecord>>().InsertAsync(dl);

        await _integrationRecordService.RequeueDeadLetterRecordAsync(dl.Id);

        var updated = await GetService<IRepository<DeadLetterRecord>>().GetByIdAsync(dl.Id);
        updated.EscalationState.Should().Be(IntegrationDefaults.DlEscalationState.Requeued);
        updated.ResolvedAtUtc.Should().NotBeNull();

        await GetService<IRepository<DeadLetterRecord>>().DeleteAsync(updated);
    }

    [Test]
    public void RequeueDeadLetterRecord_WithInvalidId_ThrowsArgumentException()
    {
        Assert.ThrowsAsync<ArgumentException>(
            () => _integrationRecordService.RequeueDeadLetterRecordAsync(int.MaxValue));
    }

    // ── SearchDeadLetterRecords ──────────────────────────────────────────────

    [Test]
    public async Task SearchDeadLetterRecords_FilterByEscalationState_ReturnsOnlyMatching()
    {
        var dl = new DeadLetterRecord
        {
            Payload = "{}",
            IdempotencyKey = Guid.NewGuid().ToString(),
            CorrelationId = "order-99",
            Adapter = "WarehouseAdapter",
            EscalationState = IntegrationDefaults.DlEscalationState.Acknowledged,
            CreatedAtUtc = DateTime.UtcNow
        };
        await GetService<IRepository<DeadLetterRecord>>().InsertAsync(dl);

        var results = await _integrationRecordService.SearchDeadLetterRecordsAsync(
            IntegrationDefaults.DlEscalationState.Acknowledged);

        results.All(r => r.EscalationState == IntegrationDefaults.DlEscalationState.Acknowledged)
            .Should().BeTrue();

        await GetService<IRepository<DeadLetterRecord>>().DeleteAsync(dl);
    }

    // ── IdempotencyRecord ────────────────────────────────────────────────────

    [Test]
    public async Task InsertIdempotencyRecord_CanBeRetrievedByKey()
    {
        var key = Guid.NewGuid().ToString();
        var record = new IdempotencyRecord
        {
            Key = key,
            Outcome = "success",
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddHours(48)
        };

        await _integrationRecordService.InsertIdempotencyRecordAsync(record);

        var retrieved = await _integrationRecordService.GetIdempotencyRecordByKeyAsync(key);
        retrieved.Should().NotBeNull();
        retrieved!.Key.Should().Be(key);
        retrieved.Outcome.Should().Be("success");

        await GetService<IRepository<IdempotencyRecord>>().DeleteAsync(record);
    }

    [Test]
    public async Task GetIdempotencyRecordByKey_UnknownKey_ReturnsNull()
    {
        var result = await _integrationRecordService.GetIdempotencyRecordByKeyAsync("nonexistent-key-xyz-123");

        result.Should().BeNull();
    }

    [Test]
    public async Task InsertIdempotencyRecord_AssignsPositiveId()
    {
        var key = Guid.NewGuid().ToString();
        var record = new IdempotencyRecord
        {
            Key = key,
            Outcome = "processed",
            CreatedAtUtc = DateTime.UtcNow
        };

        await _integrationRecordService.InsertIdempotencyRecordAsync(record);

        record.Id.Should().BeGreaterThan(0);

        await GetService<IRepository<IdempotencyRecord>>().DeleteAsync(record);
    }
}
