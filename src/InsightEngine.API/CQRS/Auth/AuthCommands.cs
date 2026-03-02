using InsightEngine.API.Configuration;
using InsightEngine.API.Services;
using InsightEngine.Domain.Core;
using InsightEngine.Domain.Entities;
using InsightEngine.Domain.Interfaces;
using InsightEngine.Domain.Settings;
using InsightEngine.Infra.Data.Identity;
using MediatR;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using System.Security.Claims;

namespace InsightEngine.API.CQRS.Auth;

public record AuthUserDto(Guid Id, string Email, string DisplayName, string AvatarUrl, string Plan);

public class AuthTokensResult
{
    public string AccessToken { get; set; } = string.Empty;
    public string RefreshToken { get; set; } = string.Empty;
    public int ExpiresIn { get; set; }
    public AuthUserDto User { get; set; } = new(Guid.Empty, string.Empty, string.Empty, string.Empty, "Free");
}

public class PlanInfoDto
{
    public string Plan { get; set; } = "Free";
    public string? Message { get; set; }
}

public record RegisterCommand(string Email, string Password, string? DisplayName) : IRequest<Result<AuthTokensResult>>;
public record LoginCommand(string Email, string Password) : IRequest<Result<AuthTokensResult>>;
public record RefreshTokenCommand(string AccessToken, string RefreshToken) : IRequest<Result<AuthTokensResult>>;
public record LogoutCommand(string RefreshToken) : IRequest<Result<bool>>;
public record GetMeQuery() : IRequest<Result<AuthUserDto>>;
public record UpdateMeProfileCommand(string DisplayName) : IRequest<Result<AuthUserDto>>;
public record UpdateMePasswordCommand(string CurrentPassword, string NewPassword) : IRequest<Result<bool>>;
public record UpdateMeAvatarCommand(string AvatarUrl) : IRequest<Result<AuthUserDto>>;
public record GetMePlanQuery() : IRequest<Result<PlanInfoDto>>;
public record UpgradeMePlanCommand(string TargetPlan) : IRequest<Result<PlanInfoDto>>;

public class RegisterCommandHandler : IRequestHandler<RegisterCommand, Result<AuthTokensResult>>
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RegisterCommandHandler(
        UserManager<ApplicationUser> users,
        IRefreshTokenRepository refreshTokens,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        IOptions<JwtSettings> jwtOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtOptions.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Result<AuthTokensResult>> Handle(RegisterCommand request, CancellationToken cancellationToken)
    {
        var existing = await _users.FindByEmailAsync(request.Email);
        if (existing is not null)
        {
            return Result.Failure<AuthTokensResult>("Email already in use.");
        }

        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = request.Email.Trim().ToLowerInvariant(),
            Email = request.Email.Trim().ToLowerInvariant(),
            DisplayName = string.IsNullOrWhiteSpace(request.DisplayName) ? request.Email : request.DisplayName.Trim(),
            Plan = "Free",
            IsActive = true
        };

        var createResult = await _users.CreateAsync(user, request.Password);
        if (!createResult.Succeeded)
        {
            return Result.Failure<AuthTokensResult>(createResult.Errors.Select(x => x.Description).ToList());
        }

        return await BuildAuthResultAsync(user, cancellationToken);
    }

    private async Task<Result<AuthTokensResult>> BuildAuthResultAsync(ApplicationUser user, CancellationToken cancellationToken)
    {
        var accessToken = _tokenService.GenerateAccessToken(user.Id, user.Email ?? string.Empty, user.Plan, user.DisplayName, ["User"]);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();
        var ip = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;

        var refreshToken = new RefreshToken(
            Guid.NewGuid(),
            user.Id,
            refreshTokenValue,
            DateTime.UtcNow.AddDays(_jwtSettings.RefreshExpirationInDays),
            ip);

        await _refreshTokens.AddAsync(refreshToken);
        await _unitOfWork.CommitAsync();

        return Result.Success(new AuthTokensResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresIn = _jwtSettings.ExpirationInMinutes * 60,
            User = new AuthUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.Plan)
        });
    }
}

public class LoginCommandHandler : IRequestHandler<LoginCommand, Result<AuthTokensResult>>
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public LoginCommandHandler(
        UserManager<ApplicationUser> users,
        IRefreshTokenRepository refreshTokens,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        IOptions<JwtSettings> jwtOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtOptions.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Result<AuthTokensResult>> Handle(LoginCommand request, CancellationToken cancellationToken)
    {
        var user = await _users.FindByEmailAsync(request.Email);
        if (user is null)
        {
            return Result.Failure<AuthTokensResult>("Invalid credentials.");
        }

        var passwordValid = await _users.CheckPasswordAsync(user, request.Password);
        if (!passwordValid)
        {
            return Result.Failure<AuthTokensResult>("Invalid credentials.");
        }

        if (!user.IsActive)
        {
            return Result.Failure<AuthTokensResult>("User is inactive.");
        }

        var accessToken = _tokenService.GenerateAccessToken(user.Id, user.Email ?? string.Empty, user.Plan, user.DisplayName, ["User"]);
        var refreshTokenValue = _tokenService.GenerateRefreshToken();
        var ip = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        var refreshToken = new RefreshToken(Guid.NewGuid(), user.Id, refreshTokenValue, DateTime.UtcNow.AddDays(_jwtSettings.RefreshExpirationInDays), ip);
        await _refreshTokens.AddAsync(refreshToken);
        await _unitOfWork.CommitAsync();

        return Result.Success(new AuthTokensResult
        {
            AccessToken = accessToken,
            RefreshToken = refreshTokenValue,
            ExpiresIn = _jwtSettings.ExpirationInMinutes * 60,
            User = new AuthUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.Plan)
        });
    }
}

public class RefreshTokenCommandHandler : IRequestHandler<RefreshTokenCommand, Result<AuthTokensResult>>
{
    private readonly UserManager<ApplicationUser> _users;
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly ITokenService _tokenService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly JwtSettings _jwtSettings;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public RefreshTokenCommandHandler(
        UserManager<ApplicationUser> users,
        IRefreshTokenRepository refreshTokens,
        ITokenService tokenService,
        IUnitOfWork unitOfWork,
        IOptions<JwtSettings> jwtOptions,
        IHttpContextAccessor httpContextAccessor)
    {
        _users = users;
        _refreshTokens = refreshTokens;
        _tokenService = tokenService;
        _unitOfWork = unitOfWork;
        _jwtSettings = jwtOptions.Value;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<Result<AuthTokensResult>> Handle(RefreshTokenCommand request, CancellationToken cancellationToken)
    {
        var principal = _tokenService.GetPrincipalFromExpiredToken(request.AccessToken);
        var subject = principal?.FindFirstValue(ClaimTypes.NameIdentifier) ?? principal?.FindFirstValue(ClaimTypes.Name);
        if (!Guid.TryParse(subject, out var userId))
        {
            return Result.Failure<AuthTokensResult>("Invalid access token.");
        }

        var storedRefreshToken = await _refreshTokens.GetByTokenAsync(request.RefreshToken, cancellationToken);
        if (storedRefreshToken is null || !storedRefreshToken.IsActive || storedRefreshToken.UserId != userId)
        {
            return Result.Failure<AuthTokensResult>("Invalid refresh token.");
        }

        var user = await _users.FindByIdAsync(userId.ToString());
        if (user is null || !user.IsActive)
        {
            return Result.Failure<AuthTokensResult>("User not found.");
        }

        var newRefreshTokenValue = _tokenService.GenerateRefreshToken();
        storedRefreshToken.Revoke(newRefreshTokenValue);

        var ip = _httpContextAccessor.HttpContext?.Connection?.RemoteIpAddress?.ToString() ?? string.Empty;
        var newRefreshToken = new RefreshToken(
            Guid.NewGuid(),
            userId,
            newRefreshTokenValue,
            DateTime.UtcNow.AddDays(_jwtSettings.RefreshExpirationInDays),
            ip);
        await _refreshTokens.AddAsync(newRefreshToken);

        var newAccessToken = _tokenService.GenerateAccessToken(user.Id, user.Email ?? string.Empty, user.Plan, user.DisplayName, ["User"]);
        await _unitOfWork.CommitAsync();

        return Result.Success(new AuthTokensResult
        {
            AccessToken = newAccessToken,
            RefreshToken = newRefreshTokenValue,
            ExpiresIn = _jwtSettings.ExpirationInMinutes * 60,
            User = new AuthUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.Plan)
        });
    }
}

public class LogoutCommandHandler : IRequestHandler<LogoutCommand, Result<bool>>
{
    private readonly IRefreshTokenRepository _refreshTokens;
    private readonly IUnitOfWork _unitOfWork;

    public LogoutCommandHandler(IRefreshTokenRepository refreshTokens, IUnitOfWork unitOfWork)
    {
        _refreshTokens = refreshTokens;
        _unitOfWork = unitOfWork;
    }

    public async Task<Result<bool>> Handle(LogoutCommand request, CancellationToken cancellationToken)
    {
        var token = await _refreshTokens.GetByTokenAsync(request.RefreshToken, cancellationToken);
        if (token is not null && token.IsActive)
        {
            token.Revoke();
            await _unitOfWork.CommitAsync();
        }

        return Result.Success(true);
    }
}

public class GetMeQueryHandler : IRequestHandler<GetMeQuery, Result<AuthUserDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _users;

    public GetMeQueryHandler(ICurrentUser currentUser, UserManager<ApplicationUser> users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<AuthUserDto>> Handle(GetMeQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<AuthUserDto>("Unauthorized");
        }

        var user = await _users.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null)
        {
            return Result.Failure<AuthUserDto>("User not found.");
        }

        return Result.Success(new AuthUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.Plan));
    }
}

public class UpdateMeProfileCommandHandler : IRequestHandler<UpdateMeProfileCommand, Result<AuthUserDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _users;

    public UpdateMeProfileCommandHandler(ICurrentUser currentUser, UserManager<ApplicationUser> users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<AuthUserDto>> Handle(UpdateMeProfileCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<AuthUserDto>("Unauthorized");
        }

        var user = await _users.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null)
        {
            return Result.Failure<AuthUserDto>("User not found.");
        }

        user.DisplayName = request.DisplayName.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _users.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Result.Failure<AuthUserDto>(updateResult.Errors.Select(x => x.Description).ToList());
        }

        return Result.Success(new AuthUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.Plan));
    }
}

public class UpdateMePasswordCommandHandler : IRequestHandler<UpdateMePasswordCommand, Result<bool>>
{
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _users;

    public UpdateMePasswordCommandHandler(ICurrentUser currentUser, UserManager<ApplicationUser> users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<bool>> Handle(UpdateMePasswordCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<bool>("Unauthorized");
        }

        var user = await _users.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null)
        {
            return Result.Failure<bool>("User not found.");
        }

        var result = await _users.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
        if (!result.Succeeded)
        {
            return Result.Failure<bool>(result.Errors.Select(x => x.Description).ToList());
        }

        return Result.Success(true);
    }
}

public class UpdateMeAvatarCommandHandler : IRequestHandler<UpdateMeAvatarCommand, Result<AuthUserDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _users;

    public UpdateMeAvatarCommandHandler(ICurrentUser currentUser, UserManager<ApplicationUser> users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<AuthUserDto>> Handle(UpdateMeAvatarCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<AuthUserDto>("Unauthorized");
        }

        var user = await _users.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null)
        {
            return Result.Failure<AuthUserDto>("User not found.");
        }

        user.AvatarUrl = request.AvatarUrl?.Trim() ?? string.Empty;
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _users.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Result.Failure<AuthUserDto>(updateResult.Errors.Select(x => x.Description).ToList());
        }

        return Result.Success(new AuthUserDto(user.Id, user.Email ?? string.Empty, user.DisplayName, user.AvatarUrl, user.Plan));
    }
}

public class GetMePlanQueryHandler : IRequestHandler<GetMePlanQuery, Result<PlanInfoDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _users;

    public GetMePlanQueryHandler(ICurrentUser currentUser, UserManager<ApplicationUser> users)
    {
        _currentUser = currentUser;
        _users = users;
    }

    public async Task<Result<PlanInfoDto>> Handle(GetMePlanQuery request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<PlanInfoDto>("Unauthorized");
        }

        var user = await _users.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null)
        {
            return Result.Failure<PlanInfoDto>("User not found.");
        }

        return Result.Success(new PlanInfoDto { Plan = user.Plan });
    }
}

public class UpgradeMePlanCommandHandler : IRequestHandler<UpgradeMePlanCommand, Result<PlanInfoDto>>
{
    private readonly ICurrentUser _currentUser;
    private readonly UserManager<ApplicationUser> _users;
    private readonly InsightEngineFeatures _features;

    public UpgradeMePlanCommandHandler(
        ICurrentUser currentUser,
        UserManager<ApplicationUser> users,
        InsightEngineFeatures features)
    {
        _currentUser = currentUser;
        _users = users;
        _features = features;
    }

    public async Task<Result<PlanInfoDto>> Handle(UpgradeMePlanCommand request, CancellationToken cancellationToken)
    {
        if (!_currentUser.IsAuthenticated || !_currentUser.UserId.HasValue)
        {
            return Result.Failure<PlanInfoDto>("Unauthorized");
        }

        if (!_features.FakePlanUpgradeEnabled)
        {
            return Result.Failure<PlanInfoDto>("Plan upgrade is disabled.");
        }

        var user = await _users.FindByIdAsync(_currentUser.UserId.Value.ToString());
        if (user is null)
        {
            return Result.Failure<PlanInfoDto>("User not found.");
        }

        user.Plan = request.TargetPlan.Trim();
        user.UpdatedAt = DateTime.UtcNow;

        var updateResult = await _users.UpdateAsync(user);
        if (!updateResult.Succeeded)
        {
            return Result.Failure<PlanInfoDto>(updateResult.Errors.Select(x => x.Description).ToList());
        }

        return Result.Success(new PlanInfoDto
        {
            Plan = user.Plan,
            Message = "Plan upgraded (fake mode)."
        });
    }
}
