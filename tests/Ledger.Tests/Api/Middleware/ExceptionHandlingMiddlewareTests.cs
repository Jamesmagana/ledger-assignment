using Ledger.Api.Middleware;
using Ledger.Api.Models;
using Ledger.Application.Exceptions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Moq;
using System.Net;
using System.Text;
using System.Text.Json;
using Xunit;

namespace Ledger.Tests.Api.Middleware;

public class ExceptionHandlingMiddlewareTests
{
    private readonly Mock<RequestDelegate> _nextMock;
    private readonly Mock<ILogger<ExceptionHandlingMiddleware>> _loggerMock;
    private readonly Mock<IWebHostEnvironment> _environmentMock;
    private readonly ExceptionHandlingMiddleware _middleware;
    private readonly DefaultHttpContext _context;

    public ExceptionHandlingMiddlewareTests()
    {
        _nextMock = new Mock<RequestDelegate>();
        _loggerMock = new Mock<ILogger<ExceptionHandlingMiddleware>>();
        _environmentMock = new Mock<IWebHostEnvironment>();
        _environmentMock.Setup(e => e.EnvironmentName).Returns("Production");

        _middleware = new ExceptionHandlingMiddleware(
            _nextMock.Object,
            _loggerMock.Object,
            _environmentMock.Object);

        _context = new DefaultHttpContext();
        _context.Request.Path = "/api/test";
        _context.Request.Method = "GET";
        _context.Response.Body = new MemoryStream();
    }

    [Fact]
    public async Task InvokeAsync_ValidationException_Returns400WithReasonCodeAndCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new ValidationException("Validation failed", "VALIDATION_ERROR");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal((int)HttpStatusCode.BadRequest, _context.Response.StatusCode);
        Assert.Equal("application/problem+json", _context.Response.ContentType);

        var problem = await DeserializeProblemDetails();
        Assert.Equal("VALIDATION_ERROR", problem.Extensions?["reasonCode"]?.ToString());
        Assert.Equal(correlationId, problem.Extensions?["correlationId"]?.ToString());
        Assert.Equal("Validation Error", problem.Title);
        Assert.Equal("Validation failed", problem.Detail);
    }

    [Fact]
    public async Task InvokeAsync_ConflictException_Returns409WithReasonCodeAndCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new ConflictException("Duplicate resource", "DUPLICATE_ACCOUNT_NAME");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal((int)HttpStatusCode.Conflict, _context.Response.StatusCode);

        var problem = await DeserializeProblemDetails();
        Assert.Equal("DUPLICATE_ACCOUNT_NAME", problem.Extensions?["reasonCode"]?.ToString());
        Assert.Equal(correlationId, problem.Extensions?["correlationId"]?.ToString());
        Assert.Equal("Conflict", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_NotFoundException_Returns404WithReasonCodeAndCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new NotFoundException("Resource not found", "ACCOUNT_NOT_FOUND");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal((int)HttpStatusCode.NotFound, _context.Response.StatusCode);

        var problem = await DeserializeProblemDetails();
        Assert.Equal("ACCOUNT_NOT_FOUND", problem.Extensions?["reasonCode"]?.ToString());
        Assert.Equal(correlationId, problem.Extensions?["correlationId"]?.ToString());
        Assert.Equal("Resource Not Found", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_UnauthorizedException_Returns401WithReasonCodeAndCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new UnauthorizedException("Authentication required", "UNAUTHORIZED");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal((int)HttpStatusCode.Unauthorized, _context.Response.StatusCode);

        var problem = await DeserializeProblemDetails();
        Assert.Equal("UNAUTHORIZED", problem.Extensions?["reasonCode"]?.ToString());
        Assert.Equal(correlationId, problem.Extensions?["correlationId"]?.ToString());
        Assert.Equal("Unauthorized", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_GenericException_Returns500WithReasonCodeAndCorrelationId()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new InvalidOperationException("Something went wrong");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        Assert.Equal((int)HttpStatusCode.InternalServerError, _context.Response.StatusCode);

        var problem = await DeserializeProblemDetails();
        Assert.Equal("INTERNAL_ERROR", problem.Extensions?["reasonCode"]?.ToString());
        Assert.Equal(correlationId, problem.Extensions?["correlationId"]?.ToString());
        Assert.Equal("Internal Server Error", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_MissingCorrelationId_GeneratesNewCorrelationId()
    {
        // Arrange
        // Don't set CorrelationId in context
        var exception = new ValidationException("Test error");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        var problem = await DeserializeProblemDetails();
        var correlationId = problem.Extensions?["correlationId"]?.ToString();
        Assert.NotNull(correlationId);
        Assert.True(Guid.TryParse(correlationId, out _)); // Should be a valid GUID
    }

    [Fact]
    public async Task InvokeAsync_AlwaysIncludesCorrelationId()
    {
        // Arrange
        var correlationId = "test-correlation-id-123";
        _context.Items["CorrelationId"] = correlationId;
        var exception = new ArgumentException("Invalid argument");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        var problem = await DeserializeProblemDetails();
        Assert.Equal(correlationId, problem.Extensions?["correlationId"]?.ToString());
    }

    [Fact]
    public async Task InvokeAsync_AlwaysIncludesReasonCode()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new Exception("Generic error");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        var problem = await DeserializeProblemDetails();
        Assert.NotNull(problem.Extensions?["reasonCode"]);
        Assert.Equal("INTERNAL_ERROR", problem.Extensions?["reasonCode"]?.ToString());
    }

    [Fact]
    public async Task InvokeAsync_ProblemDetailsHasCorrectStructure()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new ValidationException("Test validation error", "TEST_ERROR");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        var problem = await DeserializeProblemDetails();
        Assert.NotNull(problem.Type);
        Assert.NotNull(problem.Title);
        Assert.NotNull(problem.Status);
        Assert.NotNull(problem.Detail);
        Assert.NotNull(problem.Instance);
        Assert.NotNull(problem.Extensions);
        Assert.True(problem.Extensions.ContainsKey("reasonCode"));
        Assert.True(problem.Extensions.ContainsKey("correlationId"));
    }

    [Fact]
    public async Task InvokeAsync_LogsException()
    {
        // Arrange
        var correlationId = Guid.NewGuid().ToString();
        _context.Items["CorrelationId"] = correlationId;
        var exception = new Exception("Test exception");
        _nextMock.Setup(n => n(It.IsAny<HttpContext>())).ThrowsAsync(exception);

        // Act
        await _middleware.InvokeAsync(_context);

        // Assert
        _loggerMock.Verify(
            x => x.Log(
                LogLevel.Error,
                It.IsAny<EventId>(),
                It.Is<It.IsAnyType>((v, t) => true),
                It.IsAny<Exception>(),
                It.Is<Func<It.IsAnyType, Exception?, string>>((v, t) => true)),
            Times.Once);
    }

    private async Task<ProblemDetails> DeserializeProblemDetails()
    {
        _context.Response.Body.Seek(0, SeekOrigin.Begin);
        var body = await new StreamReader(_context.Response.Body).ReadToEndAsync();
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        };
        var jsonDoc = JsonDocument.Parse(body);
        var root = jsonDoc.RootElement;
        
        var problem = new ProblemDetails
        {
            Type = root.TryGetProperty("type", out var typeProp) ? typeProp.GetString() : null,
            Title = root.TryGetProperty("title", out var titleProp) ? titleProp.GetString() : null,
            Status = root.TryGetProperty("status", out var statusProp) ? statusProp.GetInt32() : null,
            Detail = root.TryGetProperty("detail", out var detailProp) ? detailProp.GetString() : null,
            Instance = root.TryGetProperty("instance", out var instanceProp) ? instanceProp.GetString() : null,
            Extensions = new Dictionary<string, object?>()
        };

        // Extract extensions (reasonCode, correlationId, etc.)
        foreach (var prop in root.EnumerateObject())
        {
            if (prop.Name != "type" && prop.Name != "title" && prop.Name != "status" && 
                prop.Name != "detail" && prop.Name != "instance")
            {
                problem.Extensions[prop.Name] = prop.Value.ValueKind switch
                {
                    JsonValueKind.String => prop.Value.GetString(),
                    JsonValueKind.Number => prop.Value.GetInt32(),
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    _ => prop.Value.ToString()
                };
            }
        }

        return problem;
    }

    private class ProblemDetails
    {
        public string? Type { get; set; }
        public string? Title { get; set; }
        public int? Status { get; set; }
        public string? Detail { get; set; }
        public string? Instance { get; set; }
        public Dictionary<string, object?> Extensions { get; set; } = new();
    }
}

