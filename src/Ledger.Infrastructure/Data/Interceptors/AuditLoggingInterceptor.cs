using Ledger.Application.Services;
using Ledger.Domain.Entities;
using Ledger.Infrastructure.Data;
using Ledger.Infrastructure.Data.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace Ledger.Infrastructure.Data.Interceptors;

/// <summary>
/// EF Core interceptor that automatically creates audit log entries
/// for all entity changes (Added, Modified).
/// </summary>
public class AuditLoggingInterceptor : SaveChangesInterceptor
{
    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IUserContextService _userContextService;

    public AuditLoggingInterceptor(
        IHttpContextAccessor httpContextAccessor,
        IUserContextService userContextService)
    {
        _httpContextAccessor = httpContextAccessor;
        _userContextService = userContextService;
    }

    public override InterceptionResult<int> SavingChanges(
        DbContextEventData eventData,
        InterceptionResult<int> result)
    {
        if (eventData.Context is not null)
        {
            ProcessEntityChanges(eventData.Context);
        }

        return base.SavingChanges(eventData, result);
    }

    public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData,
        InterceptionResult<int> result,
        CancellationToken cancellationToken = default)
    {
        if (eventData.Context is not null)
        {
            await ProcessEntityChangesAsync(eventData.Context, cancellationToken);
        }

        return await base.SavingChangesAsync(eventData, result, cancellationToken);
    }

    private void ProcessEntityChanges(DbContext context)
    {
        // Synchronous version - call async method and wait
        ProcessEntityChangesAsync(context, CancellationToken.None).GetAwaiter().GetResult();
    }

    private async Task ProcessEntityChangesAsync(DbContext context, CancellationToken cancellationToken)
    {
        // Get correlation ID from HTTP context
        var correlationId = GetCorrelationId();

        // Get current user ID
        var performedBy = _userContextService.GetCurrentUserId();

        // Process all tracked entities
        var entries = context.ChangeTracker.Entries()
            .Where(e => e.State == EntityState.Added || e.State == EntityState.Modified)
            .ToList();

        foreach (var entry in entries)
        {
            // Skip AuditLog entities themselves (prevent infinite recursion)
            if (entry.Entity is AuditLog)
            {
                continue;
            }

            var entityName = entry.Entity.GetType().Name;
            var entityId = GetEntityId(entry.Entity);

            if (entityId == Guid.Empty)
            {
                // Entity doesn't have an Id property or it's not set yet
                continue;
            }

            string action;
            object? oldValues = null;
            object? newValues = null;

            if (entry.State == EntityState.Added)
            {
                action = "CREATE";
                newValues = entry.Entity;
            }
            else if (entry.State == EntityState.Modified)
            {
                action = "UPDATE";
                
                // Get original values (before change)
                var originalValues = new Dictionary<string, object?>();
                foreach (var property in entry.Properties)
                {
                    if (property.IsModified)
                    {
                        originalValues[property.Metadata.Name] = property.OriginalValue;
                    }
                }
                oldValues = originalValues.Count > 0 ? originalValues : null;

                // Get current values (after change)
                newValues = entry.Entity;
            }
            else
            {
                continue; // Skip other states
            }

            // Serialize old and new values, excluding sensitive fields
            var oldValuesJson = SensitiveFieldExcluder.SerializeToJsonExcludingSensitive(oldValues);
            var newValuesJson = SensitiveFieldExcluder.SerializeToJsonExcludingSensitive(newValues);

            // Create audit log entry directly in the same context
            var auditLog = new AuditLog(
                entityName,
                entityId,
                action,
                oldValuesJson,
                newValuesJson,
                performedBy,
                correlationId);

            // Add to context (will be saved in same transaction)
            if (context is LedgerDbContext ledgerContext)
            {
                ledgerContext.AuditLogs.Add(auditLog);
            }
        }
    }

    private string GetCorrelationId()
    {
        var httpContext = _httpContextAccessor.HttpContext;
        if (httpContext != null && httpContext.Items.TryGetValue("CorrelationId", out var correlationIdObj))
        {
            return correlationIdObj?.ToString() ?? Guid.NewGuid().ToString();
        }

        // Fallback: generate new correlation ID if not available
        return Guid.NewGuid().ToString();
    }

    private static Guid GetEntityId(object entity)
    {
        // Try to get Id property using reflection
        var idProperty = entity.GetType().GetProperty("Id");
        if (idProperty != null)
        {
            var idValue = idProperty.GetValue(entity);
            if (idValue is Guid guid)
            {
                return guid;
            }
        }

        return Guid.Empty;
    }
}

