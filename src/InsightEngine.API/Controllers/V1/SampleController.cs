using InsightEngine.Domain.Core.Notifications;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace InsightEngine.API.Controllers.V1;

/// <summary>
/// Exemplo de controller protegido com autenticação JWT
/// </summary>
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]
[Authorize] // Todos os endpoints requerem autenticação
public class SampleController : BaseController
{
    public SampleController(
        IDomainNotificationHandler notificationHandler,
        IMediator mediator) : base(notificationHandler, mediator)
    {
    }

    /// <summary>
    /// Endpoint público - não requer autenticação
    /// </summary>
    [HttpGet("public")]
    [AllowAnonymous]
    public IActionResult GetPublicData()
    {
        return Ok(new
        {
            success = true,
            message = "Este endpoint é público e não requer autenticação",
            data = new
            {
                timestamp = DateTime.UtcNow,
                version = "1.0.0"
            }
        });
    }

    /// <summary>
    /// Endpoint protegido - requer autenticação
    /// </summary>
    [HttpGet("protected")]
    public IActionResult GetProtectedData()
    {
        var userId = User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value;
        var email = User.FindFirst(System.Security.Claims.ClaimTypes.Email)?.Value;

        return Ok(new
        {
            success = true,
            message = "Você está autenticado!",
            data = new
            {
                userId,
                email,
                timestamp = DateTime.UtcNow
            }
        });
    }

    /// <summary>
    /// Endpoint que requer role específica
    /// </summary>
    [HttpGet("admin")]
    [Authorize(Roles = "Admin")]
    public IActionResult GetAdminData()
    {
        return Ok(new
        {
            success = true,
            message = "Você tem permissões de administrador!",
            data = new
            {
                timestamp = DateTime.UtcNow,
                adminLevel = "Full Access"
            }
        });
    }

    /// <summary>
    /// Endpoint que aceita múltiplas roles
    /// </summary>
    [HttpGet("users")]
    [Authorize(Roles = "Admin,User")]
    public IActionResult GetUsersList()
    {
        var roles = User.FindAll(System.Security.Claims.ClaimTypes.Role).Select(c => c.Value);

        return Ok(new
        {
            success = true,
            message = "Lista de usuários",
            yourRoles = roles,
            data = new[]
            {
                new { id = 1, name = "John Doe", email = "john@example.com" },
                new { id = 2, name = "Jane Smith", email = "jane@example.com" }
            }
        });
    }
}
