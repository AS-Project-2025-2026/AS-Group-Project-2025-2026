namespace Nop.Core.Domain.Integration;

public partial class IdempotencyRecord : BaseEntity
{
    public string Key { get; set; }
    public string Outcome { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
