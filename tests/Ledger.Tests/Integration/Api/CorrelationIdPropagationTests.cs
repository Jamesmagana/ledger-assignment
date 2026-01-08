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

/// <summary>
/// Tests to verify CorrelationId propagation in all responses and audit logs.
/// </summary>
public class CorrelationIdPropagationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"CorrelationIdTest_{Guid.NewGuid()}";
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
                        var userContextService = sp.GetRequiredService<IUserContextService>();
                        options.AddInterceptors(new AuditLoggingInterceptor(
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
    public async Task CorrelationId_IncludedInSuccessResponseHeader()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
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
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation2@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
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
        // Arrange - Clear headers and don't provide correlation ID
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");

        // Create user and login
        var email = "correlation3@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
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
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation4@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Act - Create account
        var request = new CreateAccountRequest("Audit Correlation", AccountType.Asset, true);
        var response = await _client.PostAsJsonAsync("/api/accounts", request);
        response.EnsureSuccessStatusCode();
        var account = await response.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        Assert.NotNull(account);

        // Assert - Check audit log
        // Create a fresh context to ensure we see the latest changes
        using var assertScope = _factory!.Services.CreateScope();
        var assertContext = assertScope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        
        // Wait a bit to ensure the audit log is saved (InMemory database is synchronous but ensure it's persisted)
        await Task.Delay(100);
        assertContext.ChangeTracker.Clear(); // Clear change tracker to ensure fresh data
        
        // Query all audit logs to see what's there
        var allAuditLogs = await assertContext.AuditLogs.ToListAsync();
        var auditLog = await assertContext.AuditLogs
            .FirstOrDefaultAsync(a => a.EntityName == "Account" && a.EntityId == account.Id);

        Assert.NotNull(auditLog);
        Assert.Equal(correlationId, auditLog.CorrelationId);
    }

    [Fact]
    public async Task CorrelationId_InValidationErrorResponse()
    {
        // Arrange - Clear headers first
        _client!.DefaultRequestHeaders.Remove("X-Correlation-Id");
        _client.DefaultRequestHeaders.Remove("Authorization");
        
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Create user and login
        var email = "correlation5@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
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

