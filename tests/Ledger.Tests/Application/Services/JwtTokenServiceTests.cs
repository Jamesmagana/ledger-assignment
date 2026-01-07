using Ledger.Application.Services;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class JwtTokenServiceTests
{
    private readonly JwtTokenService _tokenService;
    private readonly JwtTokenSettings _settings;

    public JwtTokenServiceTests()
    {
        _settings = new JwtTokenSettings
        {
            Issuer = "https://test-issuer.com",
            Audience = "https://test-audience.com",
            SecretKey = "TestSecretKey_Minimum32CharactersLong_ForTesting"
        };

        var options = Options.Create(_settings);
        _tokenService = new JwtTokenService(options);
    }

    [Fact]
    public void GenerateToken_ValidInput_ReturnsNonEmptyToken()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";

        // Act
        var token = _tokenService.GenerateToken(userId, email);

        // Assert
        Assert.NotNull(token);
        Assert.NotEmpty(token);
    }

    [Fact]
    public void GenerateToken_ValidInput_ProducesValidJwt()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";

        // Act
        var token = _tokenService.GenerateToken(userId, email);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);
        Assert.NotNull(jsonToken);
    }

    [Fact]
    public void GenerateToken_ContainsCorrectClaims()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";

        // Act
        var token = _tokenService.GenerateToken(userId, email);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);
        
        Assert.Equal(userId.ToString(), jsonToken.Claims.First(c => c.Type == "sub").Value);
        Assert.Equal(email, jsonToken.Claims.First(c => c.Type == "email").Value);
        Assert.NotNull(jsonToken.Claims.FirstOrDefault(c => c.Type == "jti"));
    }

    [Fact]
    public void GenerateToken_HasCorrectIssuerAndAudience()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";

        // Act
        var token = _tokenService.GenerateToken(userId, email);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);
        
        Assert.Equal(_settings.Issuer, jsonToken.Issuer);
        Assert.Contains(_settings.Audience, jsonToken.Audiences);
    }

    [Fact]
    public void GenerateToken_HasExpiration()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";

        // Act
        var token = _tokenService.GenerateToken(userId, email);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var jsonToken = handler.ReadJwtToken(token);
        
        Assert.True(jsonToken.ValidTo > DateTime.UtcNow);
        Assert.True(jsonToken.ValidTo <= DateTime.UtcNow.AddHours(2)); // Should be around 1 hour
    }

    [Fact]
    public void GenerateToken_CanBeValidated()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var email = "test@example.com";
        var key = new SymmetricSecurityKey(System.Text.Encoding.UTF8.GetBytes(_settings.SecretKey));

        // Act
        var token = _tokenService.GenerateToken(userId, email);

        // Assert
        var handler = new JwtSecurityTokenHandler();
        var validationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = _settings.Issuer,
            ValidAudience = _settings.Audience,
            IssuerSigningKey = key
        };

        var principal = handler.ValidateToken(token, validationParameters, out var validatedToken);
        Assert.NotNull(principal);
        Assert.NotNull(validatedToken);
    }
}

