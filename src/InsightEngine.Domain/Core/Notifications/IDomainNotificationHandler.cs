namespace InsightEngine.Domain.Core.Notifications;

public interface IDomainNotificationHandler
{
    bool HasNotifications();
    List<DomainNotification> GetNotifications();
    void AddNotification(string key, string value);
    void AddNotification(DomainNotification notification);
    void ClearNotifications();
}
