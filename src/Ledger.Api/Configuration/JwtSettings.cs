namespace Ledger.Api.Configuration;

/// <summary>
/// Configuration settings for JWT authentication.
/// </summary>
public class JwtSettings
{
    /// <summary>
    /// The issuer of the JWT token.
    /// </summary>
    public string Issuer { get; set; } = string.Empty;

    /// <summary>
    /// The audience of the JWT token.
    /// </summary>
    public string Audience { get; set; } = string.Empty;

    /// <summary>
    /// The secret key used to sign and validate JWT tokens.
    /// Should be read from environment variable in production.
    /// </summary>
    public string SecretKey { get; set; } = string.Empty;

    /// <summary>
    /// Clock skew in seconds to account for time differences between servers.
    /// Default: 300 seconds (5 minutes).
    /// </summary>
    public int ClockSkewSeconds { get; set; } = 300;
}

