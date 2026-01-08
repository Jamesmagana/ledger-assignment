using System.Security.Claims;
using Ledger.Domain.Entities;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Data.Interceptors;
using Ledger.Infrastructure.Data.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
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

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new LedgerDbContext(options);
        var account = new Account("Test Account", AccountType.Asset, true);

        // Act
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Assert
        var auditLogs = await context.AuditLogs
            .Where(a => a.EntityName == "Account" && a.EntityId == account.Id)
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs[0];
        Assert.Equal("CREATE", auditLog.Action);
        Assert.Equal(userId, auditLog.PerformedBy);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.Null(auditLog.OldValues);
        Assert.NotNull(auditLog.NewValues);
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

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new LedgerDbContext(options);
        var account = new Account("Original Name", AccountType.Asset, true);
        context.Accounts.Add(account);
        await context.SaveChangesAsync();

        // Clear change tracker to simulate a new context load
        context.ChangeTracker.Clear();

        // Reload and modify the account
        var accountToUpdate = await context.Accounts.FindAsync(account.Id);
        Assert.NotNull(accountToUpdate);
        
        // Create updated account instance
        var updatedAccount = accountToUpdate.WithIsActive(false);
        
        // Detach the original and attach the new one
        context.Entry(accountToUpdate).State = EntityState.Detached;
        context.Entry(updatedAccount).State = EntityState.Modified;

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditLogs = await context.AuditLogs
            .Where(a => a.EntityName == "Account" && a.EntityId == updatedAccount.Id && a.Action == "UPDATE")
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs[0];
        Assert.Equal("UPDATE", auditLog.Action);
        Assert.Equal(userId, auditLog.PerformedBy);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.NotNull(auditLog.NewValues);
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

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService.Object);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new LedgerDbContext(options);
        var user = new User("test@example.com", "$2a$12$hashed.password", true);
        context.Users.Add(user);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditLogs = await context.AuditLogs
            .Where(a => a.EntityName == "User" && a.EntityId == user.Id)
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs[0];
        var newValuesJson = auditLog.NewValues ?? string.Empty;
        Assert.DoesNotContain("PasswordHash", newValuesJson, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("passwordHash", newValuesJson, StringComparison.OrdinalIgnoreCase);
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

        var interceptor = new AuditLoggingInterceptor(
            httpContextAccessor.Object,
            userContextService.Object);

        var options = new DbContextOptionsBuilder<LedgerDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .AddInterceptors(interceptor)
            .Options;

        using var context = new LedgerDbContext(options);
        var account = new Account("Test Account", AccountType.Asset, true);
        context.Accounts.Add(account);

        // Act
        await context.SaveChangesAsync();

        // Assert
        var auditLogs = await context.AuditLogs
            .Where(a => a.EntityName == "Account" && a.EntityId == account.Id)
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs[0];
        // CorrelationId should be generated (non-empty GUID string)
        Assert.NotNull(auditLog.CorrelationId);
        Assert.True(Guid.TryParse(auditLog.CorrelationId, out _));
    }
}

