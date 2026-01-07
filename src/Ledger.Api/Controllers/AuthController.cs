using Ledger.Api.DTOs.Auth;
using Ledger.Application.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ledger.Api.Controllers;

/// <summary>
/// Controller for authentication operations.
/// </summary>
[AllowAnonymous] // Login endpoint must be public
[ApiController]
[Route("api/auth")]
public class AuthController : ControllerBase
{
    private readonly IAuthenticationService _authenticationService;

    public AuthController(IAuthenticationService authenticationService)
    {
        _authenticationService = authenticationService;
    }

    /// <summary>
    /// Authenticates a user and returns a JWT token.
    /// </summary>
    /// <param name="request">Login request with email and password.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JWT token and user information.</returns>
    [HttpPost("login")]
    [ProducesResponseType(typeof(LoginResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> Login([FromBody] LoginRequest request, CancellationToken cancellationToken)
    {
        var result = await _authenticationService.LoginAsync(request.Email, request.Password, cancellationToken);
        
        var response = new LoginResponse
        {
            Token = result.Token,
            UserId = result.UserId,
            Email = result.Email
        };
        
        return Ok(response);
    }
}

