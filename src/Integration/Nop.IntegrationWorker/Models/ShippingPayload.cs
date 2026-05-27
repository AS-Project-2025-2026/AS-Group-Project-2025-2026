namespace Nop.IntegrationWorker.Models;

public class ShippingPayload
{
    public int OrderId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public string WarehouseReference { get; set; } = string.Empty;
}
