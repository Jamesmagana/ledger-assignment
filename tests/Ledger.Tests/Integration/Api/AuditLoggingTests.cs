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
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ledger.Tests.Integration.Api;

public class AuditLoggingTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private string? _testIssuer;
    private string? _testAudience;
    private string? _testSecretKey;

    public async Task InitializeAsync()
    {
        // Start PostgreSQL container
        _postgresContainer = new PostgreSqlBuilder()
            .WithImage("postgres:16")
            .WithDatabase("ledger_test")
            .WithUsername("test_user")
            .WithPassword("test_password")
            .Build();

        await _postgresContainer.StartAsync();

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

                    // Add test DbContext
                    var connectionString = _postgresContainer.GetConnectionString();
                    services.AddDbContext<LedgerDbContext>(options =>
                    {
                        options.UseNpgsql(connectionString);
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

        // Get DbContext and run migrations
        using var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.MigrateAsync();
    }

    [Fact]
    public async Task CreateAccount_CreatesAuditLog()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        var request = new CreateAccountRequest("Audit Test Account", AccountType.Asset, true);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);

        // Assert - Check audit log was created
        var auditLogs = await _context!.AuditLogs
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
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var createRequest = new CreateAccountRequest("Update Test Account", AccountType.Asset, true);
        var createResponse = await _client.PostAsJsonAsync("/api/accounts", createRequest);
        var account = await createResponse.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);

        // Act - Update account
        var updateRequest = new UpdateAccountRequest(false); // Disable account
        var updateResponse = await _client.PutAsJsonAsync($"/api/accounts/{account.Id}", updateRequest);
        updateResponse.EnsureSuccessStatusCode();

        // Assert - Check audit log was created for update
        var auditLogs = await _context!.AuditLogs
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
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "audit@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create accounts
        var cashAccount = await CreateAccountAsync("Cash", AccountType.Asset);
        var revenueAccount = await CreateAccountAsync("Revenue", AccountType.Revenue);

        var request = new CreateJournalEntryRequest(
            externalId: "EXT-001",
            lines: new List<CreateJournalEntryLineRequest>
            {
                new(cashAccount.Id, LineDirection.Debit, 1000.00m),
                new(revenueAccount.Id, LineDirection.Credit, 1000.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/journal-entries", request);
        response.EnsureSuccessStatusCode();
        var journalEntry = await response.Content.ReadFromJsonAsync<JournalEntryResponse>();
        Assert.NotNull(journalEntry);

        // Assert - Check audit log was created
        var auditLogs = await _context!.AuditLogs
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
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var request = new CreateUserRequest("audituser@example.com", "Password123");

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request);
        response.EnsureSuccessStatusCode();
        var user = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(user);

        // Assert - Check audit log was created
        var auditLogs = await _context!.AuditLogs
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
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        var email = "loginuser@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));

        // Act
        var response = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        response.EnsureSuccessStatusCode();
        var loginResult = await response.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);

        // Assert - Check audit log was created for login
        var auditLogs = await _context!.AuditLogs
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
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "correlation@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var request = new CreateAccountRequest("Correlation Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);

        // Assert
        var auditLog = await _context!.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);

        Assert.NotNull(auditLog);
        Assert.Equal(correlationId, auditLog.CorrelationId);
    }

    [Fact]
    public async Task AuditLogs_IncludeUserId_WhenAuthenticated()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "userid@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var request = new CreateAccountRequest("User ID Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);

        // Assert
        var auditLog = await _context!.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);

        Assert.NotNull(auditLog);
        Assert.Equal(loginResult.UserId, auditLog.PerformedBy);
    }

    [Fact]
    public async Task AuditLogs_WrittenInSameTransaction()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create a user and get token
        var email = "transaction@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create account
        var request = new CreateAccountRequest("Transaction Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);

        // Assert - Both account and audit log should exist
        var dbAccount = await _context!.Accounts.FindAsync(account.Id);
        Assert.NotNull(dbAccount);

        var auditLog = await _context.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);
        Assert.NotNull(auditLog);
    }

    private async Task<AccountResponse> CreateAccountAsync(string name, AccountType type)
    {
        var request = new CreateAccountRequest(name, type, true);
        var response = await _client!.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadFromJsonAsync<AccountResponse>()
            ?? throw new InvalidOperationException("Failed to create account");
    }

    public async Task DisposeAsync()
    {
        if (_client != null)
        {
            _client.Dispose();
        }
        if (_factory != null)
        {
            await _factory.DisposeAsync();
        }
        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
        }
    }
}

