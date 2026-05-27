namespace Nop.IntegrationWorker.Models;

public class FulfillmentPayload
{
    public int OrderId { get; set; }
    public string CorrelationId { get; set; } = string.Empty;
    public string IdempotencyKey { get; set; } = string.Empty;
    public List<OrderItem> Items { get; set; } = [];

    public class OrderItem
    {
        public int ProductId { get; set; }
        public int Quantity { get; set; }
        public string Sku { get; set; } = string.Empty;
    }
}
