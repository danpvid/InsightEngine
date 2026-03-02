using System.Security.Claims;

namespace InsightEngine.API.Services;

public interface ITokenService
{
    string GenerateAccessToken(
        Guid userId,
        string email,
        string plan,
        string displayName,
        IEnumerable<string>? roles = null);
    string GenerateRefreshToken();
    ClaimsPrincipal? GetPrincipalFromExpiredToken(string accessToken);
}
