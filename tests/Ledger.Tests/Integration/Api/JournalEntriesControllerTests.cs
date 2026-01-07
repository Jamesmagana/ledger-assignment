using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration.Api;

public class JournalEntriesControllerTests : IAsyncLifetime
{
    private PostgreSqlContainer? _postgresContainer;
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private Guid? _cashAccountId;
    private Guid? _revenueAccountId;

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

        // Create WebApplicationFactory with test database
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

                    // Register repositories and services
                    services.AddScoped<IAccountRepository, AccountRepository>();
                    services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
                    services.AddScoped<IAccountService, AccountService>();
                    services.AddScoped<IRequestHashService, RequestHashService>();
                    services.AddScoped<IJournalEntryService, JournalEntryService>();
                });
            });

        _client = _factory.CreateClient();

        // Get DbContext and run migrations
        using var scope = _factory.Services.CreateScope();
        _context = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        await _context.Database.MigrateAsync();

        // Create test accounts
        var cashResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Cash", AccountType.Asset, true));
        var cashAccount = await cashResponse.Content.ReadFromJsonAsync<AccountResponse>();
        _cashAccountId = cashAccount?.Id;

        var revenueResponse = await _client.PostAsJsonAsync("/api/accounts", new CreateAccountRequest("Revenue", AccountType.Revenue, true));
        var revenueAccount = await revenueResponse.Content.ReadFromJsonAsync<AccountResponse>();
        _revenueAccountId = revenueAccount?.Id;
    }

    public async Task DisposeAsync()
    {
        if (_context != null)
        {
            await _context.DisposeAsync();
        }

        _client?.Dispose();
        _factory?.Dispose();

        if (_postgresContainer != null)
        {
            await _postgresContainer.DisposeAsync();
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
        var response = await _client.PostAsJsonAsync("/api/journal-entries", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<JournalEntryResponse>();
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
        var response = await _client.PostAsJsonAsync("/api/journal-entries", request);

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
        var response = await _client.PostAsJsonAsync("/api/journal-entries", request);

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
        var firstResponse = await _client.PostAsJsonAsync("/api/journal-entries", request);
        Assert.Equal(HttpStatusCode.Created, firstResponse.StatusCode);
        var firstEntry = await firstResponse.Content.ReadFromJsonAsync<JournalEntryResponse>();
        Assert.NotNull(firstEntry);

        // Act - Second request (idempotent replay)
        var secondResponse = await _client.PostAsJsonAsync("/api/journal-entries", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, secondResponse.StatusCode);
        var secondEntry = await secondResponse.Content.ReadFromJsonAsync<JournalEntryResponse>();
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
        await _client.PostAsJsonAsync("/api/journal-entries", request1);

        // Second request with same ExternalId but different payload
        var request2 = new CreateJournalEntryRequest(
            externalId,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 200.00m), // Different amount
                new(_revenueAccountId.Value, LineDirection.Credit, 200.00m)
            });

        // Act
        var response = await _client.PostAsJsonAsync("/api/journal-entries", request2);

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

        var createResponse = await _client.PostAsJsonAsync("/api/journal-entries", request);
        var createdEntry = await createResponse.Content.ReadFromJsonAsync<JournalEntryResponse>();
        Assert.NotNull(createdEntry);

        // Act
        var response = await _client.GetAsync($"/api/journal-entries/{createdEntry.Id}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<JournalEntryResponse>();
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
        var response = await _client.GetAsync($"/api/journal-entries/{nonExistentId}");

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
        var response = await _client.PostAsJsonAsync("/api/journal-entries", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var entry = await response.Content.ReadFromJsonAsync<JournalEntryResponse>();
        Assert.NotNull(entry);
    }
}

