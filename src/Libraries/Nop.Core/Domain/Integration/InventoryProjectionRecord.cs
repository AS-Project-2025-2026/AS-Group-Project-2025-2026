namespace Nop.Core.Domain.Integration;

public partial class InventoryProjectionRecord : BaseEntity
{
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
