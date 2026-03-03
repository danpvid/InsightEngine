namespace InsightEngine.API.Configuration;

public class AdminSeedSettings
{
    public const string SectionName = "AdminSeed";

    public bool Enabled { get; set; } = true;
    public string Email { get; set; } = "admin@insightengine.local";
    public string Password { get; set; } = "Admin@123456";
    public string DisplayName { get; set; } = "Admin";
    public string Plan { get; set; } = "Enterprise";
    public string AvatarUrl { get; set; } = string.Empty;
}
