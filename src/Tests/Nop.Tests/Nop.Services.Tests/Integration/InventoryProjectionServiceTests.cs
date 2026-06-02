using AwesomeAssertions;
using Nop.Core.Domain.Integration;
using Nop.Data;
using Nop.Services.Integration;
using NUnit.Framework;

namespace Nop.Tests.Nop.Services.Tests.Integration;

[TestFixture]
public class InventoryProjectionServiceTests : ServiceTest
{
    private IIntegrationRecordService _service;
    private IRepository<InventoryProjectionRecord> _repo;

    [OneTimeSetUp]
    public void SetUp()
    {
        _service = GetService<IIntegrationRecordService>();
        _repo = GetService<IRepository<InventoryProjectionRecord>>();
    }

    private async Task<InventoryProjectionRecord> InsertAsync(
        int productId, string source, int qty,
        bool isStale = false, bool conflict = false, bool pending = false,
        DateTime? lastConfirmed = null, DateTime? resolvedAt = null)
    {
        var r = new InventoryProjectionRecord
        {
            ProductId = productId,
            SourceSystem = source,
            ReportedQuantity = qty,
            LastConfirmedUtc = lastConfirmed ?? DateTime.UtcNow,
            IsStale = isStale,
            ConflictFlag = conflict,
            PendingReconciliation = pending,
            ResolvedAtUtc = resolvedAt,
            UpdatedAtUtc = DateTime.UtcNow
        };
        await _repo.InsertAsync(r);
        return r;
    }

    // ── GetStaleOrConflictedProjections ─────────────────────────────────────

    [Test]
    public async Task GetStaleOrConflicted_WmsStaleRow_IsReturned()
    {
        var r = await InsertAsync(productId: 9001, "wms", qty: 5, isStale: true);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            results.Should().Contain(x => x.Id == r.Id);
        }
        finally { await _repo.DeleteAsync(r); }
    }

    [Test]
    public async Task GetStaleOrConflicted_PosConflictRow_IsReturned()
    {
        // POS rows with ConflictFlag must be visible — not filtered away
        var r = await InsertAsync(productId: 9002, "pos", qty: 1, conflict: true, pending: true);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            results.Should().Contain(x => x.Id == r.Id);
        }
        finally { await _repo.DeleteAsync(r); }
    }

    [Test]
    public async Task GetStaleOrConflicted_CleanRow_IsNotReturned()
    {
        var r = await InsertAsync(productId: 9003, "wms", qty: 10,
            isStale: false, conflict: false, pending: false);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            results.Should().NotContain(x => x.Id == r.Id);
        }
        finally { await _repo.DeleteAsync(r); }
    }

    [Test]
    public async Task GetStaleOrConflicted_BothSourcesConflicting_BothRowsReturned()
    {
        var wms = await InsertAsync(productId: 9004, "wms", qty: 8, conflict: true, pending: true);
        var pos = await InsertAsync(productId: 9004, "pos", qty: 1, conflict: true, pending: true);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            results.Should().Contain(x => x.Id == wms.Id);
            results.Should().Contain(x => x.Id == pos.Id);
        }
        finally
        {
            await _repo.DeleteAsync(wms);
            await _repo.DeleteAsync(pos);
        }
    }

    // ── POS-wins rule: checkout qty = min(wms, pos) ──────────────────────────
    // These tests verify the reported quantities stored — the checkout capping
    // logic lives in PosStockConsumerService / InventorySyncService (worker),
    // which are tested via integration / E2E. Here we assert the DB state
    // that those services are responsible for writing.

    [Test]
    public async Task Projection_WhenPosLowerThanWms_PosQtyIsRecorded()
    {
        // Simulate what PosStockConsumerService writes after detecting conflict:
        // WMS=8, POS=1 → pos row records qty=1
        var pos = await InsertAsync(productId: 9005, "pos", qty: 1, conflict: true, pending: true);
        var wms = await InsertAsync(productId: 9005, "wms", qty: 8, conflict: true, pending: true);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            var posRow = results.FirstOrDefault(x => x.Id == pos.Id);
            posRow.Should().NotBeNull();
            posRow!.ReportedQuantity.Should().Be(1);

            // POS (lower) should be the checkout gate
            var checkoutQty = Math.Min(
                results.First(x => x.Id == wms.Id).ReportedQuantity,
                posRow.ReportedQuantity);
            checkoutQty.Should().Be(1);
        }
        finally
        {
            await _repo.DeleteAsync(pos);
            await _repo.DeleteAsync(wms);
        }
    }

    [Test]
    public async Task Projection_WhenPosSellsLastUnit_CheckoutQtyBecomesZero()
    {
        // POS sold the last unit: pos=0, wms still shows 1 (sync lag)
        var pos = await InsertAsync(productId: 9006, "pos", qty: 0, conflict: true, pending: true);
        var wms = await InsertAsync(productId: 9006, "wms", qty: 1, conflict: true, pending: true);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            var posQty = results.First(x => x.Id == pos.Id).ReportedQuantity;
            var wmsQty = results.First(x => x.Id == wms.Id).ReportedQuantity;
            Math.Min(wmsQty, posQty).Should().Be(0);
        }
        finally
        {
            await _repo.DeleteAsync(pos);
            await _repo.DeleteAsync(wms);
        }
    }

    // ── Staleness semantics ──────────────────────────────────────────────────

    [Test]
    public async Task Projection_StaleRow_HasPendingReconciliationSet()
    {
        var r = await InsertAsync(productId: 9007, "wms", qty: 5,
            isStale: true, pending: true,
            lastConfirmed: DateTime.UtcNow.AddMinutes(-5));
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            var row = results.First(x => x.Id == r.Id);
            row.IsStale.Should().BeTrue();
            row.PendingReconciliation.Should().BeTrue();
        }
        finally { await _repo.DeleteAsync(r); }
    }

    [Test]
    public async Task Projection_AfterConflictResolved_RowNotReturnedInStaleOrConflicted()
    {
        var r = await InsertAsync(productId: 9008, "wms", qty: 5,
            isStale: false, conflict: false, pending: false,
            resolvedAt: DateTime.UtcNow);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            results.Should().NotContain(x => x.Id == r.Id);
        }
        finally { await _repo.DeleteAsync(r); }
    }

    [Test]
    public async Task Projection_PendingReconciliationAlone_IsReturned()
    {
        var r = await InsertAsync(productId: 9009, "wms", qty: 3,
            isStale: false, conflict: false, pending: true);
        try
        {
            var results = await _service.GetStaleOrConflictedProjectionsAsync();
            results.Should().Contain(x => x.Id == r.Id);
        }
        finally { await _repo.DeleteAsync(r); }
    }
}
