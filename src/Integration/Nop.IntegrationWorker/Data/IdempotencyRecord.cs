namespace Nop.IntegrationWorker.Data;

public class IdempotencyRecord
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Outcome { get; set; } = string.Empty;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ExpiresAtUtc { get; set; }
}
