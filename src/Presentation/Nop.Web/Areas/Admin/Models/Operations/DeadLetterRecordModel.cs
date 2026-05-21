using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Operations;

public partial record DeadLetterRecordModel : BaseNopEntityModel
{
    public string CorrelationId { get; set; }
    public string Adapter { get; set; }
    public string EscalationState { get; set; }
    public string FailureReason { get; set; }
    public string IdempotencyKey { get; set; }
    public string CreatedAtUtc { get; set; }
    public string LastAttemptAtUtc { get; set; }
}
