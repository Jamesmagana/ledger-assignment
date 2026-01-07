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
- Docker Compose for local PostgreSQL 16 (port 5432)
- Serilog for structured logging (Console + File)
- CorrelationId middleware pattern (X-Correlation-Id header)
- EF Core 8.0 with Npgsql.EntityFrameworkCore.PostgreSQL 8.0.0
- Health checks endpoint at `/health`
- ProblemDetails configured for RFC7807 compliance
- Testcontainers.PostgreSql for integration tests
- Directory.Build.props for common MSBuild settings
- .editorconfig for consistent code style

# Phase 2 – Domain & Persistence

**Prompt:**  
"Model Accounts, JournalEntries, and JournalEntryLines with strict  
ledger invariants and EF Core mappings."

**Decisions:**
- JournalEntry modeled as immutable header + lines
- Domain entities free of EF attributes
- DB-level uniqueness and CHECK constraints used as integrity backstop
- No cascade deletes on ledger data
- UTC timestamps enforced at all layers

# Phase 3 – Accounts API

**Prompt:**  
"Implement Chart of Accounts with duplicate prevention,  
case-insensitive uniqueness, and validation."

**Decisions:**
- Account names normalized for case-insensitive uniqueness
- Duplicate account names rejected with 409 Conflict
- Account type cannot change after first usage
- Account activation state validated before journal posting

# Phase 4 – Journal Entry Posting

**Prompt:**  
"Implement idempotent journal posting with concurrency safety,  
payload mismatch detection, and atomic transactions."

**Decisions:**
- Canonical request hashing using SHA-256
- DB unique constraint on `externalId` as idempotency source of truth
- Same `externalId` + same payload → replay (200 OK)
- Same `externalId` + different payload → 409 Conflict
- No pre-check insert patterns (race-condition safe)
- JournalEntry and lines persisted atomically in a single DB transaction

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