# Ledger API

A production-grade double-entry ledger system built with .NET 8 and ASP.NET Core, following Clean Architecture principles. This system enforces strict financial invariants, provides comprehensive audit logging, and ensures data integrity through database constraints.

## Features

- **Double-Entry Bookkeeping**: Enforces debits equal credits with exact decimal precision
- **Idempotent Operations**: Journal entry posting supports idempotency via external IDs
- **Comprehensive Audit Logging**: All state changes are automatically logged with correlation IDs
- **JWT Authentication**: Secure API access with JWT Bearer tokens
- **Database Constraints**: Critical invariants enforced at the database level
- **RFC7807 Error Responses**: Standardized ProblemDetails format for all errors

## Prerequisites

- .NET 8 SDK or later
- PostgreSQL 12+ (or Docker for running PostgreSQL)
- Docker (for running integration tests with Testcontainers)

## Local Development Setup

### 1. Clone the Repository

```bash
git clone <repository-url>
cd ledger-assignment
```

### 2. Configure Database Connection

Update `src/Ledger.Api/appsettings.Development.json` with your PostgreSQL connection string:

```json
{
  "ConnectionStrings": {
    "LedgerDb": "Host=localhost;Port=5432;Database=ledger_dev;Username=postgres;Password=your_password"
  }
}
```

### 3. Configure JWT Settings

Update `src/Ledger.Api/appsettings.Development.json` with JWT configuration:

```json
{
  "Jwt": {
    "Issuer": "https://ledger-api.local",
    "Audience": "https://ledger-api.local",
    "SecretKey": "YourSecretKeyMustBeAtLeast32CharactersLongForSecurity",
    "ClockSkewSeconds": 60
  }
}
```

**Important**: The `SecretKey` must be at least 32 characters long. In production, use environment variables or a secure secret management system.

### 4. Run Database Migrations

```bash
dotnet ef database update --project src/Ledger.Infrastructure --startup-project src/Ledger.Api
```

### 5. Start the Application

```bash
cd src/Ledger.Api
dotnet run
```

The API will be available at:
- HTTP: `http://localhost:5000`
- HTTPS: `https://localhost:5001`
- Swagger UI: `https://localhost:5001/swagger`

## Database Migrations

### Create a New Migration

```bash
dotnet ef migrations add MigrationName --project src/Ledger.Infrastructure --startup-project src/Ledger.Api
```

### Apply Migrations

```bash
dotnet ef database update --project src/Ledger.Infrastructure --startup-project src/Ledger.Api
```

### Rollback to a Previous Migration

```bash
dotnet ef database update PreviousMigrationName --project src/Ledger.Infrastructure --startup-project src/Ledger.Api
```

### List All Migrations

```bash
dotnet ef migrations list --project src/Ledger.Infrastructure --startup-project src/Ledger.Api
```

## Authentication Usage

### 1. Create a User

```bash
curl -X POST https://localhost:5001/api/users \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com",
    "password": "SecurePassword123!"
  }'
```

**Response (201 Created):**
```json
{
  "id": "guid",
  "email": "user@example.com",
  "isEnabled": true,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

### 2. Login and Get JWT Token

```bash
curl -X POST https://localhost:5001/api/auth/login \
  -H "Content-Type: application/json" \
  -d '{
    "email": "user@example.com",
    "password": "SecurePassword123!"
  }'
```

**Response (200 OK):**
```json
{
  "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9...",
  "userId": "guid",
  "email": "user@example.com"
}
```

### 3. Use JWT Token in Requests

Include the token in the `Authorization` header:

```bash
curl -X GET https://localhost:5001/api/accounts \
  -H "Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9..."
```

**Token Expiration**: JWT tokens expire after 1 hour. Re-authenticate to get a new token.

## Example API Requests

### Create an Account

```bash
curl -X POST https://localhost:5001/api/accounts \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: <optional-correlation-id>" \
  -d '{
    "name": "Cash",
    "type": 0,
    "isActive": true
  }'
```

**Account Types:**
- `0` = Asset
- `1` = Liability
- `2` = Equity
- `3` = Revenue
- `4` = Expense

**Response (201 Created):**
```json
{
  "id": "guid",
  "name": "Cash",
  "type": 0,
  "isActive": true,
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z"
}
```

### Update an Account

```bash
curl -X PUT https://localhost:5001/api/accounts/{accountId} \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -d '{
    "isActive": false
  }'
```

**Note**: Only `isActive` can be updated. Account name and type are immutable after creation.

### Post a Journal Entry

```bash
curl -X POST https://localhost:5001/api/journal-entries \
  -H "Authorization: Bearer <token>" \
  -H "Content-Type: application/json" \
  -H "X-Correlation-Id: <optional-correlation-id>" \
  -d '{
    "externalId": "EXT-001",
    "lines": [
      {
        "accountId": "<cash-account-id>",
        "direction": 0,
        "amount": 1000.00
      },
      {
        "accountId": "<revenue-account-id>",
        "direction": 1,
        "amount": 1000.00
      }
    ]
  }'
```

**Line Directions:**
- `0` = Debit
- `1` = Credit

**Idempotency**: If you post the same request with the same `externalId` and payload, you'll get a `200 OK` response with `idempotencyReplay: true` instead of creating a duplicate entry.

**Response (201 Created or 200 OK):**
```json
{
  "id": "guid",
  "externalId": "EXT-001",
  "postedAt": "2024-01-01T00:00:00Z",
  "createdAt": "2024-01-01T00:00:00Z",
  "updatedAt": "2024-01-01T00:00:00Z",
  "lines": [
    {
      "id": "guid",
      "accountId": "<cash-account-id>",
      "direction": 0,
      "amount": 1000.00
    },
    {
      "id": "guid",
      "accountId": "<revenue-account-id>",
      "direction": 1,
      "amount": 1000.00
    }
  ],
  "idempotencyReplay": false
}
```

### Get Trial Balance

```bash
curl -X GET "https://localhost:5001/api/reports/trial-balance?asOf=2024-01-01T00:00:00Z" \
  -H "Authorization: Bearer <token>"
```

**Query Parameters:**
- `asOf` (optional): UTC datetime to filter journal entries. If omitted, includes all entries.

**Response (200 OK):**
```json
{
  "asOf": "2024-01-01T00:00:00Z",
  "items": [
    {
      "accountId": "guid",
      "accountName": "Cash",
      "accountType": 0,
      "totalDebits": 1000.00,
      "totalCredits": 0.00,
      "net": 1000.00
    },
    {
      "accountId": "guid",
      "accountName": "Revenue",
      "accountType": 3,
      "totalDebits": 0.00,
      "totalCredits": 1000.00,
      "net": -1000.00
    }
  ],
  "totalNet": 0.00
}
```

## Configuration

### Connection String Format

```
Host=<host>;Port=<port>;Database=<database>;Username=<username>;Password=<password>
```

### JWT Settings

- **Issuer**: Token issuer identifier
- **Audience**: Token audience identifier
- **SecretKey**: Symmetric key for signing tokens (minimum 32 characters)
- **ClockSkewSeconds**: Allowed clock skew for token validation (default: 60)

### Environment Variables

You can override configuration using environment variables:

```bash
export ConnectionStrings__LedgerDb="Host=localhost;Port=5432;Database=ledger;Username=postgres;Password=secret"
export Jwt__Issuer="https://ledger-api.prod"
export Jwt__Audience="https://ledger-api.prod"
export Jwt__SecretKey="YourProductionSecretKeyMustBeAtLeast32CharactersLong"
export Jwt__ClockSkewSeconds="30"
```

### Production Configuration Recommendations

1. **Use Environment Variables**: Never commit secrets to source control
2. **Strong Secret Key**: Use a cryptographically random key of at least 32 characters
3. **HTTPS Only**: Ensure all API traffic uses HTTPS
4. **Database Security**: Use connection pooling and encrypted connections
5. **Audit Log Retention**: Plan for audit log retention and archival
6. **Monitoring**: Set up logging and monitoring for production

## Running Tests

### Unit Tests

```bash
dotnet test tests/Ledger.Tests/Ledger.Tests.csproj --filter "Category=Unit"
```

### Integration Tests

Integration tests require Docker to be running (for Testcontainers):

```bash
dotnet test tests/Ledger.Tests/Ledger.Tests.csproj --filter "Category=Integration"
```

### All Tests

```bash
dotnet test tests/Ledger.Tests/Ledger.Tests.csproj
```

**Note**: Integration tests create isolated PostgreSQL containers for each test, ensuring no test interference.

## Architecture Overview

### Clean Architecture Layers

- **Api**: HTTP endpoints, DTOs, ProblemDetails responses
- **Application**: Business logic, validations, orchestration
- **Domain**: Entities, enums, domain invariants (no EF Core dependencies)
- **Infrastructure**: EF Core, PostgreSQL, persistence, audit logging

### Key Design Decisions

1. **Immutability**: Journal entries are immutable after posting. Corrections use reversing entries.
2. **Idempotency**: Journal entry posting is idempotent via `externalId` and request hash.
3. **Double-Entry Enforcement**: Debits must equal credits (exact decimal match).
4. **Database Constraints**: Critical invariants enforced at both application and database levels.
5. **Audit Logging**: All state changes automatically logged with correlation IDs.

### Invariant Enforcement Strategy

- **Application Layer**: Validates business rules before database operations
- **Database Layer**: CHECK constraints and unique indexes backstop critical invariants
- **Transaction Safety**: All related operations (e.g., journal entry + lines) are atomic

### Audit Logging

- **Automatic**: EF Core interceptor captures all entity changes
- **Append-Only**: Audit logs are immutable (no updates or deletes)
- **Correlation IDs**: Every audit entry includes the request correlation ID
- **User Tracking**: PerformedBy user ID captured from JWT claims
- **Sensitive Data Exclusion**: Passwords and hashes are automatically excluded

## Error Responses

All errors follow RFC7807 ProblemDetails format:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "One or more validation errors occurred.",
  "instance": "/api/accounts",
  "reasonCode": "VALIDATION_ERROR",
  "correlationId": "guid"
}
```

**Common Error Codes:**
- `VALIDATION_ERROR`: Request validation failed
- `UNAUTHORIZED`: Authentication required or failed
- `ACCOUNT_NOT_FOUND`: Account does not exist
- `DUPLICATE_ACCOUNT_NAME`: Account name already exists (case-insensitive)
- `UNBALANCED_ENTRY`: Journal entry debits do not equal credits
- `DUPLICATE_EXTERNAL_ID`: External ID already exists with different payload
- `IDEMPOTENCY_REPLAY`: Same request replayed (200 OK response)

## Troubleshooting

### Database Connection Issues

**Error**: "Unable to connect to database"

**Solutions**:
1. Verify PostgreSQL is running: `docker ps` or `pg_isready`
2. Check connection string in `appsettings.json`
3. Verify database exists: `psql -U postgres -l`
4. Check firewall/network settings

### JWT Token Issues

**Error**: "401 Unauthorized" with `reasonCode: UNAUTHORIZED`

**Solutions**:
1. Verify token is included in `Authorization: Bearer <token>` header
2. Check token hasn't expired (1 hour lifetime)
3. Verify JWT settings match between token generation and validation
4. Check token format (should start with `eyJ`)

### Migration Conflicts

**Error**: "Migration conflicts detected"

**Solutions**:
1. Review pending migrations: `dotnet ef migrations list`
2. Apply pending migrations: `dotnet ef database update`
3. If conflicts persist, create a new migration to resolve differences

### Test Failures

**Error**: "Docker is either not running or misconfigured"

**Solutions**:
1. Ensure Docker Desktop is running
2. Verify Docker daemon is accessible: `docker ps`
3. Check Testcontainers configuration

### Audit Logging Not Working

**Symptoms**: No audit logs created for entity changes

**Solutions**:
1. Verify interceptor is registered in `Program.cs`
2. Check `HttpContextAccessor` is registered
3. Verify correlation ID middleware is in pipeline
4. Check database for `AuditLogs` table

## Security Considerations

1. **Authentication**: All write endpoints require valid JWT tokens
2. **Password Security**: Passwords are hashed using BCrypt (never stored in plain text)
3. **Sensitive Data**: Passwords and hashes are excluded from audit logs
4. **Input Validation**: All inputs validated at API and application layers
5. **SQL Injection**: EF Core parameterized queries prevent SQL injection
6. **HTTPS**: Use HTTPS in production to protect tokens and data in transit

## License

[Specify your license here]

## Contributing

[Specify contribution guidelines here]

