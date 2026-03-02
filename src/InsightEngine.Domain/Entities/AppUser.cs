namespace InsightEngine.Domain.Entities;

public class AppUser : Core.Models.Entity
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string DisplayName { get; private set; } = string.Empty;
    public string AvatarUrl { get; private set; } = string.Empty;
    public string Plan { get; private set; } = "Free";
    public bool IsActive { get; private set; } = true;

    protected AppUser() { }

    public AppUser(Guid id, string email, string passwordHash, string displayName)
    {
        Id = id;
        Email = email.Trim().ToLowerInvariant();
        PasswordHash = passwordHash;
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? email : displayName.Trim();
    }

    public void UpdateProfile(string displayName)
    {
        DisplayName = string.IsNullOrWhiteSpace(displayName) ? DisplayName : displayName.Trim();
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdateAvatar(string avatarUrl)
    {
        AvatarUrl = avatarUrl?.Trim() ?? string.Empty;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpdatePassword(string passwordHash)
    {
        PasswordHash = passwordHash;
        UpdatedAt = DateTime.UtcNow;
    }

    public void UpgradePlan(string plan)
    {
        Plan = string.IsNullOrWhiteSpace(plan) ? Plan : plan.Trim();
        UpdatedAt = DateTime.UtcNow;
    }
}
