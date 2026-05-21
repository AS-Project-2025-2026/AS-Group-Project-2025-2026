namespace Nop.Services.Integration;

public static class IntegrationDefaults
{
    public static class OutboxStatus
    {
        public const string Pending = "Pending";
        public const string Retrying = "Retrying";
        public const string Published = "Published";
        public const string Failed = "Failed";
    }

    public static class DlEscalationState
    {
        public const string New = "New";
        public const string Acknowledged = "Acknowledged";
        public const string Resolved = "Resolved";
        public const string Requeued = "Requeued";
    }

    public static class MessageType
    {
        public const string FulfillmentRequested = "FulfillmentRequested";
    }
}
