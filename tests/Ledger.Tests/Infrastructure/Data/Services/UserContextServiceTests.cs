using System.Security.Claims;
using Ledger.Infrastructure.Data.Services;
using Microsoft.AspNetCore.Http;
using Moq;
using Xunit;

namespace Ledger.Tests.Infrastructure.Data.Services;

public class UserContextServiceTests
{
    [Fact]
    public void GetCurrentUserId_WithValidJwtClaim_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new UserContextService(httpContextAccessor.Object);

        // Act
        var result = service.GetCurrentUserId();

        // Assert
        Assert.Equal(userId, result);
    }

    [Fact]
    public void GetCurrentUserId_WithSubClaim_ReturnsUserId()
    {
        // Arrange
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        var claims = new[] { new Claim("sub", userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new UserContextService(httpContextAccessor.Object);

        // Act
        var result = service.GetCurrentUserId();

        // Assert
        Assert.Equal(userId, result);
    }

    [Fact]
    public void GetCurrentUserId_NoHttpContext_ReturnsNull()
    {
        // Arrange
        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns((HttpContext?)null);

        var service = new UserContextService(httpContextAccessor.Object);

        // Act
        var result = service.GetCurrentUserId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentUserId_NoUser_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        httpContext.User = new ClaimsPrincipal();

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new UserContextService(httpContextAccessor.Object);

        // Act
        var result = service.GetCurrentUserId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentUserId_InvalidGuidClaim_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "invalid-guid") };
        var identity = new ClaimsIdentity(claims, "Test");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new UserContextService(httpContextAccessor.Object);

        // Act
        var result = service.GetCurrentUserId();

        // Assert
        Assert.Null(result);
    }

    [Fact]
    public void GetCurrentUserId_EmptyClaim_ReturnsNull()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, "") };
        var identity = new ClaimsIdentity(claims, "Test");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var service = new UserContextService(httpContextAccessor.Object);

        // Act
        var result = service.GetCurrentUserId();

        // Assert
        Assert.Null(result);
    }
}

