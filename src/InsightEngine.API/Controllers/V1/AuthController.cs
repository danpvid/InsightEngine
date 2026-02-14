using InsightEngine.API.Services;
using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers.V1;

[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
public class AuthController : BaseController
{
    private readonly ITokenService _tokenService;

    public AuthController(
        IDomainNotificationHandler notificationHandler,
        IMediator mediator,
        ITokenService tokenService) : base(notificationHandler, mediator)
    {
        _tokenService = tokenService;
    }

    /// <summary>
    /// Endpoint de exemplo para autenticação
    /// </summary>
    /// <param name="request">Credenciais do usuário</param>
    /// <returns>Token JWT</returns>
    [HttpPost("login")]
    [AllowAnonymous]
    public IActionResult Login([FromBody] LoginRequest request)
    {
        // TODO: Implementar validação real de usuário e senha
        // Este é apenas um exemplo para demonstração do JWT
        
        if (string.IsNullOrEmpty(request.Email) || string.IsNullOrEmpty(request.Password))
        {
            return BadRequest(new
            {
                success = false,
                message = "Email e senha são obrigatórios"
            });
        }

        // Exemplo: validar credenciais (implementar sua lógica real aqui)
        // Por enquanto, aceita qualquer credencial para demonstração
        
        var userId = Guid.NewGuid().ToString();
        var roles = new[] { "User", "Admin" };
        
        var token = _tokenService.GenerateToken(userId, request.Email, roles);

        return Ok(new
        {
            success = true,
            data = new
            {
                token,
                expiresIn = 480, // minutos
                user = new
                {
                    id = userId,
                    email = request.Email,
                    roles
                }
            }
        });
    }

    /// <summary>
    /// Endpoint de exemplo protegido por autenticação
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    public IActionResult GetProfile()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;
        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value);

        return Ok(new
        {
            success = true,
            data = new
            {
                userId,
                email,
                roles
            }
        });
    }
}

public class LoginRequest
{
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}
