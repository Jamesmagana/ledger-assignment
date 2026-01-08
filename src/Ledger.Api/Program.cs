using System.Text;
using System.Text.Json;
using Ledger.Api.Configuration;
using Ledger.Api.Middleware;
using Ledger.Application.Repositories;
using Ledger.Application.Services;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Repositories;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.JsonSerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    })
    .ConfigureApiBehaviorOptions(options =>
    {
        // Configure ProblemDetails for model validation errors
        options.InvalidModelStateResponseFactory = context =>
        {
            var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString()
                ?? Guid.NewGuid().ToString();

            var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "Validation Error",
                Status = StatusCodes.Status400BadRequest,
                Instance = context.HttpContext.Request.Path,
                Detail = "One or more validation errors occurred."
            };

            // Add validation errors to extensions
            if (context.ModelState != null && context.ModelState.Any())
            {
                var errors = context.ModelState
                    .Where(x => x.Value?.Errors.Count > 0)
                    .ToDictionary(
                        kvp => kvp.Key,
                        kvp => kvp.Value!.Errors.Select(e => e.ErrorMessage).ToArray());

                problemDetails.Extensions["errors"] = errors;
            }

            // Add reasonCode and correlationId
            problemDetails.Extensions["reasonCode"] = "VALIDATION_ERROR";
            problemDetails.Extensions["correlationId"] = correlationId;

            return new Microsoft.AspNetCore.Mvc.BadRequestObjectResult(problemDetails)
            {
                ContentTypes = { "application/problem+json" }
            };
        };
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Ledger API",
        Version = "v1"
    });

    // Add JWT Bearer authentication to Swagger
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token.",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "Bearer"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

// Add HttpContextAccessor for audit logging
builder.Services.AddHttpContextAccessor();

// Register audit logging services
builder.Services.AddScoped<Ledger.Infrastructure.Data.Services.IUserContextService, Ledger.Infrastructure.Data.Services.UserContextService>();
builder.Services.AddScoped<IAuditLogRepository, AuditLogRepository>();
builder.Services.AddScoped<IAuditLogService, AuditLogService>();

// Register DbContext with interceptor
var connectionString = builder.Configuration.GetConnectionString("LedgerDb");
builder.Services.AddDbContext<LedgerDbContext>((sp, options) =>
{
    options.UseNpgsql(connectionString);

    if (builder.Environment.IsDevelopment())
    {
        options.EnableSensitiveDataLogging();
    }

    // Add audit logging interceptor
    var httpContextAccessor = sp.GetRequiredService<IHttpContextAccessor>();
    var userContextService = sp.GetRequiredService<Ledger.Infrastructure.Data.Services.IUserContextService>();
    options.AddInterceptors(new Ledger.Infrastructure.Data.Interceptors.AuditLoggingInterceptor(
        httpContextAccessor,
        userContextService));
});

// Add health checks
builder.Services.AddHealthChecks()
    .AddDbContextCheck<LedgerDbContext>();

// Configure JWT settings
var jwtSettings = builder.Configuration.GetSection("Jwt").Get<JwtSettings>();
if (jwtSettings == null || string.IsNullOrWhiteSpace(jwtSettings.SecretKey))
{
    throw new InvalidOperationException("JWT configuration is missing or invalid. Please configure Jwt:SecretKey in appsettings.json or environment variables.");
}

if (jwtSettings.SecretKey.Length < 32)
{
    throw new InvalidOperationException("JWT SecretKey must be at least 32 characters long.");
}

builder.Services.Configure<JwtSettings>(builder.Configuration.GetSection("Jwt"));

// Add JWT authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtSettings.Issuer,
            ValidAudience = jwtSettings.Audience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSettings.SecretKey)),
            ClockSkew = TimeSpan.FromSeconds(jwtSettings.ClockSkewSeconds),
            RequireExpirationTime = true,
            RequireSignedTokens = true
        };

        // Configure challenge event to return RFC7807 ProblemDetails
        options.Events = new JwtBearerEvents
        {
            OnChallenge = async context =>
            {
                context.HandleResponse();

                var correlationId = context.HttpContext.Items["CorrelationId"]?.ToString()
                    ?? Guid.NewGuid().ToString();

                var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
                {
                    Status = StatusCodes.Status401Unauthorized,
                    Title = "Unauthorized",
                    Detail = "Authentication required. Please provide a valid JWT Bearer token.",
                    Instance = context.HttpContext.Request.Path,
                    Type = "https://tools.ietf.org/html/rfc7235#section-3.1"
                };

                problemDetails.Extensions["reasonCode"] = "UNAUTHORIZED";
                problemDetails.Extensions["correlationId"] = correlationId;

                context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                context.Response.ContentType = "application/problem+json";

                var jsonOptions = new JsonSerializerOptions
                {
                    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
                };

                // Serialize manually to preserve the content type
                var json = JsonSerializer.Serialize(problemDetails, jsonOptions);
                await context.Response.WriteAsync(json);
            },
            OnAuthenticationFailed = context =>
            {
                // Log authentication failures for debugging
                var logger = context.HttpContext.RequestServices.GetRequiredService<ILogger<Program>>();
                logger.LogWarning("JWT authentication failed: {Error}", context.Exception.Message);
                return Task.CompletedTask;
            }
        };
    });

// Add authorization
builder.Services.AddAuthorization();

// Register repositories
builder.Services.AddScoped<IAccountRepository, AccountRepository>();
builder.Services.AddScoped<IJournalEntryRepository, JournalEntryRepository>();
builder.Services.AddScoped<ITrialBalanceRepository, TrialBalanceRepository>();
builder.Services.AddScoped<IUserRepository, UserRepository>();

// Register services
builder.Services.AddScoped<IAccountService, AccountService>();
builder.Services.AddScoped<IRequestHashService, RequestHashService>();
builder.Services.AddScoped<IJournalEntryService, JournalEntryService>();
builder.Services.AddScoped<ITrialBalanceService, TrialBalanceService>();

// Register user and authentication services
builder.Services.AddScoped<IPasswordHasher, PasswordHasher>();
builder.Services.AddScoped<IUserRepository, UserRepository>();
builder.Services.AddScoped<IUserService, UserService>();
builder.Services.AddScoped<IAuthenticationService, AuthenticationService>();

// Configure JWT token service settings (map from JwtSettings)
builder.Services.Configure<Ledger.Application.Services.JwtTokenSettings>(options =>
{
    options.Issuer = jwtSettings.Issuer;
    options.Audience = jwtSettings.Audience;
    options.SecretKey = jwtSettings.SecretKey;
});
builder.Services.AddScoped<IJwtTokenService, JwtTokenService>();

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCorrelationId();
app.UseExceptionHandling();
app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();
app.MapHealthChecks("/health");

app.Run();

// Make Program class accessible for integration tests
public partial class Program { }
