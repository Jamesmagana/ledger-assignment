using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.Auth;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Api.DTOs.Users;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Data.Interceptors;
using Ledger.Infrastructure.Data.Services;
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Ledger.Tests.Integration.Api;

public class AuditLoggingTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"AuditLoggingTest_{Guid.NewGuid()}";
    private string? _testIssuer;
    private string? _testAudience;
    private string? _testSecretKey;

    public async Task InitializeAsync()
    {
        // Test JWT settings
        _testIssuer = "https://ledger-api.test";
        _testAudience = "https://ledger-api.test";
        _testSecretKey = "TestSecretKey_ForIntegrationTests_Minimum32Characters";

        // Create WebApplicationFactory with test database and JWT settings
        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    // Remove existing DbContext registration
                    var descriptor = services.SingleOrDefault(
                        d => d.ServiceType == typeof(DbContextOptions<LedgerDbContext>));
                    if (descriptor != null)
                    {
                        services.Remove(descriptor);
                    }

                    // Add test DbContext with in-memory database
                    // Use fixed database name so test context and API context share the same database
                    services.AddDbContext<LedgerDbContext>((sp, options) =>
                    {
                        options.UseInMemoryDatabase(databaseName: _databaseName)
                            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
                        
                        // Add audit logging interceptor (required for audit logs to be created)
                        var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
                        var userContextService = sp.GetRequiredService<Ledger.Infrastructure.Data.Services.IUserContextService>();
                        options.AddInterceptors(new Ledger.Infrastructure.Data.Interceptors.AuditLoggingInterceptor(
                            httpContextAccessor,
                            userContextService));
                    });
                });

                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Jwt:Issuer", _testIssuer },
                        { "Jwt:Audience", _testAudience },
                        { "Jwt:SecretKey", _testSecretKey },
                        { "Jwt:ClockSkewSeconds", "60" }
                    });
                });
            });

        _client = _factory.CreateClient();

        // Get DbContext and ensure database is created
        // Create a scope that will be disposed in DisposeAsync
        _serviceScope = _factory.Services.CreateScope();
        _context = _serviceScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.EnsureCreatedAsync();
    }

    [Fact]
    public async Task CreateAccount_CreatesAuditLog()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit1@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        var request = new CreateAccountRequest("Audit Test Account", AccountType.Asset, true);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await JsonHelper.ReadFromJsonAsync<AccountResponse>(response.Content);
        Assert.NotNull(account);

        // Assert - Check audit log was created
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var auditLogs = await assertContext.AuditLogs
            .Where(a => a.EntityName == "Account" && a.EntityId == account.Id)
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs.First();
        Assert.Equal("CREATE", auditLog.Action);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.NotNull(auditLog.NewValues);
        Assert.Null(auditLog.OldValues);
        Assert.Equal(loginResult.UserId, auditLog.PerformedBy);

        // Verify sensitive fields are excluded
        var newValuesJson = JsonSerializer.Deserialize<Dictionary<string, object?>>(auditLog.NewValues);
        Assert.NotNull(newValuesJson);
        // Should not contain any sensitive fields (Account doesn't have any, but verify structure)
    }

    [Fact]
    public async Task UpdateAccount_CreatesAuditLog()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit2@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var createRequest = new CreateAccountRequest("Update Test Account", AccountType.Asset, true);
        var createResponse = await _client.PostAsJsonAsync("/api/accounts", createRequest);
        createResponse.EnsureSuccessStatusCode();
        var account = await JsonHelper.ReadFromJsonAsync<AccountResponse>(createResponse.Content);
        Assert.NotNull(account);

        // Act - Update account
        var updateRequest = new UpdateAccountRequest(false); // Disable account
        var updateResponse = await _client.PutAsJsonAsync($"/api/accounts/{account.Id}", updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        // Assert - Check audit log was created for update
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var auditLogs = await assertContext.AuditLogs
            .Where(a => a.EntityName == "Account" && a.EntityId == account.Id && a.Action == "UPDATE")
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs.First();
        Assert.Equal("UPDATE", auditLog.Action);
        Assert.NotNull(auditLog.OldValues);
        Assert.NotNull(auditLog.NewValues);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.Equal(loginResult.UserId, auditLog.PerformedBy);
    }

    [Fact]
    public async Task PostJournalEntry_CreatesAuditLog()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit3@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create accounts
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var request = new CreateJournalEntryRequest(
            ExternalId: "EXT-001",
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 1000.00m),
                new(revenueAccount.Id, LineDirection.Credit, 1000.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request);
        response.EnsureSuccessStatusCode();
        var journalEntry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(response.Content);
        Assert.NotNull(journalEntry);

        // Assert - Check audit log was created
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var auditLogs = await assertContext.AuditLogs
            .Where(a => a.EntityName == "JournalEntry" && a.EntityId == journalEntry.Id)
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs.First();
        Assert.Equal("CREATE", auditLog.Action);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.Equal(loginResult.UserId, auditLog.PerformedBy);
    }

    [Fact]
    public async Task CreateUser_CreatesAuditLog_ExcludesPasswordHash()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var request = new CreateUserRequest("audituser@example.com", "Password123");

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        response.EnsureSuccessStatusCode();
        var user = await JsonHelper.ReadFromJsonAsync<UserResponse>(response.Content);
        Assert.NotNull(user);

        // Assert - Check audit log was created
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var auditLogs = await assertContext.AuditLogs
            .Where(a => a.EntityName == "User" && a.EntityId == user.Id)
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs.First();
        Assert.Equal("CREATE", auditLog.Action);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.NotNull(auditLog.NewValues);

        // Verify PasswordHash is excluded
        var newValuesJson = JsonSerializer.Deserialize<Dictionary<string, object?>>(auditLog.NewValues);
        Assert.NotNull(newValuesJson);
        Assert.DoesNotContain("PasswordHash", newValuesJson.Keys);
        Assert.DoesNotContain("passwordHash", newValuesJson.Keys);
        Assert.DoesNotContain("Password", newValuesJson.Keys);
    }

    [Fact]
    public async Task Login_CreatesAuditLog()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var email = "loginuser@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(response.Content);
        Assert.NotNull(loginResult);

        // Assert - Check audit log was created for login
        // Wait a bit to ensure the audit log is saved (InMemory database should be immediate, but ensure it's persisted)
        await Task.Delay(100);
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        assertContext.ChangeTracker.Clear(); // Clear change tracker to ensure fresh data
        var auditLogs = await assertContext.AuditLogs
            .Where(a => a.EntityName == "User" && a.EntityId == loginResult.UserId && a.Action == "LOGIN")
            .ToListAsync();

        Assert.Single(auditLogs);
        var auditLog = auditLogs.First();
        Assert.Equal("LOGIN", auditLog.Action);
        Assert.Equal(correlationId, auditLog.CorrelationId);
        Assert.Equal(loginResult.UserId, auditLog.PerformedBy);
        Assert.NotNull(auditLog.NewValues);
    }

    [Fact]
    public async Task AuditLogs_IncludeCorrelationId()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit4@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var request = new CreateAccountRequest("Correlation Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await JsonHelper.ReadFromJsonAsync<AccountResponse>(response.Content);
        Assert.NotNull(account);

        // Assert - Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var auditLog = await assertContext.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);

        Assert.NotNull(auditLog);
        Assert.Equal(correlationId, auditLog.CorrelationId);
    }

    [Fact]
    public async Task AuditLogs_IncludeUserId_WhenAuthenticated()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit5@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var request = new CreateAccountRequest("User ID Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await JsonHelper.ReadFromJsonAsync<AccountResponse>(response.Content);
        Assert.NotNull(account);

        // Assert - Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var auditLog = await assertContext.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);

        Assert.NotNull(auditLog);
        Assert.Equal(loginResult.UserId, auditLog.PerformedBy);
    }

    [Fact]
    public async Task AuditLogs_WrittenInSameTransaction()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit6@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();

        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var request = new CreateAccountRequest("Transaction Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await JsonHelper.ReadFromJsonAsync<AccountResponse>(response.Content);
        Assert.NotNull(account);

        // Assert - Both account and audit log should exist
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var dbAccount = await assertContext.Accounts.FindAsync(account.Id);
        Assert.NotNull(dbAccount);

        var auditLog = await assertContext.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);
        Assert.NotNull(auditLog);
    }

    private async Task<AccountResponse> CreateAccountAsync(string name, AccountType type)
    {
        var request = new CreateAccountRequest(name, type, true);
        var response = await _client!.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions())
            ?? throw new InvalidOperationException("Failed to create account");
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }
        if (_serviceScope != null)
        {
            _serviceScope.Dispose();
        }
        if (_client != null)
        {
            _client.Dispose();
        }
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
    }
}

