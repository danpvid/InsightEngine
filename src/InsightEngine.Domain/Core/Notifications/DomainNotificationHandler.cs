namespace InsightEngine.Domain.Core.Notifications;

public class DomainNotificationHandler : IDomainNotificationHandler
{
    private readonly List<DomainNotification> _notifications;

    public DomainNotificationHandler()
    {
        _notifications = new List<DomainNotification>();
    }

    public bool HasNotifications()
    {
        return _notifications.Any();
    }

    public List<DomainNotification> GetNotifications()
    {
        return _notifications;
    }

    public void AddNotification(string key, string value)
    {
        _notifications.Add(new DomainNotification(key, value));
    }

    public void AddNotification(DomainNotification notification)
    {
        _notifications.Add(notification);
    }

    public void ClearNotifications()
    {
        _notifications.Clear();
    }
}
