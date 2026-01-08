using System.Net;
using System.Net.Http.Json;
using Ledger.Api.DTOs.Users;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Ledger.Tests.Integration.Api;

public class UsersControllerTests : IAsyncLifetime
{
    private WebApplicationFactory<Program>? _factory;
    private HttpClient? _client;
    private LedgerDbContext? _context;
    private IServiceScope? _serviceScope;
    private readonly string _databaseName = $"UsersTest_{Guid.NewGuid()}";

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
                    services.AddScoped<IUserRepository, UserRepository>();
                    services.AddScoped<IPasswordHasher, PasswordHasher>();
                    services.AddScoped<IUserService, UserService>();
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
    public async Task PostUser_ValidRequest_ReturnsCreated()
    {
        // Arrange
        var request = new CreateUserRequest("test@example.com", "Password123");

        // Act
        var response = await _client!.PostAsJsonAsync("/api/users", request);

        // Assert
        response.EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var userResponse = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(userResponse);
        Assert.NotEqual(Guid.Empty, userResponse.Id);
        Assert.Equal(request.Email.ToLowerInvariant(), userResponse.Email);
        Assert.True(userResponse.IsEnabled);

        // Verify in DB
        _context!.ChangeTracker.Clear();
        var dbUser = await _context.Users.FindAsync(userResponse.Id);
        Assert.NotNull(dbUser);
        Assert.Equal(request.Email.ToLowerInvariant(), dbUser.Email);
        Assert.NotEmpty(dbUser.PasswordHash);
        Assert.NotEqual(request.Password, dbUser.PasswordHash); // Password should be hashed
    }

    [Fact]
    public async Task PostUser_DuplicateEmail_ReturnsConflict()
    {
        // Arrange
        var request1 = new CreateUserRequest("duplicate@example.com", "Password123");
        await _client!.PostAsJsonAsync("/api/users", request1); // Create first user

        var request2 = new CreateUserRequest("duplicate@example.com", "Password456"); // Duplicate email

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("DUPLICATE_USER", problemDetails.Extensions?["reasonCode"]?.ToString());
    }

    [Fact]
    public async Task PostUser_DuplicateEmailCaseInsensitive_ReturnsConflict()
    {
        // Arrange
        var request1 = new CreateUserRequest("CaseTest@Example.com", "Password123");
        await _client!.PostAsJsonAsync("/api/users", request1); // Create first user

        var request2 = new CreateUserRequest("casetest@example.com", "Password456"); // Duplicate email, different case

        // Act
        var response = await _client.PostAsJsonAsync("/api/users", request2);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal("DUPLICATE_USER", problemDetails.Extensions?["reasonCode"]?.ToString());
    }

    [Theory]
    [InlineData("", "Password123")]
    [InlineData("   ", "Password123")]
    [InlineData("invalid-email", "Password123")]
    [InlineData("test@example.com", "")]
    [InlineData("test@example.com", "short")]
    [InlineData("test@example.com", "onlyletters")]
    [InlineData("test@example.com", "12345678")]
    public async Task PostUser_InvalidInput_ReturnsBadRequest(string email, string password)
    {
        // Arrange
        var request = new CreateUserRequest(email, password);

        // Act
        var response = await _client!.PostAsJsonAsync("/api/users", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        // The API returns specific reason codes, not generic VALIDATION_ERROR
        var reasonCode = problemDetails.Extensions?["reasonCode"]?.ToString();
        Assert.NotNull(reasonCode);
        Assert.True(reasonCode == "VALIDATION_ERROR" || reasonCode == "WEAK_PASSWORD" || reasonCode == "INVALID_EMAIL", 
            $"Expected VALIDATION_ERROR, WEAK_PASSWORD, or INVALID_EMAIL but got {reasonCode}");
    }

    [Fact]
    public async Task PostUser_ResponseDoesNotContainPasswordHash()
    {
        // Arrange
        var request = new CreateUserRequest("test@example.com", "Password123");

        // Act
        var response = await _client!.PostAsJsonAsync("/api/users", request);

        // Assert
        response.EnsureSuccessStatusCode();
        var userResponse = await response.Content.ReadFromJsonAsync<UserResponse>();
        Assert.NotNull(userResponse);

        // Verify response JSON does not contain password hash
        var responseJson = await response.Content.ReadAsStringAsync();
        Assert.DoesNotContain("PasswordHash", responseJson);
        Assert.DoesNotContain("passwordHash", responseJson);
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

