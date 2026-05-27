namespace Nop.IntegrationWorker.Options;

public class WorkerOptions
{
    public int PollingIntervalSeconds { get; set; } = 5;
    public int BatchSize { get; set; } = 20;
}
