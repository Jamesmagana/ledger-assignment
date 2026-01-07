---
name: RFC7807 ProblemDetails Error Handling Standardization
overview: Implement standardized RFC7807 ProblemDetails error handling across the API with reasonCode and correlationId, proper HTTP status code mapping, and comprehensive test coverage for middleware behavior.
todos:
  - id: create-problem-details-extensions
    content: Create ProblemDetailsExtensions class with WithReasonCode and WithCorrelationId methods
    status: pending
  - id: create-custom-exceptions
    content: Create custom exception types in Application layer (ValidationException, NotFoundException, ConflictException, UnauthorizedException)
    status: pending
  - id: create-exception-handling-middleware
    content: Create ExceptionHandlingMiddleware that catches all exceptions, converts to ProblemDetails, maps status codes, and includes reasonCode and correlationId
    status: pending
    dependencies:
      - create-problem-details-extensions
  - id: configure-problem-details
    content: Configure ProblemDetails in Program.cs with proper options and register exception handling middleware
    status: pending
    dependencies:
      - create-exception-handling-middleware
  - id: handle-db-exceptions
    content: Add database exception mapping logic to handle EF Core DbUpdateException and unique constraint violations
    status: pending
    dependencies:
      - create-exception-handling-middleware
  - id: handle-validation-errors
    content: Configure model validation to return ProblemDetails with reasonCode and correlationId
    status: pending
    dependencies:
      - configure-problem-details
  - id: create-middleware-tests
    content: Create comprehensive tests for exception handling middleware covering all error scenarios and status code mappings
    status: pending
    dependencies:
      - create-exception-handling-middleware
  - id: update-prompts
    content: Update PROMPTS.md with error handling implementation decisions and approach
    status: pending
    dependencies:
      - configure-problem-details
      - create-middleware-tests
---

# RFC7807 ProblemDetails Error Handling Standardization Plan

## Overview

Implement standardized error handling using RFC7807 ProblemDetails format across the entire API. All errors must include `reasonCode` (machine-readable) and `correlationId` for traceability. Ensure consistent error envelope and proper HTTP status code mapping.

## Current State Analysis

- CorrelationId middleware exists but doesn't inject correlationId into error responses
- No exception handling middleware configured
- No ProblemDetails configuration in Program.cs
- No custom exception types or error mapping logic
- Controllers don't exist yet (will be created in future phases)

## Implementation Tasks

### 1. Custom ProblemDetails Extension

**File:** `src/Ledger.Api/Models/ProblemDetailsExtensions.cs` (new)Create extension methods to add custom properties to ProblemDetails:

- `reasonCode` (string) - Machine-readable error code
- `correlationId` (string) - Request correlation ID

**Design:**

```csharp
public static class ProblemDetailsExtensions
{
    public static ProblemDetails WithReasonCode(this ProblemDetails problem, string reasonCode);
    public static ProblemDetails WithCorrelationId(this ProblemDetails problem, string correlationId);
}
```



### 2. Exception Handling Middleware

**File:** `src/Ledger.Api/Middleware/ExceptionHandlingMiddleware.cs` (new)Create global exception handling middleware that:

- Catches all unhandled exceptions
- Converts exceptions to RFC7807 ProblemDetails
- Extracts correlationId from HttpContext.Items
- Maps exception types to appropriate HTTP status codes
- Includes reasonCode for all errors
- Logs exceptions with correlationId

**Exception Type Mapping:**

- `ValidationException` / `ArgumentException` → 400 Bad Request
- `NotFoundException` / `KeyNotFoundException` → 404 Not Found
- `ConflictException` / `DuplicateException` → 409 Conflict
- `UnauthorizedAccessException` → 401 Unauthorized
- `ForbiddenException` → 403 Forbidden
- `DbUpdateException` (unique constraint violations) → 409 Conflict
- Generic exceptions → 500 Internal Server Error

**Reason Codes:**

- `VALIDATION_ERROR` - Validation failures
- `NOT_FOUND` - Resource not found
- `DUPLICATE_ACCOUNT_NAME` - Account name conflict
- `DUPLICATE_EXTERNAL_ID` - Journal entry external ID conflict
- `UNAUTHORIZED` - Authentication required
- `FORBIDDEN` - Access denied
- `INTERNAL_ERROR` - Unexpected server error

### 3. Update CorrelationId Middleware

**File:** `src/Ledger.Api/Middleware/CorrelationIdMiddleware.cs` (modify)Ensure correlationId is available for error responses:

- Already stores in HttpContext.Items["CorrelationId"]
- No changes needed, but verify integration with exception middleware

### 4. Configure ProblemDetails in Program.cs

**File:** `src/Ledger.Api/Program.cs` (modify)Configure ProblemDetails:

- `builder.Services.AddProblemDetails()` - Enable ProblemDetails
- Configure options:
- Customize response format
- Set default status codes
- Enable detailed errors in Development only
- Register exception handling middleware early in pipeline

**Middleware Order:**

1. CorrelationId middleware (already first)
2. Exception handling middleware (catch all exceptions)
3. HttpsRedirection
4. Authorization
5. Controllers

### 5. Custom Exception Types (Application Layer)

**File:** `src/Ledger.Application/Exceptions/` (new directory)Create custom exception types for business logic:

- `ValidationException` - Validation failures (400)
- `NotFoundException` - Resource not found (404)
- `ConflictException` - Duplicate/conflict errors (409)
- `UnauthorizedException` - Authentication failures (401)

**Design:**

- Include reasonCode in exception
- Include user-friendly message
- Support inner exceptions

### 6. Database Exception Mapping

**File:** `src/Ledger.Api/Middleware/ExceptionHandlingMiddleware.cs`Handle EF Core database exceptions:

- `DbUpdateException` with unique constraint violations → 409 Conflict
- Extract constraint name to determine reasonCode
- Map PostgreSQL error codes to appropriate status codes
- Preserve correlationId in all cases

### 7. Validation Error Handling

**File:** `src/Ledger.Api/Filters/ValidationFilter.cs` (new, optional)Create action filter for model validation:

- Intercept `ModelState` validation failures
- Convert to ProblemDetails with reasonCode
- Return 400 Bad Request
- Include correlationId

**Alternative:** Use built-in `InvalidModelStateResponseFactory` in ProblemDetails options

### 8. Test Coverage

**File:** `tests/Ledger.Tests/Api/Middleware/ExceptionHandlingMiddlewareTests.cs` (new)Test scenarios:

- Validation errors return 400 with reasonCode and correlationId
- Duplicate errors return 409 with reasonCode and correlationId
- Unauthorized errors return 401 with reasonCode and correlationId
- Not found errors return 404 with reasonCode and correlationId
- Generic exceptions return 500 with reasonCode and correlationId
- CorrelationId is included in all error responses
- ReasonCode is present in all error responses
- Error format matches RFC7807 structure

**Test Structure:**

- Unit tests for exception mapping logic
- Integration tests for middleware behavior
- Verify correlationId propagation
- Verify reasonCode assignment

### 9. Documentation Updates

**File:** `PROMPTS.md`Add new phase documenting:

- Exception handling middleware implementation
- ProblemDetails configuration
- Custom exception types
- Error code mapping strategy
- Test coverage approach

**File:** `docs/VALIDATION_MATRIX.md`Update to reflect:

- Error handling middleware enforcement
- Status code mapping rules
- ReasonCode usage

## Implementation Details

### ProblemDetails Structure

Standard RFC7807 format with extensions:

```json
{
  "type": "https://tools.ietf.org/html/rfc7231#section-6.5.1",
  "title": "Bad Request",
  "status": 400,
  "detail": "Validation failed: Account name is required",
  "instance": "/api/accounts",
  "reasonCode": "VALIDATION_ERROR",
  "correlationId": "550e8400-e29b-41d4-a716-446655440000"
}
```



### Exception Mapping Strategy

| Exception Type | HTTP Status | Reason Code Pattern ||----------------|-------------|---------------------|| ValidationException | 400 | VALIDATION_ERROR || ArgumentException | 400 | INVALID_ARGUMENT || NotFoundException | 404 | NOT_FOUND || ConflictException | 409 | CONFLICT_ *(specific) || UnauthorizedAccessException | 401 | UNAUTHORIZED || DbUpdateException (unique) | 409 | DUPLICATE_* || Generic Exception | 500 | INTERNAL_ERROR |

### Middleware Registration

```csharp
// In Program.cs
app.UseCorrelationId();
app.UseExceptionHandler(); // Built-in or custom
app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();
```



## File Structure

```javascript
src/Ledger.Api/
├── Middleware/
│   ├── CorrelationIdMiddleware.cs (existing, verify)
│   └── ExceptionHandlingMiddleware.cs (new)
├── Models/
│   └── ProblemDetailsExtensions.cs (new)
└── Program.cs (modify)

src/Ledger.Application/
└── Exceptions/
    ├── ValidationException.cs (new)
    ├── NotFoundException.cs (new)
    ├── ConflictException.cs (new)
    └── UnauthorizedException.cs (new)

tests/Ledger.Tests/
└── Api/
    └── Middleware/
        └── ExceptionHandlingMiddlewareTests.cs (new)
```



## Verification Steps

1. All exceptions return ProblemDetails format
2. All errors include reasonCode
3. All errors include correlationId
4. Status codes mapped correctly (400, 409, 401, etc.)
5. Error format consistent across all endpoints
6. Tests verify middleware behavior
7. CorrelationId propagates correctly
8. Development vs Production error detail levels