using Microsoft.IdentityModel.Tokens;
using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;

namespace Ledger.Tests.Helpers;

/// <summary>
/// Helper class for generating test JWT tokens.
/// </summary>
public static class TestJwtTokenHelper
{
    /// <summary>
    /// Generates a valid JWT token for testing.
    /// </summary>
    public static string GenerateToken(
        string issuer,
        string audience,
        string secretKey,
        DateTime? expires = null,
        DateTime? notBefore = null,
        IEnumerable<Claim>? claims = null)
    {
        var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(secretKey));
        var credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);

        var tokenClaims = claims?.ToList() ?? new List<Claim>();

        var token = new JwtSecurityToken(
            issuer: issuer,
            audience: audience,
            claims: tokenClaims,
            expires: expires ?? DateTime.UtcNow.AddHours(1),
            notBefore: notBefore ?? DateTime.UtcNow,
            signingCredentials: credentials);

        return new JwtSecurityTokenHandler().WriteToken(token);
    }

    /// <summary>
    /// Generates an expired JWT token for testing.
    /// </summary>
    public static string GenerateExpiredToken(
        string issuer,
        string audience,
        string secretKey)
    {
        return GenerateToken(
            issuer,
            audience,
            secretKey,
            expires: DateTime.UtcNow.AddHours(-1),
            notBefore: DateTime.UtcNow.AddHours(-2));
    }

    /// <summary>
    /// Generates a token with invalid signature (different secret key).
    /// </summary>
    public static string GenerateTokenWithInvalidSignature(
        string issuer,
        string audience,
        string correctSecretKey,
        string wrongSecretKey)
    {
        return GenerateToken(
            issuer,
            audience,
            wrongSecretKey, // Use wrong secret key
            expires: DateTime.UtcNow.AddHours(1));
    }

    /// <summary>
    /// Generates a token with wrong issuer.
    /// </summary>
    public static string GenerateTokenWithWrongIssuer(
        string wrongIssuer,
        string audience,
        string secretKey)
    {
        return GenerateToken(
            wrongIssuer, // Wrong issuer
            audience,
            secretKey,
            expires: DateTime.UtcNow.AddHours(1));
    }

    /// <summary>
    /// Generates a token with wrong audience.
    /// </summary>
    public static string GenerateTokenWithWrongAudience(
        string issuer,
        string wrongAudience,
        string secretKey)
    {
        return GenerateToken(
            issuer,
            wrongAudience, // Wrong audience
            secretKey,
            expires: DateTime.UtcNow.AddHours(1));
    }

    /// <summary>
    /// Generates a token that is not yet valid (nbf in the future).
    /// </summary>
    public static string GenerateTokenNotYetValid(
        string issuer,
        string audience,
        string secretKey)
    {
        return GenerateToken(
            issuer,
            audience,
            secretKey,
            expires: DateTime.UtcNow.AddHours(2),
            notBefore: DateTime.UtcNow.AddHours(1)); // Not valid until 1 hour from now
    }
}

