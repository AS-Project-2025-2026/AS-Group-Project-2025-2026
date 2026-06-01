# Inventory Staleness & Reconciliation — Implementation Guide

> Covers **QAS 2** (stale inventory) and **QAS 6** (POS vs WMS conflict) from the report.  
> These are the only two fully documented scenarios that are **not yet implemented in code**.

---

## 1. What the report promises (exact wording)

### QAS 2 — Stale inventory
> *"The commerce core detects that the stock record has exceeded its staleness threshold, marks it stale, revalidates stock before checkout completion, and records an inventory discrepancy for reconciliation."*
>
> **Response measure:** Product pages show stale stock status **within 30 seconds** of the staleness threshold being exceeded; overselling stays under **1%** of affected order lines; inventory discrepancies are queued for reconciliation **within 1 minute**.

### QAS 6 — POS vs WMS conflict
> *"POS reports 8 units in stock; WMS reports 3 units for the same SKU. The difference exceeds the configured tolerance of 2 units and persists for more than 5 minutes."*
>
> *"The system detects the discrepancy, sets `ConflictFlag` on the affected SKU, limits checkout commits to the lower reported quantity, and creates an operator-visible reconciliation task."*
>
> **Response measure:** Conflict detected and flagged **within 30 seconds**; checkout bounded by the lower quantity; reconciliation task visible to operators **within 1 minute**; if both sources agree within 30 minutes the flag is cleared automatically, otherwise operator action is required.

### Chapter 6 (Target Architecture) — exact field names
> *"`SourceSystem` (which external system last reported the value), `LastConfirmedUtc` (when the value was last confirmed), `IsStale` (set when the record exceeds its per-source freshness threshold), `ConflictFlag` (set when two sources disagree beyond a configured tolerance), and `PendingReconciliation` (set when a discrepancy is queued for operator review) — preventing propagating stale numbers to customers."*

---

## 2. What already exists (do not recreate)

| File | What it does |
|---|---|
| `InventorySyncService.cs` | Polls `/stock` every N seconds, calls `UpdateProductStockAsync` |
| `InventoryClient.cs` | `GET /stock` → `List<InventoryStockItem>(ProductId, Quantity)` |
| `InventoryStub/app.py` | Has modes: `normal`, `slow`, `unavailable`, **`stale`** (returns lower quantities) |
| `WorkerDataService.UpdateProductStockAsync` | `UPDATE Product SET StockQuantity = @qty WHERE Id = @id AND ManageInventoryMethodId = 1` |
| `Product` entity (nopCommerce) | Has `StockQuantity`, `ManageInventoryMethodId`, `UseMultipleWarehouses` |

The stub already has a `stale` mode that returns deliberately lower quantities — **the logic to detect and flag staleness on the nopCommerce side is entirely missing**.

---

## 3. New database table needed

### `InventoryProjectionRecord`

One row per `(ProductId, SourceSystem)` pair. Tracks freshness metadata separately from `Product.StockQuantity`.

```sql
CREATE TABLE InventoryProjectionRecord (
    Id                   INT            NOT NULL IDENTITY PRIMARY KEY,
    ProductId            INT            NOT NULL,
    SourceSystem         NVARCHAR(50)   NOT NULL,   -- 'wms' | 'pos'
    ReportedQuantity     INT            NOT NULL,
    LastConfirmedUtc     DATETIME2      NOT NULL,
    IsStale              BIT            NOT NULL DEFAULT 0,
    ConflictFlag         BIT            NOT NULL DEFAULT 0,
    PendingReconciliation BIT           NOT NULL DEFAULT 0,
    ResolvedAtUtc        DATETIME2      NULL,
    UpdatedAtUtc         DATETIME2      NOT NULL
);
CREATE UNIQUE INDEX UX_InventoryProjection_ProductSource
    ON InventoryProjectionRecord (ProductId, SourceSystem);
```

> **Why a separate table and not fields on `Product`?**  
> The report says *"local views of external state"* — this is a read model / projection, not the canonical Product. Adding staleness fields directly to `Product` would pollute nopCommerce's domain with integration metadata. A separate table keeps boundaries clean (ADR 3).

### Entity + Builder + Migration

**Pattern:** follow `CircuitBreakerStateRecord` / `CircuitBreakerStateMigration` exactly.

Files to create:
- `src/Libraries/Nop.Core/Domain/Integration/InventoryProjectionRecord.cs`
- `src/Libraries/Nop.Data/Mapping/Builders/Integration/InventoryProjectionRecordBuilder.cs`
- `src/Libraries/Nop.Data/Migrations/UpgradeTo500/InventoryProjectionMigration.cs`
  - Migration timestamp: `"2026-05-27 00:00:02"` (after `CircuitBreakerStateMigration`)

---

## 4. Changes to `InventorySyncService`

### 4.1 New options needed

Add to `InventoryOptions.cs`:
```csharp
public int StalenessThresholdSeconds { get; set; } = 120;  // mark stale after 2 min without update
public int ConflictToleranceUnits { get; set; } = 2;        // max units diff before ConflictFlag
public string SourceSystem { get; set; } = "wms";           // this stub is the WMS source
```

Add to `appsettings.json` and `docker-compose.yml` env vars:
```
Inventory__StalenessThresholdSeconds=120
Inventory__ConflictToleranceUnits=2
Inventory__SourceSystem=wms
```

### 4.2 New `SyncStockAsync` logic (pseudocode)

```
foreach item in stockItems:
    1. UPSERT InventoryProjectionRecord (ProductId, SourceSystem='wms')
       SET ReportedQuantity = item.Quantity
       SET LastConfirmedUtc = now
       SET IsStale = false
       SET UpdatedAtUtc = now

    2. Check for conflict:
       otherSource = SELECT FROM InventoryProjectionRecord
                     WHERE ProductId = item.ProductId AND SourceSystem != 'wms'
       if otherSource exists AND abs(item.Quantity - otherSource.Quantity) > ConflictToleranceUnits:
           SET ConflictFlag = true on BOTH records
           SET PendingReconciliation = true
           checkoutQty = min(item.Quantity, otherSource.Quantity)  -- use lower value
       else:
           SET ConflictFlag = false
           checkoutQty = item.Quantity

    3. UpdateProductStockAsync(item.ProductId, checkoutQty)

    4. Log structured:
       InventoryStale=false/true, ConflictFlag=true/false, CheckoutQty=N
```

### 4.3 Staleness detection (background scan)

A second loop (or within the same loop) that runs every `SyncIntervalSeconds`:

```
SELECT * FROM InventoryProjectionRecord
WHERE IsStale = 0
  AND DATEDIFF(SECOND, LastConfirmedUtc, GETUTCDATE()) > StalenessThresholdSeconds

foreach stale record:
    SET IsStale = true
    SET PendingReconciliation = true
    Log: ProductId={id} SourceSystem={src} MarkedStale=true LastConfirmedUtc={ts}
```

### 4.4 Auto-clear conflict (after 30 min both agree)

```
SELECT p1.*, p2.ReportedQuantity AS OtherQty
FROM InventoryProjectionRecord p1
JOIN InventoryProjectionRecord p2 ON p1.ProductId = p2.ProductId AND p1.SourceSystem != p2.SourceSystem
WHERE p1.ConflictFlag = 1
  AND abs(p1.ReportedQuantity - p2.ReportedQuantity) <= ConflictToleranceUnits
  AND DATEDIFF(MINUTE, p1.UpdatedAtUtc, GETUTCDATE()) >= 30

foreach resolved:
    SET ConflictFlag = false, PendingReconciliation = false, ResolvedAtUtc = now
    Log: ProductId={id} ConflictAutoResolved=true
```

---

## 5. New `WorkerDataService` methods needed

```csharp
Task UpsertInventoryProjectionAsync(int productId, string sourceSystem,
    int quantity, bool isStale, bool conflictFlag, bool pendingReconciliation);

Task<List<InventoryProjectionDto>> GetStaleOrConflictedProjectionsAsync();

Task MarkProjectionStaleAsync(int id);

Task ResolveConflictAsync(int productId);  // clears ConflictFlag on both source records
```

DTO:
```csharp
public class InventoryProjectionDto {
    public int Id { get; set; }
    public int ProductId { get; set; }
    public string SourceSystem { get; set; }
    public int ReportedQuantity { get; set; }
    public DateTime LastConfirmedUtc { get; set; }
    public bool IsStale { get; set; }
    public bool ConflictFlag { get; set; }
    public bool PendingReconciliation { get; set; }
    public DateTime? ResolvedAtUtc { get; set; }
    public DateTime UpdatedAtUtc { get; set; }
}
```

---

## 6. Operations View — Inventory tab

### New files (follow the OutboxRecord / DeadLetterRecord pattern exactly):

| File | Content |
|---|---|
| `Models/Operations/InventoryProjectionModel.cs` | All fields from DTO |
| `IOperationsModelFactory.cs` | Add `Task<IList<InventoryProjectionModel>> PrepareInventoryProjectionModelsAsync()` |
| `OperationsModelFactory.cs` | Implement: call `_integrationRecordService.GetStaleOrConflictedProjectionsAsync()`, map to model |
| `OperationsController.cs` | Add `[HttpGet] InventoryProjectionList()` → returns JSON |
| `IIntegrationRecordService.cs` | Add `Task<IList<InventoryProjectionRecord>> GetStaleOrConflictedProjectionsAsync()` |
| `IntegrationRecordService.cs` | Implement using `_inventoryProjectionRepository` |

### View (`Views/Operations/List.cshtml`)

Add a new card after the Circuit Breaker section:

```html
<div class="card card-default">
  <div class="card-header">
    <h5>Inventory Projection — Stale & Conflicted</h5>
    <button onclick="refreshInventory()">Refresh</button>
  </div>
  <table id="inventory-projection-table">
    <thead>ProductId | Source | Qty | LastConfirmed | IsStale | ConflictFlag | Pending</thead>
    <tbody id="inventory-projection-body">Loading...</tbody>
  </table>
</div>
```

JS: same pattern as `refreshCircuitBreakers()` — `$.get('/Admin/Operations/InventoryProjectionList')`.

Row colouring:
- `IsStale = true` → yellow row (`table-warning`)
- `ConflictFlag = true` → red row (`table-danger`)
- Both cleared → green (`table-success`)

---

## 7. Demo scenario to capture as evidence

### Staleness demo (QAS 2)
1. Start all services normally — inventory sync runs, all `IsStale = false`.
2. Set `STUB_MODE=unavailable` on inventory stub → sync stops receiving updates.
3. Wait `StalenessThresholdSeconds` (set to 30s for demo via env var).
4. Operations View → Inventory tab shows `IsStale = true` within 30s.
5. Restore stub to `normal` → next sync clears `IsStale`.

### Conflict demo (QAS 6)
1. The inventory stub only reports WMS quantities.
2. To simulate a POS conflict: call `POST /stock/report` on the Store POS stub with a quantity that differs by > `ConflictToleranceUnits`.
3. The Store POS stub publishes a `pos.stock.reported` message; the POS stock consumer writes `SourceSystem='pos'`, detects the divergence, sets `ConflictFlag = true`, and uses the lower quantity.
4. Operations View → Inventory tab shows the conflicted SKU in red.
5. After 30 min (or manual SQL update), auto-clear fires.

---

## 8. Configurable thresholds (add to docker-compose.yml)

```yaml
# For live demo — accelerated values
Inventory__StalenessThresholdSeconds: ${INVENTORY_STALENESS_THRESHOLD:-120}
Inventory__ConflictToleranceUnits: ${INVENTORY_CONFLICT_TOLERANCE:-2}
Inventory__SourceSystem: wms
```

For demo, set `INVENTORY_STALENESS_THRESHOLD=30` so staleness triggers in 30s.

---

## 9. Gotchas and constraints

| Issue | Notes |
|---|---|
| `Product.StockQuantity` is nopCommerce's checkout gate | The checkout pipeline reads `Product.StockQuantity` directly. To enforce the lower-quantity rule for conflicted SKUs, `UpdateProductStockAsync` must write `min(wms, pos)` — not the raw WMS value. |
| Migration timestamp ordering | Must be `"2026-05-27 00:00:02"` — after `CircuitBreakerStateMigration` (`00:00:01`) and after `OmnichannelIntegrationRecordsMigration` (`2026-05-21 00:00:03`). |
| `IRepository<InventoryProjectionRecord>` in nopCommerce DI | nopCommerce auto-registers `IRepository<T>` for all `BaseEntity` subclasses — no manual registration needed in `Program.cs` of the web app. For the worker's `WorkerDataService` (Dapper), use raw SQL as done for other tables. |
| The inventory stub only has one source (WMS) | POS stock is represented by `StorePosStub`, which publishes `pos.stock.reported` messages to RabbitMQ. The remaining gap is real POS vendor integration, not the demo event path. |
| `InventoryProjectionRecord` vs `Product` staleness visibility | Stale status is in `InventoryProjectionRecord`. The product listing page (`/catalog`) reads `Product.StockQuantity`. If you want stale status on product pages, you'd need to join or expose a flag — **out of scope for the spike**, just show it in the Operations View. |
| `PendingReconciliation` auto-clear | Only clears `ConflictFlag` when BOTH sources agree AND 30 min have elapsed. If only WMS reports and POS never updates, the flag stays until operator resolves. |

---

## 10. File checklist (ordered by dependency)

```
1. Nop.Core/Domain/Integration/InventoryProjectionRecord.cs          NEW
2. Nop.Data/Mapping/Builders/Integration/InventoryProjectionRecordBuilder.cs  NEW
3. Nop.Data/Migrations/UpgradeTo500/InventoryProjectionMigration.cs  NEW
4. Nop.IntegrationWorker/Options/InventoryOptions.cs                 MODIFY (+3 fields)
5. Nop.IntegrationWorker/Data/InventoryProjectionDto.cs              NEW
6. Nop.IntegrationWorker/Data/WorkerDataService.cs                   MODIFY (+4 methods)
7. Nop.IntegrationWorker/Services/InventorySyncService.cs            MODIFY (full rewrite)
8. Nop.IntegrationWorker/appsettings.json                            MODIFY
9. docker-compose.yml                                                MODIFY
10. Nop.Services/Integration/IIntegrationRecordService.cs            MODIFY (+1 method)
11. Nop.Services/Integration/IntegrationRecordService.cs             MODIFY (+repo inject +impl)
12. Nop.Web/Areas/Admin/Models/Operations/InventoryProjectionModel.cs  NEW
13. Nop.Web/Areas/Admin/Factories/IOperationsModelFactory.cs         MODIFY (+1 method)
14. Nop.Web/Areas/Admin/Factories/OperationsModelFactory.cs          MODIFY (+impl)
15. Nop.Web/Areas/Admin/Controllers/OperationsController.cs          MODIFY (+1 endpoint)
16. Nop.Web/Areas/Admin/Views/Operations/List.cshtml                 MODIFY (+card +JS)
```

---

## 11. Evidence to capture after implementation

| Screenshot/Log | What to show |
|---|---|
| `01-inventory-normal.png` | Operations View Inventory tab, all rows green, `IsStale=false` |
| `02-stub-unavailable.png` | Terminal: `STUB_MODE=unavailable` applied |
| `03-staleness-detected.png` | Operations View 30s later: row turns yellow, `IsStale=true` |
| `04-staleness-log.txt` | Worker log: `ProductId=1 SourceSystem=wms MarkedStale=true` |
| `05-conflict-injected.png` | SQL or stub call injecting POS=8 when WMS=3 |
| `06-conflict-flagged.png` | Operations View: row turns red, `ConflictFlag=true`, checkout bounded to 3 |
| `07-conflict-autocleared.png` | After 30 min (or manual SQL): row green, `ResolvedAtUtc` set |
