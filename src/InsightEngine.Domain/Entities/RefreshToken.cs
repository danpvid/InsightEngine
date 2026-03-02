namespace InsightEngine.Domain.Entities;

public class RefreshToken : Core.Models.Entity
{
    public Guid UserId { get; private set; }
    public string Token { get; private set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; private set; }
    public DateTime? RevokedAtUtc { get; private set; }
    public string? ReplacedByToken { get; private set; }
    public string CreatedByIp { get; private set; } = string.Empty;

    protected RefreshToken() { }

    public RefreshToken(Guid id, Guid userId, string token, DateTime expiresAtUtc, string createdByIp = "")
    {
        Id = id;
        UserId = userId;
        Token = token;
        ExpiresAtUtc = expiresAtUtc;
        CreatedByIp = createdByIp?.Trim() ?? string.Empty;
    }

    public bool IsActive => RevokedAtUtc == null && ExpiresAtUtc > DateTime.UtcNow;

    public void Revoke(string? replacedByToken = null)
    {
        RevokedAtUtc = DateTime.UtcNow;
        ReplacedByToken = replacedByToken;
        UpdatedAt = DateTime.UtcNow;
    }
}
