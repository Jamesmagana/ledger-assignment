namespace Ledger.Infrastructure.Data.Services;

/// <summary>
/// Service interface for extracting current user context from HTTP context.
/// </summary>
public interface IUserContextService
{
    /// <summary>
    /// Gets the current user ID from JWT claims.
    /// Returns null if no user is authenticated or claim is invalid.
    /// </summary>
    Guid? GetCurrentUserId();
}

