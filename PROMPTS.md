# AI Interaction & Decision Log

This document records all major prompts, architectural decisions,  
and execution steps performed using Cursor (AI-assisted development).

# Phase 0 – Governance & Guardrails

**Prompt:**  
"Design a production-grade double-entry general ledger microservice  
with fintech-level correctness, idempotency, and validation."

**Decisions:**
- Clean Architecture enforced
- PostgreSQL chosen as source of truth
- Money stored as `numeric(20,4)`
- Ledger is immutable after posting
- Double-entry enforced at application + DB layers
- Idempotency via `externalId` + `request_hash`
- Case-insensitive uniqueness for account names
- RFC7807 ProblemDetails for all errors
- Governance artifacts (.cursorrules, PROMPTS.md, VALIDATION_MATRIX.md) created before coding

# Phase 1 – Infrastructure & Scaffolding

**Prompt:**  
"Bootstrap a production-grade .NET 8 solution for a fintech general ledger.  
Requirements: Clean Architecture, Api/Domain/Application/Infrastructure/Tests projects,  
Dockerized PostgreSQL, no business logic in controllers, follow .cursorrules strictly."

**Decisions:**
- Solution structure: Ledger.sln with 5 projects (Api, Domain, Application, Infrastructure, Tests)
- Clean Architecture enforced via project references:
  - Domain: no dependencies
  - Application: references Domain only
  - Infrastructure: references Domain and Application
  - Api: references Application and Infrastructure
  - Tests: references all projects
- Directory.Build.props for common MSBuild settings
- .editorconfig for consistent code style

# Phase 1.1 – Infrastructure Foundation

**Prompt:**  
"Add infrastructure foundation: Docker compose for PostgreSQL, EF Core + Npgsql wiring,  
DbContext registration, health endpoint, CorrelationId middleware pattern.  
No domain entities or migrations yet."

**Decisions:**
- Docker Compose for local PostgreSQL 16 (port 5432)
  - Database: ledger
  - User: postgres
  - Password: admin (development only)
  - Health check configured
  - Data volume for persistence
- EF Core 8.0.11 with Npgsql.EntityFrameworkCore.PostgreSQL 8.0.11
- LedgerDbContext created in Infrastructure/Data layer
  - Constructor accepts DbContextOptions<LedgerDbContext>
  - Configured for PostgreSQL via options pattern
  - Sensitive data logging enabled in Development only
- Connection string stored in appsettings.json (ConnectionStrings:LedgerDb)
- Health checks endpoint at `/health`
  - Uses AddDbContextCheck<LedgerDbContext> for database connectivity validation
  - Returns 200 OK when healthy, 503 when unhealthy
- CorrelationId middleware pattern (X-Correlation-Id header)
  - Reads X-Correlation-Id from request header
  - Generates new GUID if header is missing
  - Stores correlation ID in HttpContext.Items["CorrelationId"]
  - Adds correlation ID to response headers
  - Registered early in middleware pipeline (before routing)
  - Ready for audit logging integration

# Phase 2.1 – Error Handling Standardization

**Prompt:**  
"Standardize error handling: RFC7807 ProblemDetails for all failures,  
include reasonCode and correlationId, map validation -> 400, duplicates -> 409, auth -> 401.  
Ensure consistent envelope across the API. Update PROMPTS.md and add test coverage."

**Decisions:**
- **ProblemDetailsExtensions** created for adding custom properties (reasonCode, correlationId)
- **Custom Exception Types** in Application layer:
  - ValidationException → 400 Bad Request
  - NotFoundException → 404 Not Found
  - ConflictException → 409 Conflict
  - UnauthorizedException → 401 Unauthorized
- **ExceptionHandlingMiddleware** implemented:
  - Catches all unhandled exceptions
  - Converts to RFC7807 ProblemDetails format
  - Maps exception types to appropriate HTTP status codes
  - Always includes reasonCode and correlationId
  - Handles database exceptions (DbUpdateException) with unique constraint detection
  - Logs exceptions with correlationId for traceability
- **Model Validation** configured:
  - InvalidModelStateResponseFactory returns ProblemDetails
  - Includes validation errors in extensions
  - Always includes reasonCode (VALIDATION_ERROR) and correlationId
  - Returns 400 Bad Request for validation failures
- **Status Code Mapping:**
  - Validation errors → 400 (VALIDATION_ERROR)
  - Not found → 404 (NOT_FOUND)
  - Conflicts/duplicates → 409 (CONFLICT_*, DUPLICATE_*)
  - Unauthorized → 401 (UNAUTHORIZED)
  - Generic errors → 500 (INTERNAL_ERROR)
- **CorrelationId Integration:**
  - Extracted from HttpContext.Items["CorrelationId"]
  - Generated if missing (GUID)
  - Included in all error responses
- **Test Coverage:**
  - Unit tests for all exception types and status code mappings
  - Tests verify reasonCode and correlationId presence
  - Tests verify ProblemDetails structure (RFC7807 compliance)
  - Tests verify correlationId generation when missing

# Phase 3 – Accounts API Implementation

**Prompt:**  
"Implement Accounts feature end-to-end: DTOs + validation, Application services/handlers for rules,  
Infrastructure repositories/DbContext access, Controllers thin only, Duplicate prevention uses DB constraint + friendly 409 mapping.  
Add tests: Unit tests for validations and business rules, Integration tests for DB uniqueness and update constraints.  
Update VALIDATION_MATRIX.md."

**Decisions:**
- **DTOs Created:**
  - `CreateAccountRequest`: Name (required, max 200), Type (required), IsActive (default true)
  - `UpdateAccountRequest`: IsActive only (type is immutable after usage)
  - `AccountResponse`: Full account details (Id, Name, Type, IsActive, CreatedAt, UpdatedAt)
  - Data annotations for API-level validation
- **Repository Pattern:**
  - `IAccountRepository` interface in Application layer
  - `AccountRepository` implementation in Infrastructure layer
  - Maintains Clean Architecture boundaries (Application doesn't reference Infrastructure)
  - Methods: GetByIdAsync, GetAllAsync, FindByNameAsync (case-insensitive), HasJournalLinesAsync, AddAsync, UpdateAsync
- **Service Layer:**
  - `IAccountService` interface and `AccountService` implementation
  - Validation logic: name trimming, length checks, empty after trim
  - Duplicate name checking (case-insensitive) before insert
  - Type immutability check (defensive, though PUT only allows IsActive changes)
  - Handles DbUpdateException for database constraint violations (backstop)
- **Controller Implementation:**
  - `AccountsController` with thin implementation (delegates to service)
  - Endpoints: POST /api/accounts, GET /api/accounts, GET /api/accounts/{id}, PUT /api/accounts/{id}
  - Returns appropriate HTTP status codes (201 Created, 200 OK, 404 Not Found)
  - Exception handling via middleware (automatic ProblemDetails conversion)
- **Duplicate Prevention Strategy:**
  - Application layer: Check for existing account with same name (case-insensitive) before insert
  - Database: Unique index (UPPER(Name)) as backstop
  - DbUpdateException handling → ConflictException with reasonCode "DUPLICATE_ACCOUNT_NAME"
  - Returns 409 Conflict with friendly error message
- **Timestamp Management:**
  - CreatedAt: Set to DateTime.UtcNow on creation
  - UpdatedAt: Set via EF Core Property API in repository UpdateAsync method
  - Database defaults also set (backstop)
- **Unit Tests:**
  - `AccountServiceTests.cs` covering:
    - Valid account creation
    - Name validation (empty, whitespace, too long)
    - Duplicate name detection (case-insensitive)
    - GetByIdAsync (found and not found)
    - GetAllAccountsAsync
    - UpdateAccountIsActiveAsync (success and not found)
    - Empty ID validation
  - Uses Moq for repository mocking
- **Integration Tests:**
  - `AccountsControllerTests.cs` using Testcontainers.PostgreSql
  - WebApplicationFactory for API testing
  - Test scenarios:
    - POST: valid request (201), duplicate name (409), duplicate case-insensitive (409), invalid name (400)
    - GET: list all accounts (200), get by ID (200), not found (404)
    - PUT: update IsActive (200), not found (404)
    - CorrelationId propagation
  - Runs migrations automatically in test setup
  - Cleans up container after tests
- **Dependency Injection:**
  - Registered `IAccountRepository` → `AccountRepository` (scoped)
  - Registered `IAccountService` → `AccountService` (scoped)
  - DbContext already registered in Program.cs
- **Error Responses:**
  - All errors use ProblemDetails format (via middleware)
  - 400: Validation errors (INVALID_NAME, INVALID_ACCOUNT_TYPE, INVALID_ACCOUNT_ID)
  - 404: Account not found (ACCOUNT_NOT_FOUND)
  - 409: Duplicate name (DUPLICATE_ACCOUNT_NAME)
  - All include reasonCode and correlationId

# Phase 2 – Domain & Persistence

**Prompt:**  
"Design and implement Domain entities: Account, JournalEntry, JournalEntryLine.  
Rules: No EF attributes in Domain, Enums for AccountType and LineDirection,  
Immutability expectations documented. Add basic domain invariants where appropriate,  
but keep cross-entity validation for Application layer."

**Decisions:**
- **Enums Created:**
  - `AccountType`: Asset, Liability, Equity, Revenue, Expense (explicit integer values)
  - `LineDirection`: Debit, Credit (explicit integer values)
- **BaseEntity Pattern:**
  - Abstract base class with `Id` (Guid), `CreatedAt` (DateTime), `UpdatedAt` (DateTime)
  - Uses `init` accessors to enforce immutability for creation-time fields
- **Account Entity:**
  - Properties: Name (string), Type (AccountType), IsActive (bool)
  - Inherits from BaseEntity
  - Domain invariant: Name cannot be null or empty (enforced in constructor)
  - `WithIsActive()` method for creating updated instance (immutability pattern)
  - Name uniqueness (case-insensitive) enforced at DB level
  - Type immutability after first usage enforced in Application layer
- **JournalEntry Entity:**
  - Properties: ExternalId (string?), RequestHash (string), PostedAt (DateTime)
  - Inherits from BaseEntity
  - Domain invariant: RequestHash cannot be null or empty (enforced in constructor)
  - **IMMUTABLE after posting** (when PostedAt is set)
  - Corrections via reversing entries only
  - ExternalId nullable for idempotency (unique at DB level when provided)
  - RequestHash used for payload mismatch detection
- **JournalEntryLine Entity:**
  - Properties: Id (Guid), JournalEntryId (Guid), AccountId (Guid), Direction (LineDirection), Amount (decimal)
  - Does not inherit from BaseEntity (lines don't need separate timestamps)
  - Domain invariants enforced in constructor:
    - Amount MUST be > 0
    - JournalEntryId and AccountId cannot be empty
    - Direction is exclusive (Debit OR Credit)
  - Immutable once created (part of immutable JournalEntry)
  - Cross-entity validation (debits == credits, account exists/active) in Application layer
- **Clean Architecture Compliance:**
  - No EF Core attributes in Domain entities
  - No navigation properties in Domain (Infrastructure responsibility)
  - No database-specific types or dependencies
  - Pure domain model with basic invariants only
- **Immutability Documentation:**
  - XML documentation comments on all entities
  - Immutability expectations clearly documented
  - Business rules (e.g., type change prevention) documented but enforced in Application layer

# Phase 4 – Journal Entry Posting

**Prompt:**  
"Implement Journal Entries end-to-end: DTOs and full validation pipeline,  
Canonicalization + SHA-256 request hash, DB-backed idempotency using unique externalId,  
On unique violation, fetch existing and compare request hash, Enforce atomic DB transaction.  
Add tests: Unit tests for balancing and validation rules, Integration tests for idempotency replay and payload mismatch,  
Concurrency integration test (parallel requests -> single insert). Update VALIDATION_MATRIX.md."

**Decisions:**
- **DTOs Created:**
  - `CreateJournalEntryRequest`: ExternalId (optional), Lines (min 2)
  - `CreateJournalEntryLineRequest`: AccountId, Direction, Amount
  - `JournalEntryResponse`: Full entry with lines and IdempotencyReplay flag
  - `JournalEntryLineResponse`: Line details
  - Application layer models (`CreateJournalEntryModel`, `CreateJournalEntryLineModel`) to maintain Clean Architecture
- **Request Hash Service:**
  - `IRequestHashService` and `RequestHashService` for SHA-256 canonical hashing
  - Canonical format: sorted lines (AccountId, Direction, Amount), compact JSON
  - Deterministic: same input always produces same hash
  - Handles null ExternalId correctly
- **Repository Pattern:**
  - `IJournalEntryRepository` interface in Application layer
  - `JournalEntryRepository` implementation in Infrastructure layer
  - `AddWithLinesAsync` uses explicit database transaction for atomicity
  - `FindByExternalIdAsync` for idempotency checks
  - `GetLinesByJournalEntryIdAsync` for loading entry with lines
- **Service Layer:**
  - `IJournalEntryService` and `JournalEntryService` implementation
  - Validation rules (in order):
    1. Line count >= 2
    2. Amount > 0 and scale <= 4 decimal places
    3. Valid LineDirection enum
    4. All accounts exist and are active
    5. Balance validation (debits == credits, exact decimal match)
  - Idempotency logic:
    - If ExternalId provided: check for existing entry
    - Compare request hashes
    - Same hash → return existing (200 OK, IsIdempotencyReplay = true)
    - Different hash → throw ConflictException (409)
  - Handles DbUpdateException for concurrent inserts (fetch-on-conflict pattern)
  - Atomic transaction ensures entry + lines are written together
- **Controller Implementation:**
  - `JournalEntriesController` with thin implementation
  - POST /api/journal-entries: returns 201 Created for new entries, 200 OK for idempotent replays
  - GET /api/journal-entries/{id}: returns entry with all lines
  - Maps Application models to/from API DTOs
- **Idempotency Strategy:**
  - Application layer: Check for existing entry with same ExternalId before insert
  - Compute request hash (SHA-256 of canonical JSON)
  - Compare hashes: same → replay, different → conflict
  - Database: Unique partial index on ExternalId as backstop
  - On DbUpdateException: fetch existing entry and compare hash (fetch-on-conflict)
  - No pre-check patterns (race-condition safe)
- **Atomic Transaction:**
  - Uses `DbContext.Database.BeginTransactionAsync()`
  - Wraps entry + lines creation in single transaction
  - Commits after successful SaveChangesAsync
  - Rolls back on any exception
  - Ensures all-or-nothing behavior
- **Unit Tests:**
  - `RequestHashServiceTests.cs`: deterministic hashing, canonical format, null handling
  - `JournalEntryServiceTests.cs`: all validation scenarios, balance checks, idempotency logic
  - Uses Moq for repository mocking
- **Integration Tests:**
  - `JournalEntriesControllerTests.cs` using Testcontainers.PostgreSql
  - Test scenarios:
    - Valid entry creation (201)
    - Less than 2 lines (400)
    - Unbalanced entry (400)
    - Idempotent replay (200 OK with IdempotencyReplay = true)
    - Idempotency conflict (409)
    - Get entry by ID (200, 404)
    - Exact decimal balance validation
  - Runs migrations automatically
- **Concurrency Test:**
  - `JournalEntriesConcurrencyTests.cs`: parallel requests with same ExternalId
  - Verifies only one entry is created
  - Verifies all requests return 200 OK with same entry ID
  - Verifies database constraint prevents duplicates
  - Uses Task.WhenAll for parallel execution
- **Error Responses:**
  - All errors use ProblemDetails format (via middleware)
  - 400: Validation errors (INVALID_LINE_COUNT, INVALID_AMOUNT, INVALID_AMOUNT_SCALE, INVALID_DIRECTION, ACCOUNT_NOT_FOUND, ACCOUNT_INACTIVE, UNBALANCED_ENTRY)
  - 409: Idempotency conflict (DUPLICATE_EXTERNAL_ID)
  - 200: Idempotent replay (with IdempotencyReplay flag)
  - All include reasonCode and correlationId

# Phase 5 – Trial Balance Reporting

**Prompt:**  
"Implement trial balance: Efficient DB aggregation query including zero-activity accounts (LEFT JOIN),  
Ensure each account appears once, Validate total net equals 0 (fail loudly if not).  
Add integration tests: zero-activity inclusion, net correctness, total net == 0, asOf filter correctness.  
Update VALIDATION_MATRIX.md."

**Decisions:**
- **DTOs Created:**
  - `TrialBalanceResponse`: AsOf (nullable), Items (list), TotalNet
  - `TrialBalanceItemResponse`: AccountId, AccountName, AccountType, TotalDebits, TotalCredits, Net
  - Application models (`TrialBalanceItem`, `TrialBalanceResult`) to maintain Clean Architecture
- **Repository Pattern:**
  - `ITrialBalanceRepository` interface in Application layer
  - `TrialBalanceRepository` implementation in Infrastructure layer
  - Efficient LEFT JOIN query to include all accounts (even zero-activity)
  - Aggregation using LINQ with GROUP BY
  - Filters by `JournalEntry.PostedAt <= asOf` (if asOf provided)
  - Orders results by AccountName for consistent output
- **Query Strategy:**
  - LEFT JOIN Accounts with JournalEntryLines (via JournalEntries)
  - Filter lines by PostedAt <= asOf (if provided)
  - Group by Account (Id, Name, Type)
  - Aggregate: SUM(CASE WHEN Direction = Debit THEN Amount ELSE 0) as TotalDebits
  - Aggregate: SUM(CASE WHEN Direction = Credit THEN Amount ELSE 0) as TotalCredits
  - Calculate Net = TotalDebits - TotalCredits
  - Use DefaultIfEmpty() for LEFT JOIN behavior
  - AsNoTracking() for read-only query
- **Service Layer:**
  - `ITrialBalanceService` and `TrialBalanceService` implementation
  - Calls repository to get trial balance items
  - Calculates TotalNet = sum of all Net values
  - **Validates TotalNet == 0** (throws ValidationException if not)
  - Error code: "UNBALANCED_TRIAL_BALANCE" (400 Bad Request)
  - This ensures data integrity (double-entry accounting must balance)
- **Controller Implementation:**
  - `ReportsController` with GET /api/reports/trial-balance endpoint
  - Accepts optional `asOf` query parameter (DateTime)
  - Returns 200 OK with `TrialBalanceResponse`
  - Returns 400 Bad Request if trial balance is unbalanced
  - Maps Application models to API DTOs
- **Integration Tests:**
  - `TrialBalanceControllerTests.cs` using Testcontainers.PostgreSql
  - Test scenarios:
    - No journal entries: all accounts with zero net
    - With journal entries: correct balances
    - Each account appears exactly once (no duplicates)
    - Zero-activity accounts included with net=0
    - TotalNet equals 0 (validated)
    - Net calculation correctness (debits - credits)
    - asOf filtering works correctly
    - Accounts ordered by name
    - Multiple entries aggregated correctly
  - Creates test accounts and journal entries
  - Verifies aggregation and balance validation
- **Edge Cases Handled:**
  - No accounts exist → empty list, TotalNet = 0
  - No journal entries → all accounts with net=0, TotalNet = 0
  - All accounts have zero activity → all nets = 0, TotalNet = 0
  - asOf before any entries → all accounts with net=0
  - asOf after all entries → includes all entries
  - Multiple entries for same account → correct aggregation
- **Error Responses:**
  - 400 Bad Request: Unbalanced trial balance (UNBALANCED_TRIAL_BALANCE)
  - All errors use ProblemDetails format (via middleware)
  - All include reasonCode and correlationId

# Phase 6 – Testing & Hardening

**Prompt:**  
"Add full unit and integration test coverage for ledger invariants,  
duplicates, idempotency, and reporting correctness."

**Decisions:**
- Unit tests for validation logic and domain invariants
- PostgreSQL integration tests via Testcontainers
- Explicit concurrency tests for idempotency behavior
- VALIDATION_MATRIX.md maintained as authoritative checklist

# Phase 6 – JWT Authentication (Simple)

**Prompt:**  
"Implement simple JWT auth:
- Add JWT Bearer auth and strict validation
- Apply [Authorize] globally or at controller level for all endpoints
- Standardize 401 responses via ProblemDetails with reasonCode=UNAUTHORIZED and correlationId
Add tests:
- missing token -> 401
- invalid token -> 401
- expired token -> 401
Update PROMPTS.md and VALIDATION_MATRIX.md.
Do not add roles or policies."

**Decisions:**
- **JWT Configuration:**
  - Created `JwtSettings` configuration class in `Ledger.Api/Configuration/`
  - Added JWT settings to `appsettings.json` and `appsettings.Development.json`
  - Configuration includes: Issuer, Audience, SecretKey, ClockSkewSeconds
  - SecretKey must be at least 32 characters (validated at startup)
  - Configuration can be overridden via environment variables (JWT__SECRETKEY, etc.)
- **JWT Authentication Setup:**
  - Added `Microsoft.AspNetCore.Authentication.JwtBearer` package (version 8.0.11)
  - Configured JWT Bearer authentication in `Program.cs`
  - TokenValidationParameters configured with strict validation:
    - ValidateIssuer = true
    - ValidateAudience = true
    - ValidateLifetime = true
    - ValidateIssuerSigningKey = true
    - RequireExpirationTime = true
    - RequireSignedTokens = true
    - ClockSkew configurable (default 300 seconds, 60 seconds in development)
  - Added `UseAuthentication()` middleware before `UseAuthorization()`
- **Authentication Challenge Response:**
  - Configured `JwtBearerEvents.OnChallenge` to return RFC7807 ProblemDetails
  - All 401 responses include:
    - Status: 401
    - Title: "Unauthorized"
    - Detail: "Authentication required. Please provide a valid JWT Bearer token."
    - reasonCode: "UNAUTHORIZED"
    - correlationId: from HttpContext.Items
    - Content-Type: "application/problem+json"
- **Controller Authorization:**
  - Added `[Authorize]` attribute to all controllers:
    - `AccountsController`
    - `JournalEntriesController`
    - `ReportsController`
  - Health check endpoint (`/health`) remains public (no authentication required)
- **Exception Handling:**
  - Updated `ExceptionHandlingMiddleware` to handle JWT security token exceptions:
    - `SecurityTokenExpiredException` → 401 with reasonCode "TOKEN_EXPIRED"
    - `SecurityTokenInvalidSignatureException` → 401 with reasonCode "TOKEN_INVALID_SIGNATURE"
    - `SecurityTokenInvalidIssuerException` → 401 with reasonCode "TOKEN_INVALID_ISSUER"
    - `SecurityTokenInvalidAudienceException` → 401 with reasonCode "TOKEN_INVALID_AUDIENCE"
    - `SecurityTokenException` (generic) → 401 with reasonCode "TOKEN_INVALID"
- **Swagger/OpenAPI Configuration:**
  - Added JWT Bearer security definition to Swagger
  - Added security requirement to all endpoints
  - Enables "Authorize" button in Swagger UI for testing with JWT tokens
- **Test Infrastructure:**
  - Created `TestJwtTokenHelper` class for generating test tokens:
    - `GenerateToken()` - valid token
    - `GenerateExpiredToken()` - expired token
    - `GenerateTokenWithInvalidSignature()` - wrong secret key
    - `GenerateTokenWithWrongIssuer()` - wrong issuer
    - `GenerateTokenWithWrongAudience()` - wrong audience
    - `GenerateTokenNotYetValid()` - nbf in future
- **Integration Tests:**
  - Created `AuthenticationTests.cs` with comprehensive test coverage:
    - Missing token → 401 Unauthorized
    - Valid token → 200 OK
    - Expired token → 401 Unauthorized
    - Invalid token format → 401 Unauthorized
    - Invalid signature → 401 Unauthorized
    - Tests for all endpoints (Accounts, JournalEntries, Reports)
    - Health check remains public (no auth required)
    - CorrelationId included in 401 responses
  - Tests use `WebApplicationFactory` with test JWT configuration
  - Tests use `Testcontainers.PostgreSql` for database isolation
- **No Roles or Policies:**
  - Simple authentication only - no authorization policies
  - Valid JWT grants access to all endpoints
  - No role-based or permission-based access control

# Phase 7 – User Management & Login

**Prompt:**  
"Implement users and login end-to-end:
- Add User entity and EF mapping + migration
- Implement user creation with validations and hashing
- Implement login with secure verification and generic error messages
- Return JWT on successful login
- Update failed login count / last login time
- Enforce disabled user cannot login
Add tests:
- unit tests for hashing/verification
- integration tests for create/login/duplicate/disabled
Update VALIDATION_MATRIX.md and PROMPTS.md."

**Decisions:**
- **User Domain Entity:**
  - Created `User` entity inheriting from `BaseEntity`
  - Properties: Email (unique case-insensitive), PasswordHash, IsEnabled, FailedLoginAttempts, LastLoginAt
  - Immutability pattern with `With*` methods for updates
- **EF Core Configuration:**
  - Created `UserConfiguration` with case-insensitive unique index on Email (UPPER)
  - PasswordHash max length 200 (BCrypt hashes are ~60 chars)
  - IsEnabled default true, FailedLoginAttempts default 0
  - UTC timestamps with defaults
- **Database Migration:**
  - Generated `AddUserEntity` migration
  - Includes case-insensitive unique index: `CREATE UNIQUE INDEX "IX_Users_Email_Normalized" ON "Users" (UPPER("Email"));`
- **Password Hashing:**
  - Added `BCrypt.Net-Next` package (version 2.0.0)
  - Created `IPasswordHasher` and `PasswordHasher` service
  - BCrypt work factor 12 (adaptive hashing with salt)
  - Timing-safe verification (BCrypt handles internally)
- **JWT Token Generation:**
  - Created `IJwtTokenService` and `JwtTokenService`
  - Uses `JwtTokenSettings` (separate from API `JwtSettings` to maintain Clean Architecture)
  - Token includes claims: sub (userId), email, jti (unique token ID)
  - Token expiration: 1 hour
  - Tokens signed with same secret key as JWT validation
- **User Repository:**
  - Created `IUserRepository` interface and `UserRepository` implementation
  - Case-insensitive email lookup using EF Core ToUpper
  - Methods: GetByIdAsync, FindByEmailAsync, AddAsync, UpdateAsync
- **User Service:**
  - Created `IUserService` and `UserService`
  - `CreateUserAsync`: Validates email format, password strength (min 8 chars, letter + number), checks duplicates, hashes password
  - `AuthenticateAsync`: Timing-safe password verification, checks IsEnabled, updates FailedLoginAttempts and LastLoginAt
  - Generic error on authentication failure (no user enumeration)
  - Always performs password verification even if user not found (timing-safe)
- **Authentication Service:**
  - Created `IAuthenticationService` and `AuthenticationService`
  - `LoginAsync`: Orchestrates authentication and JWT token generation
  - Returns `LoginResult` with token, userId, email (no password hash)
  - Throws `UnauthorizedException` with generic message on failure
- **DTOs:**
  - `CreateUserRequest`: Email, Password (with validation attributes)
  - `UserResponse`: Id, Email, IsEnabled, CreatedAt, UpdatedAt (NO PasswordHash)
  - `LoginRequest`: Email, Password
  - `LoginResponse`: Token, UserId, Email
- **Controllers:**
  - `UsersController`: POST /api/users ([AllowAnonymous] - public registration)
  - `AuthController`: POST /api/auth/login ([AllowAnonymous] - public login)
  - All error responses use ProblemDetails with reasonCode and correlationId
- **Dependency Injection:**
  - Registered all services in `Program.cs`:
    - IPasswordHasher, IUserRepository, IUserService, IAuthenticationService, IJwtTokenService
  - Configured `JwtTokenSettings` from `JwtSettings` (mapped in Program.cs)
- **Unit Tests:**
  - `PasswordHasherTests`: Hash/verify, timing-safe verification, error handling
  - `JwtTokenServiceTests`: Token generation, claims validation, token validity
  - `UserServiceTests`: Email/password validation, duplicate check, authentication, disabled user, no enumeration
  - `AuthenticationServiceTests`: Login success/failure, token generation
- **Integration Tests:**
  - `UsersControllerTests`: User creation, duplicate email (case-insensitive), validation errors, password hash not in response
  - `AuthControllerTests`: Login success/failure, token validity, disabled user, LastLoginAt update, FailedLoginAttempts tracking, no user enumeration
- **Security Features:**
  - Passwords hashed with BCrypt (never stored or returned in plain text)
  - Timing-safe password verification (prevents user enumeration)
  - Generic error messages on login failure ("Invalid email or password")
  - Email uniqueness enforced at DB level (case-insensitive)
  - Disabled users cannot log in
  - Failed login attempts tracked and reset on success
  - Last login timestamp updated on successful login

# Phase 9 – Audit Logging (Financial Compliance)

**Prompt:**  
"Add compliance-grade audit logging with EF Core.

Requirements:
- Append-only AuditLogs table (immutable)
- Capture: entity name, entity id, action, old values, new values, performedBy userId, correlationId, timestamp UTC
- Old/New stored as JSON
- Exclude sensitive fields (passwords/hashes/secrets)
- Implement via EF Core SaveChanges interceptor or Unit of Work hook
- Audit logs written in same transaction where applicable
- If audit logging fails, transaction must fail

Audit must cover:
- Account create/update
- Journal entry posting
- User creation
- Login

Add tests and update PROMPTS.md and VALIDATION_MATRIX.md."

**Decisions:**
- **AuditLog Domain Entity:**
  - Created `AuditLog` entity with immutable properties (init-only)
  - Properties: Id, EntityName, EntityId, Action, OldValues (JSON), NewValues (JSON), PerformedBy (nullable), CorrelationId, Timestamp
  - Constructor validates required fields
- **EF Core Configuration:**
  - Created `AuditLogConfiguration` with JSONB columns for OldValues/NewValues
  - Timestamp with UTC timezone and default
  - Table comment indicating append-only behavior
- **Database Migration:**
  - Generated `AddAuditLogsTable` migration
  - JSONB columns for efficient JSON storage and querying
- **Sensitive Field Exclusion:**
  - Created `SensitiveFieldExcluder` utility class
  - Excludes: PasswordHash, Password, Secret, SecretKey, Token, ApiKey, AccessToken, RefreshToken
  - Case-insensitive field name matching
  - Serializes to JSON with camelCase naming
- **User Context Service:**
  - Created `IUserContextService` and `UserContextService`
  - Extracts userId from JWT claims (ClaimTypes.NameIdentifier or "sub")
  - Returns null if no authenticated user
- **Audit Log Service:**
  - Created `IAuditLogService` and `AuditLogService`
  - `LogEntityChangeAsync`: Serializes old/new values (excluding sensitive fields) and creates audit log entry
- **EF Core Interceptor:**
  - Created `AuditLoggingInterceptor` extending `SaveChangesInterceptor`
  - Captures Added and Modified entity states
  - Skips AuditLog entities (prevents recursion)
  - Extracts correlationId from HttpContext.Items
  - Gets performedBy from UserContextService
  - Creates audit log entries directly in same DbContext (same transaction)
  - Serializes old values (for Modified) and new values (for Added/Modified)
- **Login Audit:**
  - `AuthenticationService` explicitly logs LOGIN action after successful authentication
  - Captures LastLoginAt timestamp in newValues
- **Dependency Injection:**
  - Registered `IHttpContextAccessor`, `IUserContextService`, `IAuditLogRepository`, `IAuditLogService`
  - Registered interceptor in DbContext configuration
- **Unit Tests:**
  - `SensitiveFieldExcluderTests`: Excludes sensitive fields, preserves non-sensitive, case-insensitive matching
  - `UserContextServiceTests`: Extracts userId from JWT claims, handles missing context/claims
- **Integration Tests:**
  - `AuditLoggingTests`: Account create/update, journal entry posting, user creation (password exclusion), login, correlationId, userId, transaction safety
  - Tests verify audit logs are created, sensitive fields excluded, correlationId captured, performedBy set correctly
- **Transaction Safety:**
  - Audit logs added to same DbContext before SaveChanges
  - If audit logging fails, entire transaction rolls back
  - No separate transaction for audit logs (ensures atomicity)

# Phase 10 – Production Readiness Hardening

**Prompt:**  
"Perform production readiness hardening:
- Add README with setup and usage
- Validate all constraints and error formats
- Ensure test coverage for all major rules
- Add any missing edge-case tests (duplicates, idempotency mismatch, audit coverage)
- Final lint/format cleanup
Update PROMPTS.md and VALIDATION_MATRIX.md to reflect final state."

**Decisions:**
- **README.md Created:**
  - Comprehensive setup instructions (local dev, migrations, authentication)
  - Example API requests with curl commands
  - Configuration guide (connection strings, JWT settings, environment variables)
  - Architecture overview (Clean Architecture layers, design decisions)
  - Troubleshooting section
  - Security considerations
- **Database Constraint Tests:**
  - Created `DatabaseConstraintTests` integration test class
  - Tests verify database-level enforcement:
    - Negative amount violates CHECK constraint
    - Zero amount violates CHECK constraint
    - Duplicate account name (case-insensitive) violates unique index
    - Duplicate externalId violates unique partial index
  - Tests bypass application validation to verify database backstop
- **Idempotency Mismatch Tests:**
  - Created `IdempotencyMismatchTests` integration test class
  - Tests verify 409 Conflict when same externalId used with different payloads:
    - Different amounts
    - Different accounts
    - Different line counts
  - Tests verify 200 OK replay when same externalId + same payload
- **CorrelationId Propagation Tests:**
  - Created `CorrelationIdPropagationTests` integration test class
  - Tests verify CorrelationId:
    - Included in success response headers
    - Included in error responses (ProblemDetails)
    - Generated if not provided
    - Captured in audit logs
    - Included in validation error responses
- **AccountConfiguration Documentation:**
  - Added comment explaining case-insensitive unique index implementation
  - Notes that migration uses raw SQL (UPPER) while EF Core config uses standard index
  - Documents that application layer also enforces case-insensitive uniqueness
- **Test Coverage Verification:**
  - All major validation rules have integration tests
  - Database constraints tested at database level
  - Idempotency scenarios fully covered
  - Audit logging comprehensively tested
  - CorrelationId propagation verified end-to-end
  - Authentication/authorization tested for all endpoints
- **Documentation Updates:**
  - VALIDATION_MATRIX.md reviewed and verified complete
  - PROMPTS.md updated with Phase 10 details
  - All validation rules marked with test coverage status

# End of Log