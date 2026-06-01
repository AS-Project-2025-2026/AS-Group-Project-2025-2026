namespace Nop.IntegrationWorker.Options;

public class RabbitMqOptions
{
    public string Host { get; set; } = "localhost";
    public int Port { get; set; } = 5672;
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public string Exchange { get; set; } = "verdemart.integration";
    public string FulfillmentQueue { get; set; } = "fulfillment.requests";
    public string ShippingQueue { get; set; } = "shipping.requests";
    public string StoreOpsQueue { get; set; } = "storeops.requests";
    public string PosStockQueue { get; set; } = "pos.stock.updates";
    public string CustomerSupportQueue { get; set; } = "customersupport.requests";
}
