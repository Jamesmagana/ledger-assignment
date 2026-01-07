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
  - User: ledger_user
  - Password: ledger_password (development only)
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

# Phase 5 – Financial Reporting

**Prompt:**  
"Implement trial balance report ensuring correctness,  
no duplicate accounts, and zero-net totals."

**Decisions:**
- LEFT JOIN strategy to include zero-activity accounts
- Debit/Credit aggregation performed at DB level
- Each account appears exactly once
- Total net balance validated to equal zero
- Optional `asOf` filter supported

# Phase 6 – Testing & Hardening

**Prompt:**  
"Add full unit and integration test coverage for ledger invariants,  
duplicates, idempotency, and reporting correctness."

**Decisions:**
- Unit tests for validation logic and domain invariants
- PostgreSQL integration tests via Testcontainers
- Explicit concurrency tests for idempotency behavior
- VALIDATION_MATRIX.md maintained as authoritative checklist

# Phase 7 – JWT Authentication (Simple)

**Prompt:**  
"Design and implement simple JWT authentication  
to protect a financial ledger API."

**Decisions:**
- JWT Bearer authentication enforced for all endpoints
- JWT used strictly for authentication (no roles, no permissions)
- Valid JWT → access allowed
- Missing or invalid JWT → 401 Unauthorized
- Strict validation of:
  - Signature
  - Issuer
  - Audience
  - Expiration
  - NotBefore
- No hardcoded secrets; configuration-based keys
- Clock skew explicitly configured
- Authentication treated as mandatory security boundary

# Phase 8 – User Management & Login

**Prompt:**  
"Design and implement secure user creation and login  
for a financial system."

**Decisions:**
- Users persisted in database via EF Core
- Email used as unique identifier (case-insensitive)
- Email uniqueness enforced at DB level
- Passwords hashed using strong adaptive cryptographic hashing
- Timing-safe password comparison
- Duplicate users rejected with 409 Conflict
- Invalid login returns generic error (no user enumeration)
- Failed login attempts tracked
- User enable/disable supported (soft state)
- Successful login updates last login timestamp
- Login events treated as auditable actions

# Phase 9 – Audit Logging (Financial Compliance)

**Prompt:**  
"Design an audit logging system capturing old and new values  
for all financial and security-sensitive operations."

**Decisions:**
- Append-only audit log table (immutable)
- Audit entries capture:
  - Entity name and ID
  - Action (CREATE, UPDATE, LOGIN, POST)
  - OldValues and NewValues (JSON)
  - PerformedBy user
  - CorrelationId
  - UTC timestamp
- Audit logs written via EF Core interception or Unit of Work
- Audit logging included in the same transaction where applicable
- Sensitive fields (passwords, secrets) explicitly excluded
- Audit logging failures cause transaction failure (financial safety)

# End of Log