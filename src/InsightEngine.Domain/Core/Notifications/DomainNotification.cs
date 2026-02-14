namespace InsightEngine.Domain.Core.Notifications;

public class DomainNotification
{
    public string Key { get; }
    public string Value { get; }
    public ErrorType Type { get; }
    public DateTime Timestamp { get; }

    public DomainNotification(string key, string value, ErrorType type = ErrorType.Validation)
    {
        Key = key;
        Value = value;
        Type = type;
        Timestamp = DateTime.UtcNow;
    }

    // Factory methods para criar notificações tipadas
    public static DomainNotification Validation(string key, string message) 
        => new(key, message, ErrorType.Validation);

    public static DomainNotification NotFound(string key, string message) 
        => new(key, message, ErrorType.NotFound);

    public static DomainNotification Conflict(string key, string message) 
        => new(key, message, ErrorType.Conflict);

    public static DomainNotification UnprocessableEntity(string key, string message) 
        => new(key, message, ErrorType.UnprocessableEntity);

    public static DomainNotification InternalError(string key, string message) 
        => new(key, message, ErrorType.InternalError);
}
