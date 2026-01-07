using Ledger.Application.Exceptions;
using Ledger.Application.Services;
using Ledger.Domain.Entities;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Ledger.Tests.Application.Services;

public class AuthenticationServiceTests
{
    private readonly Mock<IUserService> _userServiceMock;
    private readonly Mock<IJwtTokenService> _jwtTokenServiceMock;
    private readonly Mock<IAuditLogService> _auditLogServiceMock;
    private readonly Mock<IHttpContextAccessor> _httpContextAccessorMock;
    private readonly AuthenticationService _service;

    public AuthenticationServiceTests()
    {
        _userServiceMock = new Mock<IUserService>();
        _jwtTokenServiceMock = new Mock<IJwtTokenService>();
        _auditLogServiceMock = new Mock<IAuditLogService>();
        _httpContextAccessorMock = new Mock<IHttpContextAccessor>();
        
        // Setup HttpContextAccessor to return a mock HttpContext with CorrelationId
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = Guid.NewGuid().ToString();
        _httpContextAccessorMock.Setup(x => x.HttpContext).Returns(httpContext);
        
        _service = new AuthenticationService(
            _userServiceMock.Object,
            _jwtTokenServiceMock.Object,
            _auditLogServiceMock.Object,
            _httpContextAccessorMock.Object);
    }

    [Fact]
    public async Task LoginAsync_ValidCredentials_ReturnsLoginResult()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";
        var userId = Guid.NewGuid();
        var user = new User(email, "hash", true) { Id = userId };
        var token = "test-jwt-token";

        _userServiceMock.Setup(s => s.AuthenticateAsync(email, password, It.IsAny<CancellationToken>()))
            .ReturnsAsync(user);
        _jwtTokenServiceMock.Setup(j => j.GenerateToken(userId, email))
            .Returns(token);

        // Act
        var result = await _service.LoginAsync(email, password);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(token, result.Token);
        Assert.Equal(userId, result.UserId);
        Assert.Equal(email, result.Email);
        _jwtTokenServiceMock.Verify(j => j.GenerateToken(userId, email), Times.Once);
        _auditLogServiceMock.Verify(a => a.LogEntityChangeAsync(
            "User",
            userId,
            "LOGIN",
            null,
            It.IsAny<object>(),
            userId,
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task LoginAsync_InvalidCredentials_ThrowsUnauthorizedException()
    {
        // Arrange
        var email = "test@example.com";
        var password = "WrongPassword123";

        _userServiceMock.Setup(s => s.AuthenticateAsync(email, password, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null);

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(email, password));
        Assert.Equal("INVALID_CREDENTIALS", exception.ReasonCode);
        Assert.Contains("Invalid email or password", exception.Message);
        _jwtTokenServiceMock.Verify(j => j.GenerateToken(It.IsAny<Guid>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task LoginAsync_DisabledUser_ThrowsUnauthorizedException()
    {
        // Arrange
        var email = "test@example.com";
        var password = "Password123";

        _userServiceMock.Setup(s => s.AuthenticateAsync(email, password, It.IsAny<CancellationToken>()))
            .ReturnsAsync((User?)null); // Disabled user returns null

        // Act & Assert
        var exception = await Assert.ThrowsAsync<UnauthorizedException>(() =>
            _service.LoginAsync(email, password));
        Assert.Equal("INVALID_CREDENTIALS", exception.ReasonCode);
    }
}

