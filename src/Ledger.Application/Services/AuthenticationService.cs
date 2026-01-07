using Ledger.Application.Exceptions;
using Microsoft.AspNetCore.Http;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for authentication operations.
/// Orchestrates user authentication and JWT token generation.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _jwtTokenService;
    private readonly IAuditLogService _auditLogService;
    private readonly IHttpContextAccessor _httpContextAccessor;

    public AuthenticationService(
        IUserService userService,
        IJwtTokenService jwtTokenService,
        IAuditLogService auditLogService,
        IHttpContextAccessor httpContextAccessor)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
        _auditLogService = auditLogService;
        _httpContextAccessor = httpContextAccessor;
    }

    public async Task<LoginResult> LoginAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // Authenticate user (includes password verification and enabled check)
        var user = await _userService.AuthenticateAsync(email, password, cancellationToken);

        if (user == null)
        {
            // Generic error message to prevent user enumeration
            throw new UnauthorizedException("Invalid email or password.", "INVALID_CREDENTIALS");
        }

        // Generate JWT token
        var token = _jwtTokenService.GenerateToken(user.Id, user.Email);

        // Audit log the login event
        var correlationId = GetCorrelationId();
        await _auditLogService.LogEntityChangeAsync(
            entityName: "User",
            entityId: user.Id,
            action: "LOGIN",
            oldValues: null,
            newValues: new { LastLoginAt = user.LastLoginAt },
            performedBy: user.Id,
            correlationId: correlationId,
            cancellationToken: cancellationToken);

        return new LoginResult
        {
            Token = token,
            UserId = user.Id,
            Email = user.Email
        };
    }

    private string GetCorrelationId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && httpContext.Items.TryGetValue("CorrelationId", out var correlationIdObj))
        {
            return correlationIdObj?.ToString() ?? Guid.NewGuid().ToString();
        }

        return Guid.NewGuid().ToString();
    }
}

