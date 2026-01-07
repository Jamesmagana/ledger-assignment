using Ledger.Application.Exceptions;
using Ledger.Application.Repositories;
using Ledger.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using System.Text.RegularExpressions;

namespace Ledger.Application.Services;

/// <summary>
/// Service implementation for User business operations.
/// Enforces validation rules, password security, and authentication.
/// </summary>
public class UserService : IUserService
{
    private readonly IUserRepository _repository;
    private readonly IPasswordHasher _passwordHasher;
    private const int MinPasswordLength = 8;
    private static readonly Regex EmailRegex = new(@"^[^@\s]+@[^@\s]+\.[^@\s]+$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

    public UserService(IUserRepository repository, IPasswordHasher passwordHasher)
    {
        _repository = repository;
        _passwordHasher = passwordHasher;
    }

    public async Task<User> CreateUserAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        // Validate and normalize email
        if (string.IsNullOrWhiteSpace(email))
        {
            throw new ValidationException("Email is required.", "INVALID_EMAIL");
        }

        email = email.Trim().ToLowerInvariant();

        if (!EmailRegex.IsMatch(email))
        {
            throw new ValidationException("Invalid email format.", "INVALID_EMAIL");
        }

        if (email.Length > 200)
        {
            throw new ValidationException("Email cannot exceed 200 characters.", "INVALID_EMAIL");
        }

        // Validate password
        if (string.IsNullOrWhiteSpace(password))
        {
            throw new ValidationException("Password is required.", "INVALID_PASSWORD");
        }

        ValidatePasswordStrength(password);

        // Check for duplicate email (case-insensitive)
        var existingUser = await _repository.FindByEmailAsync(email, cancellationToken);
        if (existingUser != null)
        {
            throw new ConflictException("A user with this email already exists.", "DUPLICATE_USER");
        }

        // Hash password
        var passwordHash = _passwordHasher.HashPassword(password);

        // Create user entity
        var user = new User(email, passwordHash, isEnabled: true);

        try
        {
            return await _repository.AddAsync(user, cancellationToken);
        }
        catch (DbUpdateException ex)
        {
            // Catch potential race conditions for unique constraint violation
            if (ex.InnerException?.Message?.Contains("IX_Users_Email_Normalized") == true)
            {
                throw new ConflictException("A user with this email already exists.", "DUPLICATE_USER");
            }
            throw;
        }
    }

    public async Task<User?> AuthenticateAsync(string email, string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
        {
            // Return null for invalid credentials (generic error, no user enumeration)
            return null;
        }

        // Find user by email (case-insensitive)
        var normalizedEmail = email.Trim().ToLowerInvariant();
        var user = await _repository.FindByEmailAsync(normalizedEmail, cancellationToken);

        // Always perform password verification (timing-safe, prevents user enumeration)
        // Use a dummy hash if user not found to maintain consistent timing
        var hashToVerify = user?.PasswordHash ?? "$2a$12$dummy.hash.to.prevent.timing.attacks.and.user.enumeration";
        var passwordValid = _passwordHasher.VerifyPassword(password, hashToVerify);

        // If user not found or password invalid, return null
        if (user == null || !passwordValid)
        {
            // Increment failed login attempts if user exists
            if (user != null)
            {
                var updatedUser = user.WithFailedLoginAttempts(user.FailedLoginAttempts + 1);
                await _repository.UpdateAsync(updatedUser, cancellationToken);
            }
            return null;
        }

        // Check if user is enabled
        if (!user.IsEnabled)
        {
            // Still increment failed attempts for disabled users
            var updatedUser = user.WithFailedLoginAttempts(user.FailedLoginAttempts + 1);
            await _repository.UpdateAsync(updatedUser, cancellationToken);
            return null;
        }

        // Authentication successful - update LastLoginAt and reset FailedLoginAttempts
        var successUser = user.WithLastLoginAt(DateTime.UtcNow);
        await _repository.UpdateAsync(successUser, cancellationToken);

        return successUser;
    }

    private static void ValidatePasswordStrength(string password)
    {
        if (password.Length < MinPasswordLength)
        {
            throw new ValidationException($"Password must be at least {MinPasswordLength} characters long.", "WEAK_PASSWORD");
        }

        // Require at least one letter and one number
        var hasLetter = password.Any(char.IsLetter);
        var hasNumber = password.Any(char.IsDigit);

        if (!hasLetter || !hasNumber)
        {
            throw new ValidationException("Password must contain at least one letter and one number.", "WEAK_PASSWORD");
        }
    }
}

