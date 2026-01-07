using Ledger.Application.Services;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class PasswordHasherTests
{
    private readonly PasswordHasher _hasher;

    public PasswordHasherTests()
    {
        _hasher = new PasswordHasher();
    }

    [Fact]
    public void HashPassword_ValidPassword_ReturnsNonEmptyHash()
    {
        // Arrange
        var password = "TestPassword123";

        // Act
        var hash = _hasher.HashPassword(password);

        // Assert
        Assert.NotNull(hash);
        Assert.NotEmpty(hash);
        Assert.NotEqual(password, hash); // Hash should be different from password
    }

    [Fact]
    public void HashPassword_SamePassword_ReturnsDifferentHashes()
    {
        // Arrange
        var password = "TestPassword123";

        // Act
        var hash1 = _hasher.HashPassword(password);
        var hash2 = _hasher.HashPassword(password);

        // Assert
        // BCrypt includes salt, so same password should produce different hashes
        Assert.NotEqual(hash1, hash2);
    }

    [Fact]
    public void VerifyPassword_CorrectPassword_ReturnsTrue()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword(password, hash);

        // Assert
        Assert.True(result);
    }

    [Fact]
    public void VerifyPassword_IncorrectPassword_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var wrongPassword = "WrongPassword123";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword(wrongPassword, hash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_EmptyPassword_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var hash = _hasher.HashPassword(password);

        // Act
        var result = _hasher.VerifyPassword(string.Empty, hash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void VerifyPassword_InvalidHash_ReturnsFalse()
    {
        // Arrange
        var password = "TestPassword123";
        var invalidHash = "invalid.hash.here";

        // Act
        var result = _hasher.VerifyPassword(password, invalidHash);

        // Assert
        Assert.False(result);
    }

    [Fact]
    public void HashPassword_EmptyPassword_ThrowsArgumentException()
    {
        // Arrange
        var password = string.Empty;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(password));
    }

    [Fact]
    public void HashPassword_NullPassword_ThrowsArgumentException()
    {
        // Arrange
        string? password = null;

        // Act & Assert
        Assert.Throws<ArgumentException>(() => _hasher.HashPassword(password!));
    }
}

