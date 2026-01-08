using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.Auth;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Api.DTOs.Users;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Ledger.Tests.Helpers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.Memory;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Tests.Integration.Api;

public class JournalEntriesControllerTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"JournalEntriesTest_{Guid.NewGuid()}";
    private Guid? _cashAccountId;
    private Guid? _revenueAccountId;

    public async Task InitializeAsync()
    {
        // Create WebApplicationFactory with in-memory test database
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

                    // Register repositories and services
                    services.AddScoped<IAccountRepository, AccountRepository>();
                    services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
                    services.AddScoped<IAccountService, AccountService>();
                    services.AddScoped<IRequestHashService, RequestHashService>();
                    services.AddScoped<IJournalEntryService, JournalEntryService>();
                });

                builder.ConfigureAppConfiguration((context, config) =>
                {
                    config.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        { "Jwt:Issuer", "https://ledger-api.test" },
                        { "Jwt:Audience", "https://ledger-api.test" },
                        { "Jwt:SecretKey", "TestSecretKey_ForIntegrationTests_Minimum32Characters" },
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

        // Create a user and get token for authenticated requests
        var email = "journal@example.com";
        var password = "Password123";
        var createUserResponse = await _client.PostAsJsonAsync("/api/users", new CreateUserRequest(email, password));
        createUserResponse.EnsureSuccessStatusCode();
        
        var loginResponse = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(email, password));
        loginResponse.EnsureSuccessStatusCode();
        var loginResult = await JsonHelper.ReadFromJsonAsync<LoginResponse>(loginResponse.Content);
        Assert.NotNull(loginResult);
        Assert.NotNull(loginResult.Token);
        _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", loginResult.Token);

        // Create test accounts
        var cashResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        cashResponse.EnsureSuccessStatusCode();
        var cashAccount = await cashResponse.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        _cashAccountId = cashAccount?.Id;

        var revenueResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Revenue", AccountType.Revenue, true));
        revenueResponse.EnsureSuccessStatusCode();
        var revenueAccount = await revenueResponse.Content.ReadFromJsonAsync<AccountResponse>(JsonHelper.GetJsonOptions());
        _revenueAccountId = revenueAccount?.Id;
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

    [Fact]
    public async Task POST_JournalEntries_ValidEntry_Returns201Created()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var request = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(response.Content);
        Assert.NotNull(entry);
        Assert.Equal(2, entry.Lines.Count);
        Assert.False(entry.IdempotencyReplay);
    }

    [Fact]
    public async Task POST_JournalEntries_LessThan2Lines_Returns400BadRequest()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);

        var request = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task POST_JournalEntries_UnbalancedEntry_Returns400BadRequest()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var request = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 200.00m) // Unbalanced
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("UNBALANCED_ENTRY", problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task POST_JournalEntries_IdempotentReplay_Returns200OK()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var externalId = "ext-123";
        var request = new CreateJournalEntryRequest(
            externalId,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        // Act - First request
        var firstResponse = await _client.PostAsJsonAsync("/api/JournalEntries", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstEntry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(firstResponse.Content);
        Assert.NotNull(firstEntry);

        // Act - Second request (idempotent replay)
        var secondResponse = await _client.PostAsJsonAsync("/api/JournalEntries", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondEntry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(secondResponse.Content);
        Assert.NotNull(secondEntry);
        Assert.True(secondEntry.IdempotencyReplay);
        Assert.Equal(firstEntry.Id, secondEntry.Id);
    }

    [Fact]
    public async Task POST_JournalEntries_IdempotencyConflict_Returns409Conflict()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var externalId = "ext-456";
        var request1 = new CreateJournalEntryRequest(
            externalId,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        // First request
        await _client.PostAsJsonAsync("/api/JournalEntries", request1);

        // Second request with same ExternalId but different payload
        var request2 = new CreateJournalEntryRequest(
            externalId,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 200.00m), // Different amount
                new(_revenueAccountId.Value, LineDirection.Credit, 200.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("DUPLICATE_EXTERNAL_ID", problemDetails.GetProperty("reasonCode").GetString());
    }

    [Fact]
    public async Task GET_JournalEntries_ById_ExistingEntry_Returns200OK()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var request = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        var createResponse = await _client.PostAsJsonAsync("/api/JournalEntries", request);
        createResponse.EnsureSuccessStatusCode();
        var createdEntry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(createResponse.Content);
        Assert.NotNull(createdEntry);

        // Act
        var response = await _client.GetAsync($"/api/JournalEntries/{createdEntry.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(response.Content);
        Assert.NotNull(entry);
        Assert.Equal(createdEntry.Id, entry.Id);
        Assert.Equal(2, entry.Lines.Count);
    }

    [Fact]
    public async Task GET_JournalEntries_ById_NotFound_Returns404NotFound()
    {
        // Arrange
        Assert.NotNull(_client);
        var nonExistentId = Guid.NewGuid();

        // Act
        var response = await _client.GetAsync($"/api/JournalEntries/{nonExistentId}");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task POST_JournalEntries_ExactDecimalBalance_ValidatesCorrectly()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var request = new CreateJournalEntryRequest(
            null,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.1234m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.1234m) // Exact match
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/JournalEntries", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await JsonHelper.ReadFromJsonAsync<JournalEntryResponse>(response.Content);
        Assert.NotNull(entry);
    }
}

