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
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Xunit;

namespace Ledger.Tests.Integration.Api;

/// <summary>
/// Tests to verify CorrelationId propagation in all responses and audit logs.
/// </summary>
public class CorrelationIdPropagationTests : IAsyncLifetime
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
    public async Task CorrelationId_IncludedInSuccessResponseHeader()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Act
        var request = new CreateAccountRequest("Correlation Test", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var responseCorrelationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.Equal(correlationId, responseCorrelationId);
    }

    [Fact]
    public async Task CorrelationId_IncludedInErrorResponse()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation2@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create first account
        await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Duplicate", AccountType.Asset, true));

        // Act - Try to create duplicate
        var duplicateRequest = new CreateAccountRequest("Duplicate", AccountType.Liability, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", duplicateRequest);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(correlationId, problemDetails.GetProperty("correlationId").GetString());
        Assert.NotNull(problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task CorrelationId_GeneratedIfNotProvided()
    {
        // Arrange - Don't provide correlation ID
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");

        // Create user and login
        var email = "correlation3@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Act
        var request = new CreateAccountRequest("Auto Correlation", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.True(response.Headers.Contains("X-Correlation-Id"));
        var generatedCorrelationId = response.Headers.GetValues("X-Correlation-Id").FirstOrDefault();
        Assert.NotNull(generatedCorrelationId);
        Assert.True(Guid.TryParse(generatedCorrelationId, out _)); // Should be valid GUID
    }

    [Fact]
    public async Task CorrelationId_CapturedInAuditLog()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation4@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Act - Create account
        var request = new CreateAccountRequest("Audit Correlation", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>();
        Assert.NotNull(account);

        // Assert - Check audit log
        var auditLog = await _context!.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);

        Assert.NotNull(auditLog);
        Assert.Equal(correlationId, auditLog.CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_InValidationErrorResponse()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation5@example.com";
        var password = "Password123";
        await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        var loginResult = await loginResponse.Content.ReadFromJsonAsync<LoginResponse>();
        Assert.NotNull(loginResult);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Act - Send invalid request (empty name)
        var invalidRequest = new CreateAccountRequest("", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", invalidRequest);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(correlationId, problemDetails.GetProperty("correlationId").GetString());
        Assert.Equal("VALIDATION_ERROR", problemDetails.GetProperty("reasonCode").GetString());
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

