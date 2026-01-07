using Ledger.Api.DTOs.Accounts;
using Ledger.Api.DTOs.JournalEntries;
using Ledger.Domain.Enums;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Net;
using System.Net.Http.Json;
using Testcontainers.PostgreSql;

namespace Ledger.Tests.Integration.Api;

public class JournalEntriesConcurrencyTests : IAsyncLifetime
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
    public async Task POST_JournalEntries_ConcurrentSameExternalId_CreatesOnlyOneEntry()
    {
        // Arrange
        Assert.NotNull(_client);
        Assert.NotNull(_cashAccountId);
        Assert.NotNull(_revenueAccountId);

        var externalId = $"ext-concurrent-{Guid.NewGuid()}";
        var request = new CreateJournalEntryRequest(
            externalId,
            new List<CreateJournalEntryLineRequest>
            {
                new(_cashAccountId.Value, LineDirection.Debit, 100.00m),
                new(_revenueAccountId.Value, LineDirection.Credit, 100.00m)
            });

        // Act - Execute 10 parallel requests with same ExternalId and same payload
        var tasks = Enumerable.Range(0, 10)
            .Select(_ => _client.PostAsJsonAsync("/api/journal-entries", request))
            .ToArray();

        var responses = await Task.WhenAll(tasks);

        // Assert
        // All requests should succeed (either 201 or 200 for idempotent replay)
        foreach (var response in responses)
        {
            Assert.True(
                response.StatusCode == HttpStatusCode.Created || 
                response.StatusCode == HttpStatusCode.OK,
                $"Unexpected status code: {response.StatusCode}");
        }

        // All responses should have the same JournalEntry ID
        var entries = new List<JournalEntryResponse>();
        foreach (var response in responses)
        {
            var entry = await response.Content.ReadFromJsonAsync<JournalEntryResponse>();
            Assert.NotNull(entry);
            entries.Add(entry);
        }

        var firstEntryId = entries[0].Id;
        Assert.All(entries, e => Assert.Equal(firstEntryId, e.Id));

        // Verify database contains exactly one entry with this ExternalId
        using var scope = _factory!.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<LedgerDbContext>();
        var count = await dbContext.JournalEntries
            .CountAsync(je => je.ExternalId == externalId);
        
        Assert.Equal(1, count);

        // Verify all responses are idempotent replays (except possibly the first one)
        // At least one should be a replay
        var replayCount = entries.Count(e => e.IdempotencyReplay);
        Assert.True(replayCount >= 1, "At least one response should be an idempotent replay");
    }
}

