namespace Ledger.Domain.Entities;

/// <summary>
/// Represents a user in the system.
/// Users are uniquely identified by email (case-insensitive).
/// Passwords are hashed and never stored in plain text.
/// </summary>
public class User : BaseEntity
{
    /// <summary>
    /// The user's email address. Must be unique case-insensitively.
    /// </summary>
    public string Email { get; init; } = string.Empty;

    /// <summary>
    /// The hashed password. Never exposed in API responses.
    /// </summary>
    public string PasswordHash { get; init; } = string.Empty;

    /// <summary>
    /// Indicates whether the user account is enabled.
    /// Disabled users cannot log in.
    /// </summary>
    public bool IsEnabled { get; init; }

    /// <summary>
    /// The number of consecutive failed login attempts.
    /// Reset to 0 on successful login.
    /// </summary>
    public int FailedLoginAttempts { get; init; }

    /// <summary>
    /// UTC timestamp of the last successful login.
    /// Null if the user has never logged in.
    /// </summary>
    public DateTime? LastLoginAt { get; init; }

    /// <summary>
    /// Initializes a new instance of the User entity.
    /// </summary>
    /// <param name="email">The user's email address.</param>
    /// <param name="passwordHash">The hashed password.</param>
    /// <param name="isEnabled">Whether the user is enabled (default: true).</param>
    public User(string email, string passwordHash, bool isEnabled = true)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ArgumentException("Email cannot be null or empty.", nameof(email));
        }

        if (string.IsNullOrWhiteSpace(passwordHash))
        {
            throw new ArgumentException("Password hash cannot be null or empty.", nameof(passwordHash));
        }

        Email = email;
        PasswordHash = passwordHash;
        IsEnabled = isEnabled;
        FailedLoginAttempts = 0;
    }

    /// <summary>
    /// Creates a new user with updated IsEnabled state.
    /// </summary>
    public User WithIsEnabled(bool isEnabled)
    {
        return new User(Email, PasswordHash, isEnabled)
        {
            Id = this.Id,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt,
            FailedLoginAttempts = this.FailedLoginAttempts,
            LastLoginAt = this.LastLoginAt
        };
    }

    /// <summary>
    /// Creates a new user with updated FailedLoginAttempts.
    /// </summary>
    public User WithFailedLoginAttempts(int failedLoginAttempts)
    {
        return new User(Email, PasswordHash, IsEnabled)
        {
            Id = this.Id,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt,
            FailedLoginAttempts = failedLoginAttempts,
            LastLoginAt = this.LastLoginAt
        };
    }

    /// <summary>
    /// Creates a new user with updated LastLoginAt timestamp.
    /// </summary>
    public User WithLastLoginAt(DateTime lastLoginAt)
    {
        return new User(Email, PasswordHash, IsEnabled)
        {
            Id = this.Id,
            CreatedAt = this.CreatedAt,
            UpdatedAt = this.UpdatedAt,
            FailedLoginAttempts = 0, // Reset failed attempts on successful login
            LastLoginAt = lastLoginAt
        };
    }
}

