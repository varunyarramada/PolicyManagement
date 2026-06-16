# Architecture: PolicyManagement BFF Service

- **Date:** 2026-06-16
- **Status:** Approved
- **Authors:** Architect Agent (Chubb APAC PolicyManagement)

## Context

This document defines the system architecture for the **PolicyManagement Backend-for-Frontend (BFF)** service built for Chubb APAC. The service aggregates and serves insurance policy data to a Policy Overview Dashboard frontend, spanning eight APAC regions and four lines of business. It is implemented in .NET 10 / ASP.NET Core Web API following Clean Architecture, logical CQRS via MediatR, and a contract-first OpenAPI 3.x design.

This document covers:

- Layer responsibilities and namespace assignments
- The `Policy` entity database schema with indexing strategy
- API contract structure for all four endpoints
- Architectural risks and trade-offs

Architectural decision rationale is captured in the ADRs under `docs/architecture/decisions/`.

---

## Layer Responsibilities

```
┌──────────────────────────────────────────────┐
│            PolicyManagement.API              │  ASP.NET Core, controllers, middleware
├──────────────────────────────────────────────┤
│        PolicyManagement.Application          │  Use cases, CQRS handlers, validators, DTOs
├──────────────────────────────────────────────┤
│          PolicyManagement.Domain             │  Entities, value objects, events, interfaces
├──────────────────────────────────────────────┤
│       PolicyManagement.Infrastructure        │  EF Core, SQL Server, cache, event publisher
└──────────────────────────────────────────────┘

Allowed dependency directions:
API  →  Application  →  Domain  ←  Infrastructure
```

| Layer | Namespace | Responsibilities |
|-------|-----------|-----------------|
| **Domain** | `PolicyManagement.Domain` | Pure business core. Zero external dependencies beyond the .NET BCL. Defines the `Policy` entity and its invariants. Declares repository interfaces (`IPolicyRepository`) and the event publisher interface (`IEventPublisher`). Contains domain exceptions (`PolicyNotFoundException`, `InvalidPolicyStateException`). Contains domain events (`PolicyFlaggedEvent`). Contains the `IAuditableEntity` marker interface. |
| **Application** | `PolicyManagement.Application` | Orchestrates use cases via MediatR. Contains all CQRS commands, queries, and their handlers. Defines the `ICacheService` interface (cache abstraction). Contains DTOs (`PolicyDto`, `PolicySummaryResponse`, `PagedResponse<T>`), input validators (`GetPoliciesQueryValidator`, `FlagPoliciesCommandValidator`), and AutoMapper mapping profiles. Depends only on `Domain`. Never references EF Core, SQL, or any infrastructure concern. |
| **Infrastructure** | `PolicyManagement.Infrastructure` | Implements every interface from `Domain` and `Application`. Contains `PolicyDbContext`, EF Core entity configurations (`IEntityTypeConfiguration<T>`), the `PolicyRepository` implementation, `InMemoryCacheService`, `InMemoryEventPublisher`, and the seed data factory. Contains the `HttpClients/` folder (reserved for future downstream integrations). |
| **API** | `PolicyManagement.API` | ASP.NET Core entry point. Contains `PoliciesController` (thin — delegates exclusively to MediatR), `GlobalExceptionMiddleware`, health check registrations, CORS configuration, response compression, OpenAPI/Swagger registration, and `Program.cs` DI composition root. Contains `CorrelationIdMiddleware` for request tracing. |

### Prohibited dependencies (enforce strictly)

| Dependency | Violation |
|---|---|
| `Domain` referencing `Application`, `Infrastructure`, or `API` | Forbidden |
| `Application` referencing `Infrastructure` or `API` | Forbidden |
| `Application` referencing `DbContext`, `DbSet`, or any EF Core type | Forbidden |
| `Domain` referencing any NuGet package beyond the .NET BCL | Forbidden |
| Business logic written in a controller action | Forbidden |
| Concrete infrastructure types injected anywhere except `Program.cs` | Forbidden |
| `HttpContext` or ASP.NET types used outside the `API` layer | Forbidden |

---

## CQRS Handler Map

| Operation | Type | Message | Handler |
|---|---|---|---|
| List policies | Query | `GetPoliciesQuery` | `GetPoliciesQueryHandler` |
| Get policy by ID | Query | `GetPolicyByIdQuery` | `GetPolicyByIdQueryHandler` |
| Get summary statistics | Query | `GetPolicySummaryQuery` | `GetPolicySummaryQueryHandler` |
| Bulk flag policies | Command | `FlagPoliciesCommand` | `FlagPoliciesCommandHandler` |

Controllers call `_mediator.Send(request, ct)` only. No business logic in controllers.

### MediatR pipeline behaviours (applied to all handlers)

| Order | Behaviour | Responsibility |
|---|---|---|
| 1 | `LoggingPipelineBehavior<,>` | Logs handler entry, exit, and elapsed time with structured parameters |
| 2 | `ValidationPipelineBehavior<,>` | Runs all registered `IValidator<T>` for the request; throws `ValidationException` on failure |

---

## Application Folder Structure

```
Application/
└── Features/
    └── Policies/
        ├── Commands/
        │   └── FlagPolicies/
        │       ├── FlagPoliciesCommand.cs
        │       ├── FlagPoliciesCommandHandler.cs
        │       └── FlagPoliciesCommandValidator.cs
        └── Queries/
            ├── GetPolicies/
            │   ├── GetPoliciesQuery.cs
            │   ├── GetPoliciesQueryHandler.cs
            │   └── GetPoliciesQueryValidator.cs
            ├── GetPolicyById/
            │   ├── GetPolicyByIdQuery.cs
            │   └── GetPolicyByIdQueryHandler.cs
            └── GetPolicySummary/
                ├── GetPolicySummaryQuery.cs
                └── GetPolicySummaryQueryHandler.cs
```

---

## Database Schema

### Design conventions

- Table name: `Policies` (PascalCase, SQL Server convention)
- Column names: `snake_case` (portable, explicitly mapped in `IEntityTypeConfiguration<T>`)
- Enums stored as `varchar(50)` strings — self-documenting, no migration cost for new members
- All monetary values stored as `decimal(18,2)` — never `float` or `double`
- Policy dates stored as `date` (maps to `DateOnly`) — no time component
- Audit timestamps stored as `datetimeoffset(7)` (maps to `DateTimeOffset`) — timezone-aware for APAC
- No EF Core data annotations on entity classes — all mapping in configuration classes
- Soft delete enforced via global EF Core query filter on `is_deleted`

### Table: `Policies`

| Column | C# Property | SQL Type | Nullable | Constraints |
|--------|-------------|----------|----------|-------------|
| `id` | `Id` | `uniqueidentifier` | No | Primary key; client-generated GUID |
| `policy_number` | `PolicyNumber` | `varchar(20)` | No | Unique; format `POL-XXXXXX` |
| `policyholder_name` | `PolicyholderName` | `nvarchar(200)` | No | |
| `line_of_business` | `LineOfBusiness` | `varchar(50)` | No | Enum as string: Property, Casualty, A&H, Marine |
| `status` | `Status` | `varchar(50)` | No | Enum as string: Active, Expired, Pending, Cancelled |
| `premium_amount` | `PremiumAmount` | `decimal(18,2)` | No | Range: 1,000.00 – 5,000,000.00 |
| `currency` | `Currency` | `varchar(10)` | No | USD, SGD, HKD, AUD, JPY, THB |
| `effective_date` | `EffectiveDate` | `date` | No | |
| `expiry_date` | `ExpiryDate` | `date` | No | Must be after `effective_date` |
| `region` | `Region` | `varchar(100)` | No | Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines |
| `underwriter` | `Underwriter` | `nvarchar(200)` | No | |
| `flagged_for_review` | `FlaggedForReview` | `bit` | No | Default `0` |
| `is_deleted` | `IsDeleted` | `bit` | No | Default `0`; soft delete flag |
| `created_at` | `CreatedAt` | `datetimeoffset(7)` | No | Set on insert; never updated |
| `updated_at` | `UpdatedAt` | `datetimeoffset(7)` | No | Set on insert and updated on every write |

### Indexes

Indexes are designed for the access patterns of `GET /api/v1/policies` (filter, sort, search, pagination) and `GET /api/v1/policies/summary` (aggregation). All indexes exclude soft-deleted rows via a `WHERE is_deleted = 0` filter clause.

| Index Name | Columns | Type | Rationale |
|---|---|---|---|
| `PK_Policies` | `id` | Primary key (clustered) | Default clustered primary key on GUID. Non-sequential GUIDs cause page fragmentation — see trade-off note below. |
| `UQ_Policies_PolicyNumber` | `policy_number` | Unique non-clustered | Enforces uniqueness constraint; used for LIKE search lookups |
| `IX_Policies_Status` | `status` | Non-clustered, filtered (`is_deleted = 0`) | Supports `status` filter on list endpoint and `countByStatus` aggregation |
| `IX_Policies_LineOfBusiness` | `line_of_business` | Non-clustered, filtered (`is_deleted = 0`) | Supports `lineOfBusiness` filter and `countByLineOfBusiness` aggregation |
| `IX_Policies_Region` | `region` | Non-clustered, filtered (`is_deleted = 0`) | Supports `region` filter and `countByRegion` aggregation |
| `IX_Policies_EffectiveDate` | `effective_date` | Non-clustered, filtered (`is_deleted = 0`) | Supports `effectiveDateFrom` / `effectiveDateTo` range filter |
| `IX_Policies_ExpiryDate` | `expiry_date` | Non-clustered, filtered (`is_deleted = 0`) | Supports `expiryDate` range filter and `expiringSoonCount` computation |
| `IX_Policies_CreatedAt` | `created_at DESC` | Non-clustered, filtered (`is_deleted = 0`) | Supports default sort (`createdAt DESC`) and pagination |
| `IX_Policies_PolicyholderName` | `policyholder_name` | Non-clustered, filtered (`is_deleted = 0`) | Supports `policyholderName` sort; partial support for LIKE search |
| `IX_Policies_FlaggedForReview` | `flagged_for_review` | Non-clustered, filtered (`is_deleted = 0`) | Supports `flaggedCount` aggregation in summary |
| `IX_Policies_Status_LineOfBusiness` | `status`, `line_of_business` | Composite non-clustered, filtered (`is_deleted = 0`) | Supports the most common combined filter: status + line of business |
| `IX_Policies_Status_Region` | `status`, `region` | Composite non-clustered, filtered (`is_deleted = 0`) | Supports combined status + region filter (dashboard's primary view) |
| `IX_Policies_ExpiryDate_Status` | `expiry_date`, `status` | Composite non-clustered, filtered (`is_deleted = 0`) | Optimises `expiringSoonCount` query (Active + expiryDate range) |

**GUID clustering trade-off:** Using `uniqueidentifier` as the clustered primary key with client-generated GUIDs causes index page fragmentation because new rows are not inserted in sequential order. Mitigations: (a) use `NEWSEQUENTIALID()` for server-generated GUIDs in production, or (b) use `int IDENTITY` as the clustered key and expose the GUID as a non-clustered unique key. For this assessment's 200-record seed dataset, the fragmentation impact is negligible. The mitigation path is documented for production readiness.

**LIKE search limitation:** The `search` parameter uses `LIKE '%term%'` which cannot use a standard B-tree index and results in an index scan. For the assessment's scale (200–10,000 records), this is acceptable. The search predicate is isolated in the repository so it can be replaced with SQL Server Full-Text Search (`CONTAINS`) without any change to the handler or controller.

---

## API Contract Structure

Base URL: `/api/v1`

All error responses use RFC 7807 `application/problem+json`. All successful responses use `application/json`. Correlation IDs are included in every error response.

### Shared Schemas

**`PolicyDto`** — represents a single policy in list and detail responses:

| Field | Type | Description |
|---|---|---|
| `id` | `string (uuid)` | Policy unique identifier |
| `policyNumber` | `string` | Format: `POL-XXXXXX` |
| `policyholderName` | `string` | |
| `lineOfBusiness` | `string (enum)` | Property, Casualty, A&H, Marine |
| `status` | `string (enum)` | Active, Expired, Pending, Cancelled |
| `premiumAmount` | `number (decimal)` | |
| `currency` | `string` | USD, SGD, HKD, AUD, JPY, THB |
| `effectiveDate` | `string (date)` | ISO 8601 date format: `YYYY-MM-DD` |
| `expiryDate` | `string (date)` | ISO 8601 date format: `YYYY-MM-DD` |
| `region` | `string` | APAC region name |
| `underwriter` | `string` | |
| `flaggedForReview` | `boolean` | |
| `createdAt` | `string (date-time)` | ISO 8601 with timezone offset |
| `updatedAt` | `string (date-time)` | ISO 8601 with timezone offset |

**`PaginationMeta`** — included in list responses:

| Field | Type | Description |
|---|---|---|
| `page` | `integer` | Current page number (1-based) |
| `size` | `integer` | Page size |
| `totalCount` | `integer` | Total matching records |
| `totalPages` | `integer` | Ceil(totalCount / size) |

**`ProblemDetails`** (RFC 7807):

| Field | Type | Description |
|---|---|---|
| `type` | `string (uri)` | Problem type URI |
| `title` | `string` | Short problem summary |
| `status` | `integer` | HTTP status code |
| `detail` | `string` | Human-readable explanation |
| `instance` | `string (uri)` | Endpoint URI that produced the error |
| `correlationId` | `string` | Request correlation ID |
| `errors` | `object` | Field-level errors (400 only) — `{ fieldName: [messages] }` |

---

### `GET /api/v1/policies`

**Purpose:** Returns a paginated, filtered, sorted list of policies.

**Query parameters:**

| Parameter | Type | Required | Default | Constraints | Description |
|---|---|---|---|---|---|
| `page` | integer | No | `1` | `>= 1` | 1-based page number |
| `size` | integer | No | `20` | `1–100` | Records per page |
| `sort` | string | No | `createdAt` | See allowed values | Comma-separated `field,direction` — e.g., `sort=premiumAmount,desc` |
| `status` | string | No | — | Active, Expired, Pending, Cancelled | Filter by policy status |
| `lineOfBusiness` | string | No | — | Property, Casualty, A&H, Marine | Filter by line of business |
| `region` | string | No | — | APAC region values | Filter by region |
| `effectiveDateFrom` | string (date) | No | — | ISO 8601 date; inclusive | Start of effective date range |
| `effectiveDateTo` | string (date) | No | — | ISO 8601 date; inclusive | End of effective date range |
| `search` | string | No | — | — | Case-insensitive substring match across `policyNumber`, `policyholderName`, `underwriter` |

**Allowed `sort` fields:** `policyNumber`, `status`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, `policyholderName`

**Default sort:** `createdAt,desc` (when `sort` parameter is absent)

**Multiple filters** are combined with AND logic.

**Response — `200 OK`:**

```
{
  "data": [ PolicyDto ],
  "pagination": PaginationMeta
}
```

**Error responses:**

| Status | Condition |
|---|---|
| `400 Bad Request` | Invalid `page`, `size`, `sort` field, or `status`/`lineOfBusiness` enum value |
| `500 Internal Server Error` | Unhandled exception |

---

### `GET /api/v1/policies/{id}`

**Purpose:** Returns a single policy by UUID.

**Path parameters:**

| Parameter | Type | Required | Description |
|---|---|---|---|
| `id` | `string (uuid)` | Yes | Policy UUID |

**Response — `200 OK`:** `PolicyDto`

**Error responses:**

| Status | Condition |
|---|---|
| `400 Bad Request` | `id` is not a valid UUID |
| `404 Not Found` | No policy with the given ID exists (or is soft-deleted) |
| `500 Internal Server Error` | Unhandled exception |

---

### `PATCH /api/v1/policies/flag`

**Purpose:** Bulk-flags a set of policies for review. Atomic — all policies are updated in a single transaction or none are.

**Request body (`application/json`):**

```
{
  "policyIds": [ "uuid", ... ]
}
```

| Field | Type | Required | Constraints |
|---|---|---|---|
| `policyIds` | `array of uuid` | Yes | Min 1, max 100 items; no duplicates |

**Behaviour:**
1. Validate the request (min/max array size, valid UUIDs).
2. Verify all supplied IDs exist and are not soft-deleted — if any are missing, return `404`.
3. Verify no supplied policy is already flagged — if any are already flagged, return `409`.
4. Within a single database transaction, set `flagged_for_review = true` and update `updated_at` on all matched policies.
5. Publish a `PolicyFlaggedEvent` for each flagged policy via `IEventPublisher`.
6. Invalidate the summary cache key.

**Response — `204 No Content`:** Empty body.

**Error responses:**

| Status | Condition |
|---|---|
| `400 Bad Request` | `policyIds` is empty, exceeds 100, or contains invalid UUIDs |
| `404 Not Found` | One or more policy IDs do not exist |
| `409 Conflict` | One or more policies are already flagged for review |
| `500 Internal Server Error` | Unhandled exception |

---

### `GET /api/v1/policies/summary`

**Purpose:** Returns aggregated statistics across all non-deleted policies. Response is cached with a short TTL (1 minute) and invalidated on successful flag operations.

**Query parameters:** None.

**Response — `200 OK`:**

```
{
  "totalCount":              integer,
  "flaggedCount":            integer,
  "expiringSoonCount":       integer,
  "countByStatus": {
    "Active":                integer,
    "Expired":               integer,
    "Pending":               integer,
    "Cancelled":             integer
  },
  "countByRegion": {
    "Singapore":             integer,
    "Hong Kong":              integer,
    "Australia":             integer,
    "Japan":                 integer,
    "Thailand":              integer,
    "Indonesia":             integer,
    "Malaysia":              integer,
    "Philippines":           integer
  },
  "countByLineOfBusiness": {
    "Property":              integer,
    "Casualty":              integer,
    "A&H":                   integer,
    "Marine":                integer
  },
  "premiumTotalByCurrency": {
    "USD":                   number (decimal),
    "SGD":                   number (decimal),
    "HKD":                   number (decimal),
    "AUD":                   number (decimal),
    "JPY":                   number (decimal),
    "THB":                   number (decimal)
  }
}
```

**Field definitions:**

| Field | Definition |
|---|---|
| `totalCount` | Count of all non-deleted policies |
| `flaggedCount` | Count of policies where `flagged_for_review = true` |
| `expiringSoonCount` | Count of Active policies where `expiry_date` is within the next 30 calendar days from the current UTC date (inclusive of today) |
| `countByStatus` | Count per `PolicyStatus` enum value |
| `countByRegion` | Count per region string value |
| `countByLineOfBusiness` | Count per `LineOfBusiness` enum value |
| `premiumTotalByCurrency` | Sum of `premium_amount` grouped by `currency`; only currencies with at least one policy are included |

**Error responses:**

| Status | Condition |
|---|---|
| `500 Internal Server Error` | Unhandled exception |

---

## Architectural Risks and Trade-offs

| Risk | Likelihood | Impact | Mitigation |
|---|---|---|---|
| **GUID clustering fragmentation** — Client-generated `uniqueidentifier` primary keys are non-sequential, causing B-tree page splits and index fragmentation on the clustered primary key. | Low (200-record seed; acceptable at dev scale) | Med (production scale) | Use `NEWSEQUENTIALID()` or a separate `int IDENTITY` clustered key with the GUID as a non-clustered unique key. Document this as a production readiness item. |
| **LIKE search full table scan** — `LIKE '%term%'` cannot use a B-tree index and results in a full scan of the `Policies` table for any search query. | Med | Med | Acceptable at assessment scale. Isolate the search predicate in `IPolicyRepository` so it can be replaced with `CONTAINS` (SQL Server Full-Text Search) without any handler change. ADR-006 documents this path. |
| **Bulk-flag partial visibility** — If the transaction commits but `IEventPublisher.PublishAsync` fails, the database is updated but no `PolicyFlaggedEvent` is delivered to downstream consumers. | Low | Med | In-memory publisher is fire-and-forget; log failures at `Warning` level. For the Kafka production swap, use transactional outbox pattern (write events to a `PolicyOutboxEvents` table inside the same DB transaction) to guarantee at-least-once delivery. |
| **Summary cache staleness after flag** — The summary response is cached. If the cache key is not invalidated after a successful flag operation, `flaggedCount` and `expiringSoonCount` may be stale for up to the TTL window (1 minute). | Low | Low | `FlagPoliciesCommandHandler` explicitly calls `ICacheService.RemoveAsync` for the summary cache key after a successful commit. |
| **Summary aggregation under load** — `GET /api/v1/policies/summary` runs GROUP BY aggregations across the full dataset on every cache miss. Under concurrent traffic and large datasets, this generates contention on the `Policies` table. | Med | Med | Short TTL cache (1 minute) reduces database hits by a factor proportional to request rate. For production, add filtered indexes on all aggregation columns (already designed in the schema above) and consider a materialized summary updated by the flag command handler. |
| **EF Core InMemory test provider** — The InMemory database provider used in unit/integration tests does not enforce relational constraints, foreign keys, or unique indexes. Uniqueness violations on `policy_number` will not be caught by InMemory-backed tests. | Med | Med | Integration tests that verify constraint enforcement (e.g., unique `policyNumber`) must use the real SQL Server schema, not InMemory. Use `WebApplicationFactory` with a real test database or Respawn for teardown. |
| **Swagger exposure in non-development environments** — Swagger UI and the OpenAPI JSON endpoint expose the full API contract if left enabled outside Development. | Low | High | `app.UseSwagger()` and `app.UseSwaggerUI()` are gated behind `app.Environment.IsDevelopment()`. If staging access is needed, introduce an explicit `Features:SwaggerEnabled` configuration flag — never default to open. |
| **Already-flagged policy detection race condition** — In a concurrent scenario, two simultaneous flag requests for the same policy ID could both pass the "not already flagged" check before either commits. | Low | Low | Acceptable for the assessment. In production, use a database-level optimistic concurrency token (`xmin` / `rowversion`) on the `Policy` entity to detect concurrent updates and return a conflict error. |
| **`decimal(18,2)` for JPY** — JPY has no fractional units by convention. Storing JPY premium amounts as `decimal(18,2)` introduces two unnecessary decimal places. | Low | Low | Documented as assumption A-04. The assessment does not require currency-aware decimal handling. If required, introduce a `CurrencyDecimalPlaces` value object in Domain to enforce the correct scale per currency. |
