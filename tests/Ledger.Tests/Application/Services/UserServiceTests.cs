using Ledger.Application.Exceptions;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Entities;
using Moq;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class UserServiceTests
{
    private readonly Mock<IUserRepository> _repositoryMock;
    private readonly Mock<IPasswordHasher> _passwordHasherMock;
    private readonly UserService _service;

    public UserServiceTests()
    {
        _repositoryMock = new Mock<IUserRepository>();
        _passwordHasherMock = new Mock<IPasswordHasher>();
        _service = new UserService(_repositoryMock.Object, _passwordHasherMock.Object);
    }

    [Fact]
    public async Task CreateUserAsync_ValidInput_ReturnsUser()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";
        var passwordHash = "$2a$12$hashed.password.here";

        _repositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);
        _passwordHasherMock.Setup(h => h.HashPassword(password))
            .Returns(passwordHash);

        var expectedUser = new User(email, passwordHash, true);
        _repositoryMock.Setup(r => r.AddAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(expectedUser);

        // Act
        var result = await _service.CreateUserAsync(email, password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(email.ToLowerInvariant(), result.Email);
        Assert.Equal(passwordHash, result.PasswordHash);
        Assert.True(result.IsEnabled);
        _passwordHasherMock.Verify(h => h.HashPassword(password), Times.Once);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateUserAsync_InvalidEmail_ThrowsValidationException(string invalidEmail)
    {
        // Arrange
        var password = "Password123";

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateUserAsync(invalidEmail!, password));
    }

    [Theory]
    [InlineData("invalid-email")]
    [InlineData("missing@domain")]
    [InlineData("@missinglocal.com")]
    public async Task CreateUserAsync_InvalidEmailFormat_ThrowsValidationException(string invalidEmail)
    {
        // Arrange
        var password = "Password123";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateUserAsync(invalidEmail, password));
        Assert.Equal("INVALID_EMAIL", exception.ReasonCode);
    }

    [Fact]
    public async Task CreateUserAsync_DuplicateEmail_ThrowsConflictException()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";
        var existingUser = new User(email, "existing-hash", true);

        _repositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingUser);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ConflictException>(() =>
            _service.CreateUserAsync(email, password));
        Assert.Equal("DUPLICATE_USER", exception.ReasonCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task CreateUserAsync_EmptyPassword_ThrowsValidationException(string invalidPassword)
    {
        // Arrange
        var email = "test@example.com";

        // Act & Assert
        await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateUserAsync(email, invalidPassword!));
    }

    [Theory]
    [InlineData("short")]
    [InlineData("1234567")]
    public async Task CreateUserAsync_WeakPassword_ThrowsValidationException(string weakPassword)
    {
        // Arrange
        var email = "test@example.com";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateUserAsync(email, weakPassword));
        Assert.Equal("WEAK_PASSWORD", exception.ReasonCode);
    }

    [Theory]
    [InlineData("onlyletters")]
    [InlineData("12345678")]
    public async Task CreateUserAsync_PasswordWithoutLetterOrNumber_ThrowsValidationException(string weakPassword)
    {
        // Arrange
        var email = "test@example.com";

        // Act & Assert
        var exception = await Assert.ThrowsAsync<ValidationException>(() =>
            _service.CreateUserAsync(email, weakPassword));
        Assert.Equal("WEAK_PASSWORD", exception.ReasonCode);
    }

    [Fact]
    public async Task AuthenticateAsync_ValidCredentials_ReturnsUser()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";
        var passwordHash = "$2a$12$hashed.password.here";
        var user = new User(email, passwordHash, true) { Id = Guid.NewGuid() };

        _repositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.VerifyPassword(password, passwordHash))
            .Returns(true);

        var updatedUser = user.WithLastLoginAt(DateTime.UtcNow);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _service.AuthenticateAsync(email, password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(user.Id, result.Id);
        _passwordHasherMock.Verify(h => h.VerifyPassword(password, passwordHash), Times.Once);
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<User>(u => u.FailedLoginAttempts == 0 && u.LastLoginAt.HasValue), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_InvalidPassword_ReturnsNull()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";
        var wrongPassword = "WrongPassword123";
        var passwordHash = "$2a$12$hashed.password.here";
        var user = new User(email, passwordHash, true) { Id = Guid.NewGuid() };

        _repositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.VerifyPassword(wrongPassword, passwordHash))
            .Returns(false);

        var updatedUser = user.WithFailedLoginAttempts(user.FailedLoginAttempts + 1);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _service.AuthenticateAsync(email, wrongPassword);

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<User>(u => u.FailedLoginAttempts > 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_UserNotFound_ReturnsNull()
    {
        // Arrange
        var email = "nonexistent@example.com";
        var wrongPassword = "Password123";

        _repositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act
        var result = await _service.AuthenticateAsync(email, wrongPassword);

        // Assert
        Assert.Null(result);
        // Should still verify password with dummy hash (timing-safe, no enumeration)
        _passwordHasherMock.Verify(h => h.VerifyPassword(It.IsAny<string>(), It.IsAny<string>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_DisabledUser_ReturnsNull()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";
        var passwordHash = "$2a$12$hashed.password.here";
        var user = new User(email, passwordHash, false) { Id = Guid.NewGuid() }; // IsEnabled = false

        _repositoryMock.Setup(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _passwordHasherMock.Setup(h => h.VerifyPassword(password, passwordHash))
            .Returns(true);

        var updatedUser = user.WithFailedLoginAttempts(user.FailedLoginAttempts + 1);
        _repositoryMock.Setup(r => r.UpdateAsync(It.IsAny<User>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(updatedUser);

        // Act
        var result = await _service.AuthenticateAsync(email, password);

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.UpdateAsync(It.Is<User>(u => u.FailedLoginAttempts > 0), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task AuthenticateAsync_EmptyCredentials_ReturnsNull()
    {
        // Arrange
        var email = "";
        var password = "";

        // Act
        var result = await _service.AuthenticateAsync(email, password);

        // Assert
        Assert.Null(result);
        _repositoryMock.Verify(r => r.FindByEmailAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}

