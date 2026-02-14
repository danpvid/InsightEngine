using System.Security.Claims;

namespace InsightEngine.API.Services;

public interface ITokenService
{
    string GenerateToken(string userId, string email, IEnumerable<string>? roles = null);
}
