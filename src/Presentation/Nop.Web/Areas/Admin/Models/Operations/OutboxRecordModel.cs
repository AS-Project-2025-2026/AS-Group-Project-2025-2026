using Nop.Web.Framework.Models;

namespace Nop.Web.Areas.Admin.Models.Operations;

public partial record OutboxRecordModel : BaseNopEntityModel
{
    public int OrderId { get; set; }
    public string MessageType { get; set; }
    public string Status { get; set; }
    public int RetryCount { get; set; }
    public string CorrelationId { get; set; }
    public string IdempotencyKey { get; set; }
    public string CreatedAtUtc { get; set; }
    public string NextAttemptAtUtc { get; set; }
    public string LastError { get; set; }
}
