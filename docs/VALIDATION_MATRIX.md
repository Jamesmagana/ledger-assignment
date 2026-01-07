# Ledger Validation Matrix
End-to-End Validation Coverage

==================================================
ACCOUNTS API
==================================================

| Rule                                | Layer     | Enforcement                  | Error Code               | Test         | Status      |
|-------------------------------------|-----------|------------------------------|--------------------------|--------------|-------------|
| Name required                       | API       | DTO validation               | INVALID_NAME             | Unit         | ✅ Implemented |
| Name length limits                  | API       | DTO validation               | INVALID_NAME             | Unit         | ✅ Implemented |
| Name trimmed                        | App       | Service validation           | INVALID_NAME             | Unit         | ✅ Implemented |
| Name uniqueness (case-insensitive)  | DB + App  | Unique index (UPPER) + check | DUPLICATE_ACCOUNT_NAME   | Integration  | ✅ Implemented |
| Valid AccountType enum              | API       | Enum validation              | INVALID_ACCOUNT_TYPE     | Unit         | ✅ Implemented |
| Prevent type change after usage     | App       | Business rule                | ACCOUNT_TYPE_IMMUTABLE   | Integration  | ✅ Implemented |
| Account exists                      | App       | Lookup                       | ACCOUNT_NOT_FOUND        | Unit + Integration | ✅ Implemented |
| Account active (IsActive update)    | App       | Service method               | N/A                      | Integration  | ✅ Implemented |
| UpdatedAt timestamp on update       | Infra     | Repository update            | N/A                      | Integration  | ✅ Implemented |

==================================================
JOURNAL ENTRY POSTING
==================================================

| Rule                     | Layer     | Enforcement                  | Error Code             | Test                | Status      |
|--------------------------|-----------|------------------------------|------------------------|---------------------|-------------|
| Minimum 2 lines          | App       | Validation                   | INVALID_LINE_COUNT     | Unit                | ✅ Implemented |
| Amount > 0               | API + DB  | Validator + CHECK constraint | INVALID_AMOUNT         | Integration         | ✅ Implemented |
| Decimal scale ≤ 4         | App       | Validation                   | INVALID_AMOUNT_SCALE   | Unit                | ✅ Implemented |
| Debit OR Credit only     | App       | Enum validation              | INVALID_DIRECTION      | Unit                | ✅ Implemented |
| Accounts exist           | App       | Lookup                       | ACCOUNT_NOT_FOUND      | Integration         | ✅ Implemented |
| Account active           | App       | Lookup                       | ACCOUNT_INACTIVE       | Integration         | ✅ Implemented |
| Debits == Credits        | App       | Pre-commit validation        | UNBALANCED_ENTRY       | Unit + Integration  | ✅ Implemented |
| Atomic write             | DB        | Transaction                  | N/A                    | Integration         | ✅ Implemented |
| Idempotency (same hash)  | App + DB  | Hash comparison + unique index | IDEMPOTENCY_REPLAY (200 OK) | Integration | ✅ Implemented |
| Idempotency (diff hash)  | App + DB  | Hash comparison + unique index | DUPLICATE_EXTERNAL_ID (409) | Integration | ✅ Implemented |
| Concurrency safety       | DB        | Unique constraint + fetch-on-conflict | N/A | Integration | ✅ Implemented |
| Request hash canonical   | App       | SHA-256 of sorted JSON       | N/A                    | Unit                | ✅ Implemented |

==================================================
IDEMPOTENCY & DUPLICATES
==================================================

| Scenario                           | Enforcement | Result           | Test        |
|------------------------------------|-------------|------------------|-------------|
| Same externalId, same payload      | DB + App    | 200 replay       | Integration |
| Same externalId, different payload | App         | 409 conflict     | Integration |
| Concurrent same externalId         | DB          | Unique partial index| Integration |
| Duplicate prevention               | DB          | Unique partial index (WHERE ExternalId IS NOT NULL)| Integration |

==================================================
TRIAL BALANCE REPORT
==================================================

| Rule                              | Layer    | Enforcement   | Test        | Status      |
|-----------------------------------|----------|---------------|-------------|-------------|
| Each account appears once         | DB query | Grouping      | Integration | ✅ Implemented |
| Zero-activity accounts included   | DB query | LEFT JOIN     | Integration | ✅ Implemented |
| Net = debits - credits            | DB query | Aggregate     | Integration | ✅ Implemented |
| Total net equals 0                | App      | Validation    | Integration | ✅ Implemented |
| asOf filter correctness           | DB query | WHERE clause  | Integration | ✅ Implemented |

==================================================
JWT AUTHENTICATION (SIMPLE)
==================================================

| Rule                              | Layer | Enforcement            | Error Code     | Test         | Status       |
|-----------------------------------|-------|------------------------|----------------|--------------|--------------|
| JWT required for all endpoints    | API   | Auth middleware        | UNAUTHORIZED   | Integration  | ✅ Implemented |
| Missing token rejected            | API   | JWT handler            | UNAUTHORIZED   | Integration  | ✅ Implemented |
| Invalid signature rejected        | API   | JWT validation         | UNAUTHORIZED   | Integration  | ✅ Implemented |
| Expired token rejected            | API   | JWT validation         | UNAUTHORIZED   | Integration  | ✅ Implemented |
| Invalid issuer rejected           | API   | JWT validation         | UNAUTHORIZED   | Integration  | ✅ Implemented |
| Invalid audience rejected         | API   | JWT validation         | UNAUTHORIZED   | Integration  | ✅ Implemented |
| Clock skew configured             | API   | TokenValidationParams  | N/A            | Integration  | ✅ Implemented |
| 401 returns ProblemDetails        | API   | Challenge event        | UNAUTHORIZED   | Integration  | ✅ Implemented |
| CorrelationId on auth failures    | API   | Middleware             | N/A            | Integration  | ✅ Implemented |
| Health check public (no auth)      | API   | No [Authorize]         | N/A            | Integration  | ✅ Implemented |
| JWT required for all endpoints    | API   | Auth middleware        | UNAUTHORIZED   | Integration  |
| Missing token rejected            | API   | JWT handler            | UNAUTHORIZED   | Integration  |
| Invalid signature rejected        | API   | JWT validation         | UNAUTHORIZED   | Unit         |
| Expired token rejected            | API   | JWT validation         | UNAUTHORIZED   | Unit         |
| Invalid issuer/audience rejected  | API   | JWT validation         | UNAUTHORIZED   | Unit         |
| CorrelationId on auth failures    | API   | Middleware             | N/A            | Unit         |

==================================================
USER CREATION
==================================================

| Rule                              | Layer     | Enforcement              | Error Code         | Test         | Status       |
|-----------------------------------|-----------|--------------------------|--------------------|--------------|--------------|
| Email required                    | API       | DTO validation           | INVALID_EMAIL      | Unit         | ✅ Implemented |
| Valid email format                | API       | Validation               | INVALID_EMAIL      | Unit         | ✅ Implemented |
| Email uniqueness (case-insensitive)| DB + App | Unique index             | DUPLICATE_USER     | Integration  | ✅ Implemented |
| Password required                 | API       | DTO validation           | INVALID_PASSWORD   | Unit         | ✅ Implemented |
| Password strength enforced        | App       | Business rule            | WEAK_PASSWORD      | Unit         | ✅ Implemented |
| Password hashed                   | App       | Hashing service          | N/A                | Unit         | ✅ Implemented |
| No password returned              | API       | Response contract        | N/A                | Integration  | ✅ Implemented |
| User enabled by default           | App       | Default state            | N/A                | Unit         | ✅ Implemented |

==================================================
USER LOGIN
==================================================

| Rule                              | Layer     | Enforcement              | Error Code           | Test         | Status       |
|-----------------------------------|-----------|--------------------------|----------------------|--------------|--------------|
| Email required                    | API       | DTO validation           | INVALID_CREDENTIALS  | Unit         | ✅ Implemented |
| Password required                 | API       | DTO validation           | INVALID_CREDENTIALS  | Unit         | ✅ Implemented |
| Invalid credentials generic error | App       | Auth logic               | INVALID_CREDENTIALS  | Integration  | ✅ Implemented |
| No user enumeration               | App       | Timing-safe verification | INVALID_CREDENTIALS  | Integration  | ✅ Implemented |
| Disabled user blocked             | App       | State check              | INVALID_CREDENTIALS  | Integration  | ✅ Implemented |
| Failed login count tracked        | App       | State update             | N/A                  | Integration  | ✅ Implemented |
| Successful login updates timestamp| App       | State update             | N/A                  | Integration  | ✅ Implemented |
| JWT issued on success             | App       | Token generation         | N/A                  | Integration  | ✅ Implemented |
| Timing-safe password verification | App       | BCrypt verification      | N/A                  | Unit         | ✅ Implemented |

==================================================
AUDIT LOGGING
==================================================

| Rule                              | Layer | Enforcement                  | Test         |
|-----------------------------------|-------|------------------------------|--------------|
| Audit on entity CREATE            | Infra | EF Core interceptor          | Integration  |
| Audit on entity UPDATE            | Infra | EF Core interceptor          | Integration  |
| Audit on journal POST             | Infra | EF Core interceptor          | Integration  |
| Audit on user creation            | Infra | EF Core interceptor          | Integration  |
| Audit on login                    | Infra | EF Core interceptor          | Integration  |
| Old values captured               | Infra | Change tracking              | Unit         |
| New values captured               | Infra | Change tracking              | Unit         |
| Sensitive fields excluded         | Infra | Field filter                 | Unit         |
| PerformedBy user captured         | API   | JWT context                  | Integration  |
| CorrelationId captured            | API   | Middleware                   | Integration  |
| Append-only behavior              | DB    | No update/delete paths       | Integration  |
| Audit failure blocks transaction  | DB    | Transaction rollback         | Integration  |

==================================================
DATABASE CONSTRAINTS (BACKSTOP)
==================================================

| Rule                              | Layer | Enforcement                  | Test         |
|-----------------------------------|-------|------------------------------|--------------|
| Account name case-insensitive unique| DB   | Unique index (UPPER(Name))   | Integration  |
| JournalEntry ExternalId unique    | DB    | Unique partial index          | Integration  |
| Line Amount > 0                   | DB    | CHECK constraint              | Integration  |
| Money precision numeric(20,4)     | DB    | Column type                   | Integration  |
| No cascade deletes               | DB    | Foreign key (Restrict)         | Integration  |
| UTC timestamps                    | DB    | Column type (timestamptz)     | Integration  |

==================================================
GLOBAL RULES
==================================================

| Rule                       | Layer | Enforcement   | Test |
|----------------------------|-------|---------------|------|
| RFC7807 ProblemDetails     | API   | Middleware    | Unit |
| CorrelationId included     | API   | Middleware    | Unit |
| UTC timestamps             | App   | Guard         | Unit |
| No deletes for ledger      | API   | No endpoints  | N/A  |

==================================================
END OF MATRIX
==================================================
