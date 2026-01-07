using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.Api.DTOs.Accounts;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration.Api;

/// <summary>
/// Integration tests for JWT authentication.
/// </summary>
public class AuthenticationTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private string? _validToken;
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

        // Generate a valid token for tests
        _validToken = TestJwtTokenHelper.GenerateToken(
            _testIssuer,
            _testAudience,
            _testSecretKey);
    }

    [Fact]
    public async Task GetAccounts_WithoutToken_Returns401Unauthorized()
    {
        // Arrange - no token in request

        // Act
        var response = await _client!.GetAsync("/api/accounts");

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
        // Arrange
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _validToken);

        // Act
        var response = await _client.GetAsync("/api/accounts");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
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
    }

    [Fact]
    public async Task PostAccount_WithoutToken_Returns401Unauthorized()
    {
        // Arrange
        var request = new CreateAccountRequest("Test Account", AccountType.Asset, true);

        // Act
        var response = await _client!.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task PostAccount_WithValidToken_Returns201Created()
    {
        // Arrange
        _client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _validToken);
        var request = new CreateAccountRequest("Test Account", AccountType.Asset, true);

        // Act
        var response = await _client.PostAsJsonAsync("/api/accounts", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task PostJournalEntry_WithoutToken_Returns401Unauthorized()
    {
        // Arrange
        var request = new
        {
            ExternalId = "test-123",
            Lines = new[]
            {
                new { AccountId = Guid.NewGuid(), Direction = 1, Amount = 100.00m },
                new { AccountId = Guid.NewGuid(), Direction = 2, Amount = 100.00m }
            }
        };

        // Act
        var response = await _client!.PostAsJsonAsync("/api/journal-entries", request);

        // Assert
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetTrialBalance_WithoutToken_Returns401Unauthorized()
    {
        // Act
        var response = await _client!.GetAsync("/api/reports/trial-balance");

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
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _client!.DefaultRequestHeaders.Add("X-Correlation-Id", correlationId);

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

