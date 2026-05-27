namespace Nop.IntegrationWorker.Options;

public class InventoryOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5083";
    public int SyncIntervalSeconds { get; set; } = 30;
}
