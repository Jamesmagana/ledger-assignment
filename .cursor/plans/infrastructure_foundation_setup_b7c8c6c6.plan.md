---
name: Infrastructure Foundation Setup
overview: Set up the infrastructure foundation layer including Docker Compose for PostgreSQL, EF Core with Npgsql, DbContext registration, health checks, and CorrelationId middleware. This establishes the persistence and observability foundation before implementing domain entities.
todos:
  - id: docker-compose
    content: Create docker-compose.yml with PostgreSQL 16 configuration, port mapping, environment variables, and health checks
    status: completed
  - id: ef-core-packages
    content: Add EF Core and Npgsql packages to Infrastructure project (Microsoft.EntityFrameworkCore, Npgsql.EntityFrameworkCore.PostgreSQL, Microsoft.EntityFrameworkCore.Design)
    status: completed
  - id: dbcontext
    content: Create LedgerDbContext class in Infrastructure/Data with constructor accepting DbContextOptions, configured for PostgreSQL
    status: completed
    dependencies:
      - ef-core-packages
  - id: connection-string
    content: Add ConnectionStrings section to appsettings.json and appsettings.Development.json with PostgreSQL connection string
    status: completed
  - id: dbcontext-registration
    content: Register DbContext in Program.cs with connection string from configuration, enable sensitive logging in Development
    status: completed
    dependencies:
      - dbcontext
      - connection-string
  - id: health-checks
    content: Add health checks service registration and /health endpoint mapping with database connectivity check
    status: completed
    dependencies:
      - dbcontext-registration
  - id: correlationid-middleware
    content: Create CorrelationIdMiddleware to read/generate X-Correlation-Id header and store in HttpContext.Items
    status: completed
  - id: correlationid-registration
    content: Register CorrelationIdMiddleware in Program.cs pipeline before other middleware
    status: completed
    dependencies:
      - correlationid-middleware
  - id: update-prompts
    content: Update PROMPTS.md with Phase 1.1 documenting infrastructure foundation decisions
    status: completed
    dependencies:
      - docker-compose
      - dbcontext-registration
      - health-checks
      - correlationid-registration
---

# Infrastructure Foundation Setup Plan

## Overview

Establish the infrastructure foundation for the ledger service, including database connectivity, health monitoring, and request correlation tracking. This prepares the system for domain entity implementation.

## Implementation Tasks

### 1. Docker Compose for PostgreSQL

**File:** `docker-compose.yml` (root)

- PostgreSQL 16 container
- Port mapping: 5432:5432
- Environment variables:
- POSTGRES_DB: ledger
- POSTGRES_USER: postgres
- POSTGRES_PASSWORD: admin (development only)
- Volume for data persistence
- Health check configuration
- Network configuration

**Connection String Format:**

```javascript
Host=localhost;Port=5432;Database=ledger;Username=postgres;Password=admin
```



### 2. EF Core and Npgsql Packages

**File:** `src/Ledger.Infrastructure/Ledger.Infrastructure.csproj`Add NuGet packages:

- `Microsoft.EntityFrameworkCore` (8.0.x)
- `Npgsql.EntityFrameworkCore.PostgreSQL` (8.0.x)
- `Microsoft.EntityFrameworkCore.Design` (8.0.x) - for migrations tooling

### 3. DbContext Implementation

**File:** `src/Ledger.Infrastructure/Data/LedgerDbContext.cs`Create DbContext class:

- Inherit from `DbContext`
- Constructor accepting `DbContextOptions<LedgerDbContext>`
- No entity `DbSet<>` properties yet (added in next phase)
- Configure for PostgreSQL:
- Use Npgsql provider
- Configure connection string
- Set default command timeout
- Enable sensitive data logging only in Development

**Design Decisions:**

- DbContext lives in Infrastructure layer (Clean Architecture)
- Options pattern for configuration
- No domain entities yet - placeholder structure

### 4. DbContext Registration in API

**File:** `src/Ledger.Api/Program.cs`Register services:

- Add DbContext to DI container using `AddDbContext<LedgerDbContext>`
- Read connection string from configuration
- Configure options:
- Connection string from `appsettings.json`
- Enable sensitive data logging in Development only
- Set command timeout

**File:** `src/Ledger.Api/appsettings.json`Add connection string:

```json
{
  "ConnectionStrings": {
    "LedgerDb": "Host=localhost;Port=5432;Database=ledger;Username=ledger_user;Password=ledger_password"
  }
}
```

**File:** `src/Ledger.Api/appsettings.Development.json`Override with development-specific connection string if needed.

### 5. Health Check Endpoint

**File:** `src/Ledger.Api/Program.cs`Add health checks:

- `AddHealthChecks()` service registration
- Database health check using `AddDbContextCheck<LedgerDbContext>`
- Map health endpoint: `/health`
- Configure health check response format

**Health Check Response:**

- Returns 200 OK when healthy
- Returns 503 Service Unavailable when unhealthy
- Includes database connectivity status

### 6. CorrelationId Middleware

**File:** `src/Ledger.Api/Middleware/CorrelationIdMiddleware.cs`Create middleware:

- Read `X-Correlation-Id` header from incoming request
- Generate new GUID if header is missing
- Store correlation ID in `HttpContext.Items` for request lifetime
- Add correlation ID to response headers (`X-Correlation-Id`)
- Ensure correlation ID is available for logging and audit

**File:** `src/Ledger.Api/Program.cs`Register middleware:

- Add `UseCorrelationId()` before other middleware
- Order: CorrelationId → Logging → Routing → Authorization

**Design Decisions:**

- CorrelationId stored in `HttpContext.Items["CorrelationId"]`
- Format: GUID string
- Header name: `X-Correlation-Id` (standard convention)
- Available for audit logging in future phases

### 7. Update PROMPTS.md

**File:** `PROMPTS.md`Add new phase entry:

- **Phase 1.1 – Infrastructure Foundation**
- Document decisions:
- PostgreSQL 16 via Docker Compose
- EF Core 8.0 with Npgsql provider
- DbContext in Infrastructure layer
- Health check endpoint at `/health`
- CorrelationId middleware pattern
- Connection string configuration approach

## File Structure

```javascript
ledger-assignment/
├── docker-compose.yml (new)
├── src/
│   ├── Ledger.Api/
│   │   ├── Program.cs (modify)
│   │   ├── Middleware/
│   │   │   └── CorrelationIdMiddleware.cs (new)
│   │   ├── appsettings.json (modify)
│   │   └── appsettings.Development.json (modify)
│   └── Ledger.Infrastructure/
│       ├── Data/
│       │   └── LedgerDbContext.cs (new)
│       └── Ledger.Infrastructure.csproj (modify)
└── PROMPTS.md (modify)
```



## Dependencies

- Docker Desktop (for docker-compose)
- PostgreSQL 16 (via Docker)
- .NET 8 SDK (already installed)

## Verification Steps

1. `docker-compose up -d` starts PostgreSQL successfully
2. `dotnet build` compiles all projects
3. `dotnet run --project src/Ledger.Api` starts API
4. `GET /health` returns 200 OK
5. `GET /health` with database down returns 503
6. Request with `X-Correlation-Id` header preserves value
7. Request without header generates new correlation ID
8. Correlation ID appears in response headers

## Notes