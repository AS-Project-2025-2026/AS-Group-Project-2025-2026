using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Operations;

public partial record CircuitBreakerStateModel : BaseNopEntityModel
{
    public string Adapter { get; set; }
    public string State { get; set; }
    public int FailureCount { get; set; }
    public string OpenedAtUtc { get; set; }
    public string NextProbeAtUtc { get; set; }
    public string LastError { get; set; }
    public string UpdatedAtUtc { get; set; }
}
