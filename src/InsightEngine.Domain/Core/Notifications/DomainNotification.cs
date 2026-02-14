namespace InsightEngine.Domain.Core.Notifications;

public class DomainNotification
{
    public string Key { get; }
    public string Value { get; }
    public DateTime Timestamp { get; }

    public DomainNotification(string key, string value)
    {
        Key = key;
        Value = value;
        Timestamp = DateTime.UtcNow;
    }
}
