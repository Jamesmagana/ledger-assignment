using Ledger.Application.Services;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class SensitiveFieldExcluderTests
{
    [Fact]
    public void SerializeExcludingSensitive_ExcludesPasswordHash()
    {
        // Arrange
        var obj = new
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            PasswordHash = "$2a$12$hashed.password.here",
            Name = "Test User"
        };

        // Act
        var result = SensitiveFieldExcluder.SerializeExcludingSensitive(obj);

        // Assert
        Assert.DoesNotContain("PasswordHash", result.Keys);
        Assert.DoesNotContain("passwordHash", result.Keys);
        Assert.Contains("id", result.Keys);
        Assert.Contains("email", result.Keys);
        Assert.Contains("name", result.Keys);
    }

    [Fact]
    public void SerializeExcludingSensitive_ExcludesPassword()
    {
        // Arrange
        var obj = new
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Password = "PlainTextPassword123"
        };

        // Act
        var result = SensitiveFieldExcluder.SerializeExcludingSensitive(obj);

        // Assert
        Assert.DoesNotContain("Password", result.Keys);
        Assert.DoesNotContain("password", result.Keys);
        Assert.Contains("id", result.Keys);
        Assert.Contains("email", result.Keys);
    }

    [Fact]
    public void SerializeExcludingSensitive_ExcludesSecret()
    {
        // Arrange
        var obj = new
        {
            Id = Guid.NewGuid(),
            Secret = "secret-value",
            SecretKey = "secret-key-value"
        };

        // Act
        var result = SensitiveFieldExcluder.SerializeExcludingSensitive(obj);

        // Assert
        Assert.DoesNotContain("Secret", result.Keys);
        Assert.DoesNotContain("secret", result.Keys);
        Assert.DoesNotContain("SecretKey", result.Keys);
        Assert.DoesNotContain("secretKey", result.Keys);
    }

    [Fact]
    public void SerializeExcludingSensitive_PreservesNonSensitiveFields()
    {
        // Arrange
        var obj = new
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            Name = "Test User",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        // Act
        var result = SensitiveFieldExcluder.SerializeExcludingSensitive(obj);

        // Assert
        Assert.Contains("id", result.Keys);
        Assert.Contains("email", result.Keys);
        Assert.Contains("name", result.Keys);
        Assert.Contains("isActive", result.Keys);
        Assert.Contains("createdAt", result.Keys);
    }

    [Fact]
    public void SerializeExcludingSensitive_NullObject_ReturnsEmptyDictionary()
    {
        // Arrange
        object? obj = null;

        // Act
        var result = SensitiveFieldExcluder.SerializeExcludingSensitive(obj);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public void SerializeToJsonExcludingSensitive_ReturnsValidJson()
    {
        // Arrange
        var obj = new
        {
            Id = Guid.NewGuid(),
            Email = "test@example.com",
            PasswordHash = "$2a$12$hashed.password.here"
        };

        // Act
        var json = SensitiveFieldExcluder.SerializeToJsonExcludingSensitive(obj);

        // Assert
        Assert.NotNull(json);
        Assert.DoesNotContain("PasswordHash", json);
        Assert.DoesNotContain("passwordHash", json);
        Assert.Contains("id", json);
        Assert.Contains("email", json);
    }

    [Fact]
    public void SerializeToJsonExcludingSensitive_NullObject_ReturnsNull()
    {
        // Arrange
        object? obj = null;

        // Act
        var json = SensitiveFieldExcluder.SerializeToJsonExcludingSensitive(obj);

        // Assert
        Assert.Null(json);
    }

    [Fact]
    public void SerializeExcludingSensitive_CaseInsensitive()
    {
        // Arrange
        var obj = new
        {
            Id = Guid.NewGuid(),
            passwordHash = "$2a$12$hashed.password.here", // lowercase
            PASSWORD = "PlainTextPassword123", // uppercase
            Secret = "secret-value"
        };

        // Act
        var result = SensitiveFieldExcluder.SerializeExcludingSensitive(obj);

        // Assert
        Assert.DoesNotContain("passwordHash", result.Keys);
        Assert.DoesNotContain("PASSWORD", result.Keys);
        Assert.DoesNotContain("Secret", result.Keys);
    }
}

