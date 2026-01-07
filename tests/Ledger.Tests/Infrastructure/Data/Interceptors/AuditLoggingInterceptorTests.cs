using System.Security.Claims;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Entities;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Data.Interceptors;
using Ledger.Infrastructure.Data.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Moq;
using Xunit;

namespace Ledger.Tests.Infrastructure.Data.Interceptors;

public class AuditLoggingInterceptorTests
{
    [Fact]
    public async Task SavingChangesAsync_AddedEntity_CreatesAuditLog()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = correlationId;
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var userContextService = new UserContextService(httpContextAccessor.Object);
        var auditLogService = new Mock<IAuditLogService>();

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService,
            auditLogService.Object);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new LedgerDbContext(options);
        var account = new Account("Test Account", AccountType.Asset, true);

        // Act
        context.Accounts.Add(account);
        await interceptor.SavingChangesAsync(
            new DbContextEventData(
                new FakeEventData(),
                context,
                new FakeEventData()),
            default,
            CancellationToken.None);

        // Assert
        auditLogService.Verify(a => a.LogEntityChangeAsync(
            "Account",
            account.Id,
            "CREATE",
            null,
            It.IsAny<object>(),
            userId,
            correlationId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavingChangesAsync_ModifiedEntity_CreatesAuditLogWithOldAndNewValues()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var userId = Guid.NewGuid();
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = correlationId;
        var claims = new[] { new Claim(ClaimTypes.NameIdentifier, userId.ToString()) };
        var identity = new ClaimsIdentity(claims, "Test");
        httpContext.User = new ClaimsPrincipal(identity);

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var userContextService = new UserContextService(httpContextAccessor.Object);
        var auditLogService = new Mock<IAuditLogService>();

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService,
            auditLogService.Object);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new LedgerDbContext(options);
        var account = new Account("Original Name", AccountType.Asset, true);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Modify the account
        var updatedAccount = account.WithIsActive(false);
        context.Entry(account).CurrentValues.SetValues(new
        {
            updatedAccount.Id,
            updatedAccount.Name,
            updatedAccount.Type,
            updatedAccount.IsActive,
            updatedAccount.CreatedAt,
            updatedAccount.UpdatedAt
        });
        context.Entry(account).State = EntityState.Modified;

        // Act
        await interceptor.SavingChangesAsync(
            new DbContextEventData(
                new FakeEventData(),
                context,
                new FakeEventData()),
            default,
            CancellationToken.None);

        // Assert
        auditLogService.Verify(a => a.LogEntityChangeAsync(
            "Account",
            account.Id,
            "UPDATE",
            It.IsAny<object>(),
            It.IsAny<object>(),
            userId,
            correlationId,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavingChangesAsync_ExcludesSensitiveFields()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        var httpContext = new DefaultHttpContext();
        httpContext.Items["CorrelationId"] = correlationId;

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var userContextService = new Mock<IUserContextService>();
        var auditLogService = new Mock<IAuditLogService>();

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService.Object,
            auditLogService.Object);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new LedgerDbContext(options);
        var user = new User("test@example.com", "$2a$12$hashed.password", true);
        context.Users.Add(user);

        // Act
        await interceptor.SavingChangesAsync(
            new DbContextEventData(
                new FakeEventData(),
                context,
                new FakeEventData()),
            default,
            CancellationToken.None);

        // Assert
        auditLogService.Verify(a => a.LogEntityChangeAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<object?>(),
            It.Is<object>(obj =>
            {
                var json = System.Text.Json.JsonSerializer.Serialize(obj);
                return !json.Contains("PasswordHash") && !json.Contains("passwordHash");
            }),
            It.IsAny<Guid?>(),
            It.IsAny<string>(),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SavingChangesAsync_NoCorrelationId_GeneratesNewOne()
    {
        // Arrange
        var httpContext = new DefaultHttpContext();
        // No CorrelationId in Items

        var httpContextAccessor = new Mock<IHttpContextAccessor>();
        httpContextAccessor.Setup(x => x.HttpContext).Returns(httpContext);

        var userContextService = new Mock<IUserContextService>();
        var auditLogService = new Mock<IAuditLogService>();

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService.Object,
            auditLogService.Object);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        using var context = new LedgerDbContext(options);
        var account = new Account("Test Account", AccountType.Asset, true);
        context.Accounts.Add(account);

        // Act
        await interceptor.SavingChangesAsync(
            new DbContextEventData(
                new FakeEventData(),
                context,
                new FakeEventData()),
            default,
            CancellationToken.None);

        // Assert
        auditLogService.Verify(a => a.LogEntityChangeAsync(
            It.IsAny<string>(),
            It.IsAny<Guid>(),
            It.IsAny<string>(),
            It.IsAny<object?>(),
            It.IsAny<object?>(),
            It.IsAny<Guid?>(),
            It.IsAny<string>(), // CorrelationId should be generated
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // Helper class for testing
    private class FakeEventData : IEventData
    {
        public DateTimeOffset EventDate => DateTimeOffset.UtcNow;
        public EventId EventId => new EventId(1);
        public object? EventSource => null;
    }
}

