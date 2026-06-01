namespace Nop.IntegrationWorker.Models;

public class PosStockPayload
{
    public int ProductId { get; set; }
    public int Quantity { get; set; }
    public string SourceSystem { get; set; } = "pos";
    public string? Location { get; set; }
    public string? CorrelationId { get; set; }
    public string? IdempotencyKey { get; set; }
    public DateTime ReportedAtUtc { get; set; }
}
