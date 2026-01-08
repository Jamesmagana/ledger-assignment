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
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Tests.Integration.Api;

/// <summary>
/// Integration tests for JWT authentication.
/// </summary>
public class AuthenticationTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"AuthenticationTest_{Guid.NewGuid()}";
    private string? _validToken;
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
                    services.AddDbContext<LedgerDbContext>(options =>
                    {
                        options.UseInMemoryDatabase(databaseName: _databaseName)
                            .ConfigureWarnings(w => w.Ignore(Microsoft.EntityFrameworkCore.Diagnostics.InMemoryEventId.TransactionIgnoredWarning));
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

        // Generate a valid token for tests (but don't set it globally - let each test decide)
        _validToken = TestJwtTokenHelper.GenerateToken(
            _testIssuer,
            _testAudience,
            _testSecretKey);
        
        // Only set token for tests that need it - tests checking 401 should clear it
        // _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _validToken);
    }

    [Fact]
    public async Task GetAccounts_WithoutToken_Returns401Unauthorized()
    {
        // Arrange - Remove authorization header
        _client!.DefaultRequestHeaders.Remove("Authorization");

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(401, problemDetails.Status);
        Assert.Equal("Unauthorized", problemDetails.Title);
        Assert.Equal("UNAUTHORIZED", problemDetails.Extensions?["reasonCode"]?.ToString());
        Assert.NotNull(problemDetails.Extensions?["correlationId"]?.ToString());
        
    }

    [Fact]
    public async Task GetAccounts_WithValidToken_Returns200Ok()
    {
        // Arrange - Create a user and get a real token (not just a generated one)
        var email = "validtoken2@example.com";
        var password = "Password123";
        var createUserResponse = await _client!.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        
        // Clean up - remove token
        _client.DefaultRequestHeaders.Remove("Authorization");
    }

    [Fact]
    public async Task GetAccounts_WithExpiredToken_Returns401Unauthorized()
    {
        // Arrange
        var expiredToken = TestJwtTokenHelper.GenerateExpiredToken(
            _testIssuer!,
            _testAudience!,
            _testSecretKey!);

        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", expiredToken);

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(401, problemDetails.Status);
        Assert.Equal("UNAUTHORIZED", problemDetails.Extensions?["reasonCode"]?.ToString());
        
        // Clean up - remove token
        _client.DefaultRequestHeaders.Remove("Authorization");
    }

    [Fact]
    public async Task GetAccounts_WithInvalidToken_Returns401Unauthorized()
    {
        // Arrange
        var invalidToken = "invalid.token.here";
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalidToken);

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(401, problemDetails.Status);
        Assert.Equal("UNAUTHORIZED", problemDetails.Extensions?["reasonCode"]?.ToString());
        
        // Clean up - remove token
        _client.DefaultRequestHeaders.Remove("Authorization");
    }

    [Fact]
    public async Task GetAccounts_WithTokenInvalidSignature_Returns401Unauthorized()
    {
        // Arrange
        var wrongSecretKey = "WrongSecretKey_ForTesting_InvalidSignature_32Chars";
        var invalidToken = TestJwtTokenHelper.GenerateTokenWithInvalidSignature(
            _testIssuer!,
            _testAudience!,
            _testSecretKey!,
            wrongSecretKey);

        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", invalidToken);

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(401, problemDetails.Status);
        Assert.Equal("UNAUTHORIZED", problemDetails.Extensions?["reasonCode"]?.ToString());
        
        // Clean up - remove token
        _client.DefaultRequestHeaders.Remove("Authorization");
    }

    [Fact]
    public async Task PostAccount_WithoutToken_Returns401Unauthorized()
    {
        // Arrange - Remove authorization header
        _client!.DefaultRequestHeaders.Remove("Authorization");
        var request = new CreateAccountRequest("Test Account", AccountType.Asset, true);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
    }

    [Fact]
    public async Task PostAccount_WithValidToken_Returns201Created()
    {
        // Arrange - Create a user and get a real token (not just a generated one)
        var email = "validtoken@example.com";
        var password = "Password123";
        var createUserResponse = await _client!.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);
        var request = new CreateAccountRequest("Test Account", AccountType.Asset, true);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        
        // Clean up - remove token
        _client.DefaultRequestHeaders.Remove("Authorization");
    }

    [Fact]
    public async Task PostJournalEntry_WithoutToken_Returns401Unauthorized()
    {
        // Arrange - Remove authorization header
        _client!.DefaultRequestHeaders.Remove("Authorization");
        var request = new CreateJournalEntryRequest(
            ExternalId: "test-123",
            Lines: new List<CreateJournalEntryLineRequest>
            {
                new(Guid.NewGuid(), LineDirection.Debit, 100.00m),
                new(Guid.NewGuid(), LineDirection.Credit, 100.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTrialBalance_WithoutToken_Returns401Unauthorized()
    {
        // Arrange - Remove authorization header
        _client!.DefaultRequestHeaders.Remove("Authorization");

        // Act
        var response = await _client.GetAsync("/api/reports/trial-balance");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        
    }

    [Fact]
    public async Task GetHealthCheck_WithoutToken_Returns200Ok()
    {
        // Act - Health check should be public (no authentication required)
        var response = await _client!.GetAsync("/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task GetAccounts_401Response_IncludesCorrelationId()
    {
        // Arrange - Remove authorization header
        _client!.DefaultRequestHeaders.Remove("Authorization");
        var correlationId = Guid.NewGuid().ToString();
        _client.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(correlationId, problemDetails.Extensions?["correlationId"]?.ToString());
        
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

