namespace Nop.IntegrationWorker.Options;

public class InventoryOptions
{
    public string BaseUrl { get; set; } = "http://localhost:5083";
    public int SyncIntervalSeconds { get; set; } = 30;
    public int StalenessThresholdSeconds { get; set; } = 120;
    public int ConflictToleranceUnits { get; set; } = 2;
    public string SourceSystem { get; set; } = "wms";
}
