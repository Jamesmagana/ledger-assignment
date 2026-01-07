using Ledger.Application.Exceptions;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for authentication operations.
/// Orchestrates user authentication and JWT token generation.
/// </summary>
public class AuthenticationService : IAuthenticationService
{
    private readonly IUserService _userService;
    private readonly IJwtTokenService _jwtTokenService;

    public AuthenticationService(IUserService userService, IJwtTokenService jwtTokenService)
    {
        _userService = userService;
        _jwtTokenService = jwtTokenService;
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

        return new LoginResult
        {
            Token = token,
            UserId = user.Id,
            Email = user.Email
        };
    }
}

