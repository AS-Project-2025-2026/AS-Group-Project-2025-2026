using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Operations;

public partial record InventoryProjectionModel : BaseNopEntityModel
{
    public int ProductId { get; set; }
    public string SourceSystem { get; set; }
    public int ReportedQuantity { get; set; }
    public string LastConfirmedUtc { get; set; }
    public bool IsStale { get; set; }
    public bool ConflictFlag { get; set; }
    public bool PendingReconciliation { get; set; }
    public string ResolvedAtUtc { get; set; }
    public string UpdatedAtUtc { get; set; }
}
