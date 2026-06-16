# Requirement Analysis: PolicyManagement BFF — Chubb APAC Take-Home Assessment

## Summary

This document analyses the requirements for the **PolicyManagement Backend-for-Frontend (BFF)** service for Chubb APAC. The service acts as an orchestration and aggregation layer between an insurance policy management frontend dashboard and downstream data sources, serving an APAC regional footprint spanning eight countries (Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines) and four lines of business (Property, Casualty, Accident & Health, Marine). The assessment requires a production-grade .NET 10 / ASP.NET Core Web API implementing Clean Architecture, logical CQRS via MediatR, a contract-first OpenAPI 3.x design, EF Core with SQL Server, and a comprehensive test suite. The core domain revolves around insurance policies with statuses, premiums, policyholders, and underwriters — exposed through four REST endpoints covering list, detail, bulk-flag, and aggregated-summary operations.

---

## Functional Requirements

| ID | Requirement | Source |
|----|-------------|--------|
| FR-01 | The system shall expose a `GET /api/v1/policies` endpoint that returns a paginated list of policies. | Assessment brief — endpoint specification |
| FR-02 | The list endpoint shall support filtering by `status` (Active, Expired, Pending, Cancelled). | Assessment brief — filter specification |
| FR-03 | The list endpoint shall support filtering by `lineOfBusiness` (Property, Casualty, A&H, Marine). | Assessment brief — filter specification |
| FR-04 | The list endpoint shall support filtering by `region` (Singapore, Hong Kong, Australia, Japan, Thailand, Indonesia, Malaysia, Philippines). | Assessment brief — filter specification |
| FR-05 | The list endpoint shall support filtering by `effectiveDateFrom` and `effectiveDateTo` as an inclusive date range in ISO 8601 `date` format. | Assessment brief — filter specification |
| FR-06 | The list endpoint shall support free-text `search` across policy number, policyholder name, and underwriter name. | Assessment brief — search specification |
| FR-07 | The list endpoint shall support sorting by `policyNumber`, `status`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, and `policyholderName`. The sort format is a single comma-separated parameter: `sort=field,direction` (e.g., `sort=premiumAmount,desc`). | Assessment brief — sort specification; G-06 and G-14 resolved |
| FR-08 | The list endpoint shall support `asc` and `desc` sort order. | Assessment brief — sort specification |
| FR-09 | The list endpoint shall support `page` (1-based) and `size` (1–100) pagination parameters with defaults of `page=1` and `size=20`. | Assessment brief — pagination specification |
| FR-10 | The list endpoint shall return a pagination envelope containing `data` (array of policy DTOs) and `pagination` metadata (`page`, `size`, `totalCount`, `totalPages`). | Assessment brief — response contract |
| FR-11 | Multiple filters applied simultaneously shall be combined with AND logic. | Assessment brief — filter behaviour |
| FR-12 | The system shall expose a `GET /api/v1/policies/{id}` endpoint returning a single policy by UUID. | Assessment brief — endpoint specification |
| FR-13 | The single-policy endpoint shall return `404 Not Found` with RFC 7807 `ProblemDetails` when the policy ID does not exist. | Assessment brief — error handling |
| FR-14 | The system shall expose a `PATCH /api/v1/policies/flag` endpoint that bulk-flags a list of policy IDs for review. | Assessment brief — endpoint specification |
| FR-15 | The bulk-flag endpoint shall accept a request body containing an array of policy UUIDs (`policyIds`), minimum 1, maximum 100. | Assessment brief — request contract |
| FR-16 | The bulk-flag endpoint shall return `204 No Content` on success. | Assessment brief — response contract |
| FR-17 | The bulk-flag endpoint shall return `404 Not Found` when one or more supplied policy IDs do not exist. | Assessment brief — error handling |
| FR-18 | The bulk-flag endpoint shall set `flaggedForReview = true` on each matched policy. | Assessment brief — domain behaviour |
| FR-19 | A `PolicyFlaggedEvent` domain event shall be published for each policy that is successfully flagged. | Assessment brief / copilot-instructions — eventing |
| FR-20 | The system shall expose a `GET /api/v1/policies/summary` endpoint returning aggregated statistics. | Assessment brief — endpoint specification |
| FR-21 | The summary endpoint shall return: `totalCount`, `countByStatus`, `countByRegion`, `countByLineOfBusiness`, `premiumTotalByCurrency`, `flaggedCount`, and `expiringSoonCount`. | Assessment brief — aggregation specification; G-04 and G-13 resolved |
| FR-22 | Policy records shall carry the following fields: `id` (UUID), `policyNumber` (format `POL-XXXXXX`, unique), `policyholderName`, `lineOfBusiness`, `status`, `premiumAmount` (decimal, 1,000–5,000,000), `currency`, `effectiveDate`, `expiryDate`, `region`, `underwriter`, `flaggedForReview` (boolean, default false), `createdAt`, `updatedAt`. | Assessment brief — data model |
| FR-23 | The system shall implement soft delete for policy records (`isDeleted` flag); hard deletion of policy records is prohibited. | Derived from insurance record-keeping requirement |
| FR-24 | The system shall seed the database with a minimum of 200 realistic policy records on first startup in the development environment, covering all statuses, regions, lines of business, and currencies. | Assessment brief — seed data requirement |
| FR-25 | The system shall apply EF Core database migrations to create and maintain the schema. | Assessment brief — persistence requirement |
| FR-26 | All API errors shall be returned in RFC 7807 `ProblemDetails` format with `application/problem+json` content type. | Assessment brief / copilot-instructions — error handling |
| FR-27 | Validation failures shall return `400 Bad Request` with a field-level `errors` map in the `ProblemDetails` extension. | Assessment brief — validation behaviour |
| FR-28 | A correlation ID shall be included in every error response to enable log tracing. | Copilot-instructions — observability |
| FR-29 | Health check endpoints shall be exposed at `/health/live` (liveness) and `/health/ready` (readiness). | Copilot-instructions — health checks |
| FR-30 | The readiness health check shall verify SQL Server connectivity. | Copilot-instructions — health checks |
| FR-31 | All API endpoints shall be versioned under `/api/v1/`. | Copilot-instructions — API versioning |
| FR-32 | *(Bonus)* The system shall implement a Kafka producer that publishes events when policies are flagged for review. | Assessment brief — Kafka integration bonus |
| FR-33 | *(Bonus)* The system shall implement a Kafka consumer that listens for policy status change events and processes them with idempotent handling. | Assessment brief — Kafka integration bonus |

---

## Non-Functional Requirements

| ID | Category | Requirement | Source |
|----|----------|-------------|--------|
| NFR-01 | Architecture | The system shall follow Clean Architecture with strict inward-pointing dependencies: `API → Application → Domain ← Infrastructure`. | Copilot-instructions — architecture |
| NFR-02 | Architecture | `Domain` shall have zero dependencies on any NuGet package beyond the .NET BCL. | Copilot-instructions — architecture |
| NFR-03 | Architecture | Business logic shall reside exclusively in the `Application` layer (handlers). Controllers shall contain no business logic. | Copilot-instructions — architecture |
| NFR-04 | Design | The OpenAPI 3.x specification shall be the source of truth for all API contracts. It shall be written before any implementation code (contract-first). | Copilot-instructions — API design |
| NFR-05 | Design | The system shall use logical CQRS via MediatR (single SQL Server database; read/write handlers separated; no event sourcing or separate read model). | Copilot-instructions — CQRS |
| NFR-06 | Design | The Repository Pattern shall be used: interfaces in `Domain`, implementations in `Infrastructure`, `Application` depends only on interfaces. | Copilot-instructions — repository pattern |
| NFR-07 | Caching | Cache access shall be abstracted behind `ICacheService` (defined in `Application`). The in-memory implementation must be swappable for Redis without changing calling code. | Copilot-instructions — caching |
| NFR-08 | Eventing | Event publishing shall be abstracted behind `IEventPublisher` (defined in `Domain`). The in-memory implementation must be swappable for Kafka without changing calling code. Domain events must be serialisable. The bonus Kafka integration requires a well-defined event schema and idempotent consumer handling. | Copilot-instructions — eventing; Assessment brief — Kafka bonus |
| NFR-09 | Testability | Unit tests shall be provided for all Application handler public methods and all Domain entity invariants. | Copilot-instructions — testing |
| NFR-10 | Testability | Integration tests using `WebApplicationFactory<Program>` shall cover all API endpoints and all declared HTTP status code paths. | Copilot-instructions — testing |
| NFR-11 | Testability | xUnit shall be the only test framework. | Copilot-instructions — testing |
| NFR-12 | Security | Stack traces, connection strings, internal exception messages, SQL errors, and server names shall never appear in API responses. | Copilot-instructions — security |
| NFR-13 | Security | All configuration secrets (connection strings, API keys) shall be externalised via environment variables or a secrets manager. No secrets shall be committed to source control. | Copilot-instructions — configuration |
| NFR-14 | Security | CORS shall be configured with explicit allowed origins. `AllowAnyOrigin` is prohibited in production. | Production readiness conventions |
| NFR-15 | Observability | All log messages shall use structured parameters (named placeholders). String interpolation in `ILogger` calls is prohibited. | Copilot-instructions — logging |
| NFR-16 | Observability | Log levels shall be applied correctly: `Information` for normal flow, `Warning` for expected exceptional paths (not-found, validation), `Error` for unhandled failures. | Copilot-instructions — logging |
| NFR-17 | Observability | Correlation IDs shall be propagated through every request log scope so all log entries for a single request are queryable together. | Copilot-instructions — observability |
| NFR-18 | Configuration | All configuration sections shall be bound to strongly-typed `IOptions<T>` classes. Direct `IConfiguration["key"]` access in business code is prohibited. | Copilot-instructions — configuration |
| NFR-19 | Configuration | Options classes shall be validated at startup using `ValidateOnStart()` so misconfiguration fails before the first request. | Production readiness conventions |
| NFR-20 | Performance | Read queries (all `GET` handlers) shall use `.AsNoTracking()`. EF Core change tracking is only enabled when a subsequent write follows. | Database conventions |
| NFR-21 | Performance | Database indexes shall be designed for the list endpoint's filter and sort access patterns: single-column indexes on `status`, `lineOfBusiness`, `region`, `effectiveDate`, `expiryDate`; composite indexes for common combined filters. | Database conventions |
| NFR-22 | Resilience | `CancellationToken` shall be accepted and propagated through every async controller action, MediatR handler, and repository method. | Copilot-instructions — async |
| NFR-23 | Resilience | No `.Result` or `.Wait()` calls shall appear anywhere in the codebase. All async code uses `async`/`await`. | Copilot-instructions — async |
| NFR-24 | Maintainability | Every class shall have a single, well-defined responsibility (SRP). God classes are prohibited. | Copilot-instructions — SOLID |
| NFR-25 | Maintainability | Dependency injection shall use interface abstractions throughout. Concrete infrastructure types shall not be injected directly except in `Program.cs`. | Copilot-instructions — DI |
| NFR-26 | Deployability | The service shall be containerisable using a multi-stage Dockerfile. The runtime image shall run as a non-root user. | Production readiness conventions |
| NFR-27 | API Design | Every controller action shall declare `[ProducesResponseType]` for every possible HTTP status code it can return. | Copilot-instructions — API design |
| NFR-28 | API Design | Response compression shall be enabled for `application/json` and `application/problem+json`. | Production readiness conventions |

---

## Risks

| ID | Risk | Likelihood | Impact | Mitigation |
|----|------|------------|--------|------------|
| R-01 | The `.docx` requirements document is binary and cannot be parsed programmatically; nuanced requirements buried in prose may be missed. | Med | High | Cross-reference all derived requirements against the `copilot-instructions.md`, architect agent, and skill documents produced in this session. Raise open questions for any gaps found during implementation. |
| R-02 | Free-text search using SQL `LIKE '%term%'` performs a full table scan. With 200+ seed records this is acceptable, but performance degrades as data grows. | Med | Med | Isolate the search predicate in the repository so it is swappable. Document the SQL Server Full-Text Search upgrade path. |
| R-03 | Bulk-flag operation (`PATCH /flag`) processes policy IDs sequentially. If one policy fails mid-batch, preceding policies are already written. This is a partial-update scenario with no rollback. | Med | High | Wrap the entire batch in a single database transaction. Validate all IDs exist before applying any updates (read-validate-then-write pattern). |
| R-04 | The in-memory `ICacheService` has no expiry enforcement across application restarts. A cache warm-up period after restart may cause slightly stale reads if cache keys are not invalidated on writes. | Low | Low | Invalidate relevant cache keys in the `FlagPoliciesCommandHandler` after a successful update. Document the Redis swap path for production. |
| R-05 | The `IEventPublisher` in-memory implementation is fire-and-forget. If event delivery fails silently, downstream consumers receive no notification of flagged policies. | Low | Med | Add structured logging at `Warning` level when event publishing fails. Design the Kafka swap path to use durable delivery guarantees. |
| R-06 | `GET /api/v1/policies/summary` performs aggregation across the full dataset on every request. With no caching, concurrent requests could cause database contention. | Med | Med | Cache the summary response behind `ICacheService` with a short TTL (e.g., 1 minute). Invalidate on successful flag operations. |
| R-07 | EF Core InMemory provider used in integration tests does not enforce relational constraints, foreign keys, or unique indexes. Bugs that depend on these constraints will not be caught at test time. | Med | Med | Where constraint enforcement is critical (e.g., unique `policyNumber`), add explicit uniqueness assertions in repository integration tests using the real SQL Server schema. |
| R-08 | The assessment has a fixed technology stack. Any deviation (e.g., using NUnit instead of xUnit, or physical CQRS) will be penalised. | Low | High | Enforce the stack via the `copilot-instructions.md` and agent files. Reject any code generation that deviates. |
| R-09 | Multiple APAC currency formats have different decimal place conventions (e.g., JPY has no fractional units). Storing all premiums as `decimal(18,2)` may be semantically incorrect for JPY. | Low | Med | Validate whether the assessment requires currency-aware decimal handling. If not, document the assumption that `decimal(18,2)` is used uniformly for simplicity. |
| R-10 | Swagger UI left enabled in a non-development environment would expose the full API contract and operation list publicly. | Low | High | Enforce the `app.Environment.IsDevelopment()` guard on Swagger registration. Include this in the production readiness checklist. |

---

## Assumptions

- **A-01**: The assessment does not require authentication or authorisation (no JWT, no OAuth). All endpoints are accessible without credentials. If authentication is added in future, it will be introduced as a new requirement.
- **A-02**: The BFF does not call any downstream HTTP services in the current implementation. The `HttpClients/` folder in Infrastructure is reserved for future integrations only. All data is served from the local SQL Server database.
- **A-03**: The policy data model is flat — there are no parent-child policy relationships, policy riders, or endorsements required by the assessment.
- **A-04**: `decimal(18,2)` is used uniformly for `premiumAmount` across all currencies, including JPY (which conventionally has no fractional units). This is an assessment simplification.
- **A-05**: The `region` field is stored as a plain `varchar` string. There is no separate `Region` entity or foreign key relationship. Region values are validated by the application layer against a fixed enum-like list.
- **A-06**: The `underwriter` field is stored as a plain `nvarchar` name string. There is no separate `Underwriter` entity or user account relationship required by the assessment.
- **A-07**: The `policyholderName` field is stored as a plain `nvarchar` name string. There is no separate `Policyholder` entity with address, contact, or identity fields required by the assessment.
- **A-08**: Soft delete applies to all policy records. The `GET /api/v1/policies` and `GET /api/v1/policies/{id}` endpoints never return soft-deleted records. A global EF Core query filter enforces this.
- **A-09**: The seed dataset is generated programmatically at startup using a fixed random seed for reproducibility. It is not loaded from a data file.
- **A-10**: SQL Server is the preferred database and matches the OneHub production stack on Azure SQL. PostgreSQL or SQLite are explicitly permitted by the source document for local development only. The project implementation uses SQL Server (LocalDB) for development.
- **A-11**: Response pagination uses 1-based page numbering (`page=1` returns the first page). 0-based indexing is not used.
- **A-12**: All timestamps are stored and returned in UTC (`DateTimeOffset`). The API does not perform timezone conversion for individual regions.
- **A-13**: The `GET /api/v1/policies/summary` endpoint returns global aggregates across all regions and lines of business. Region-scoped or LOB-scoped summary sub-endpoints are not required.
- **A-14**: Flagging an already-flagged policy returns `409 Conflict` with `InvalidPolicyStateException`. This is intentional — an explicit error is safer for audit trail purposes than silent idempotency. *(G-01 resolved)*
- **A-15**: The assessment permits either C# / .NET or Java / Spring Boot. This implementation uses C# / .NET 10 as specified in the project's `copilot-instructions.md`. Both are stated as equally valid by the assessment.
- **A-16**: The assessment is sprint-format with a 2–3 hour target and a 5 hour hard cap. Not all bonus features (Kafka, caching) are expected to be complete. Prioritisation decisions made under time pressure will be documented in the AI working journal and explained during the walkthrough.
- **A-17**: The `expiringSoonCount` in the summary response is defined as policies whose `expiryDate` falls within the next 30 days from the current date and whose `status` is Active. *(G-13 resolved)*
- **A-18**: The `sort` query parameter for the list endpoint uses comma-separated `field,direction` format (e.g., `sort=premiumAmount,desc`), consistent with the example in the source document. The sort field name for premium is `premiumAmount` (matching the schema field), not `premium`. *(G-14 resolved)*

---

## Gaps & Ambiguities

| ID | Description | Recommended Action |
|----|-------------|--------------------|
| G-01 | **Idempotency of flag operation**: It is unspecified whether attempting to flag an already-flagged policy should return `409 Conflict` or silently succeed with `204 No Content`. | **RESOLVED**: Return `409 Conflict` with `InvalidPolicyStateException`. Explicit error is safer for audit trail. |
| G-02 | **Partial batch failure behaviour for bulk-flag**: If a batch of 5 policy IDs contains one invalid ID, it is unspecified whether the valid 4 should be flagged (partial success) or the entire batch rejected atomically. | **RESOLVED**: Atomic — all-or-nothing in a single transaction. Validate all IDs exist before any update; reject the entire batch on any failure. |
| G-03 | **Free-text search scope**: The `search` parameter is stated to search policy number, policyholder name, and underwriter name. It is unspecified whether this is a prefix match, substring match, or full-text match, and whether it is case-sensitive. | **RESOLVED**: Case-insensitive substring match via `LIKE '%term%'` on `policyNumber`, `policyholderName`, and `underwriter`. Document upgrade path to SQL Server Full-Text Search. |
| G-04 | **Summary endpoint aggregation fields**: The exact fields returned by `GET /api/v1/policies/summary` are not fully specified. | **RESOLVED**: `totalCount`, `countByStatus`, `countByRegion`, `countByLineOfBusiness`, `premiumTotalByCurrency`, `flaggedCount`. |
| G-05 | **Maximum page size**: The assessment states pagination is supported but does not explicitly cap the maximum `size` parameter. An uncapped size allows clients to request the entire dataset in one call. | Enforce a maximum `size` of 100 in the `GetPoliciesQueryValidator`. Document this limit in the OpenAPI spec. |
| G-06 | **Sort field for `policyholderName`**: The sort specification lists `policyNumber`, `status`, `premium`, `effectiveDate`, `expiryDate`, and `createdAt` but does not mention `policyholderName`. | **RESOLVED**: `policyholderName` is a supported sort field. Add to the validator's allowed sort fields and add a database index on `policyholderName`. |
| G-07 | **Currency validation**: The `currency` field has a fixed list (USD, SGD, HKD, AUD, JPY, THB). It is unspecified whether the API validates incoming currency values on write operations (if any write endpoints are added) or whether it is read-only. | Since the current API is read-only for policies (no create/update beyond flag), document that currency is validated at the seed/migration layer only. |
| G-08 | **`effectiveDateFrom` / `effectiveDateTo` boundary inclusivity**: It is unspecified whether the date range filter is inclusive, exclusive, or half-open (inclusive start, exclusive end). | **RESOLVED**: Both bounds inclusive (`>=` and `<=`). Document in the OpenAPI spec `description` for both parameters. |
| G-09 | **Default sort order when no `sort` parameter is supplied**: The list endpoint's default ordering is unspecified. An unordered result set is non-deterministic across pages. | **RESOLVED**: Default sort is `createdAt DESC` (most recent policies first). |
| G-10 | **Swagger UI accessibility**: It is unspecified whether Swagger UI should be accessible in non-development environments (e.g., staging). | Restrict Swagger to `IsDevelopment()` only. If staging access is needed, require an explicit configuration flag rather than defaulting to open. |
| G-11 | **Rate limiting requirements**: The assessment does not specify rate limiting thresholds. The bulk-flag endpoint in particular is a write operation that could be abused. | Document that rate limiting is deferred but the endpoint design is compatible with ASP.NET Core's built-in rate limiting when introduced. |
| G-12 | **`updatedAt` on flag operation**: It is unspecified whether the `updatedAt` audit column is updated when a policy is flagged. | Confirm. Recommended: `updatedAt` is updated on every write, including flag operations. This is enforced in `PolicyDbContext.SaveChangesAsync`. |
| G-13 | **"Expiring-soon" definition**: The summary endpoint description mentions an "expiring-soon count" but does not define the timeframe. It is unspecified what window (e.g., 30 days, 60 days) or which statuses qualify as "expiring soon". | **RESOLVED**: Defined as Active policies with `expiryDate` within the next 30 calendar days from the current UTC date. Documented on the OpenAPI spec `description` field. |
| G-14 | **Sort parameter format**: The source document example shows `sort=premiumAmount,desc` (comma-separated field and direction in a single `sort` parameter). This conflicts with the existing FR-07 which lists the field as `premium`. The schema column is `premiumAmount`. | **RESOLVED**: Use comma-separated single `sort` parameter format. The sort field for premium is `premiumAmount`, matching the data model. The existing FR-07 field list is updated accordingly. |

---

## Open Questions

All questions resolved. Decisions recorded above in Gaps & Ambiguities and Assumptions.

| # | Question | Decision |
|---|----------|----------|
| 1 | Should flagging an already-flagged policy return `409 Conflict` or `204 No Content` (idempotent)? (See G-01) | **409 Conflict** — explicit error is safer for audit trail |
| 2 | Is the bulk-flag operation atomic (all-or-nothing) or does it allow partial success? (See G-02) | **Atomic** — all-or-nothing in a single transaction |
| 3 | What is the exact match behaviour for the `search` parameter — prefix, substring, or full-text? (See G-03) | **Case-insensitive substring** via `LIKE '%term%'` across `policyNumber`, `policyholderName`, and `underwriter` |
| 4 | What are the exact fields and structure of the `GET /api/v1/policies/summary` response? (See G-04) | `totalCount`, `countByStatus`, `countByRegion`, `countByLineOfBusiness`, `premiumTotalByCurrency`, `flaggedCount`, `expiringSoonCount` |
| 5 | Is `policyholderName` a supported sort field for the list endpoint? (See G-06) | **Yes** — add to allowed sort fields and add DB index |
| 6 | Are date range filter boundaries (`effectiveDateFrom`, `effectiveDateTo`) inclusive or exclusive? (See G-08) | **Both inclusive** (`>=` and `<=`) |
| 7 | What is the default sort order when no `sort` query parameter is provided? (See G-09) | **`createdAt DESC`** |
| 8 | Does the summary endpoint include `flaggedForReview` count and/or counts by `lineOfBusiness`? (See G-04) | **Yes** — both `flaggedCount` and `countByLineOfBusiness` included |
| 9 | What timeframe defines "expiring soon" in the summary endpoint? (See G-13) | **30 calendar days** from current UTC date, Active policies only |
| 10 | What is the `sort` query parameter format and is the premium sort field `premium` or `premiumAmount`? (See G-14) | **Comma-separated** `sort=field,direction`; premium field is **`premiumAmount`** |

---

## Deliverables

The following deliverables are required by the assessment. Each is tracked here for completeness alongside the engineering artefacts.

| # | Deliverable | Notes |
|---|-------------|-------|
| D-01 | Git repository with meaningful commit history | Development process should be visible through commit granularity |
| D-02 | Working service startable locally, ideally via `docker-compose up` | Multi-stage Dockerfile and `docker-compose.yml` required |
| D-03 | OpenAPI specification file (YAML or JSON) as the API contract | Contract-first — spec written before implementation. Lives under `docs/openapi/`. |
| D-04 | AI working journal — prompt log showing what was accepted, challenged, and overridden, with brief reasoning | Committed as a running notes file alongside code; does not need to be polished |
| D-05 | Supporting documentation — architecture decisions, design rationale, trade-off analysis, diagrams | Recommended: ADRs, C4 diagrams, this analysis document |
| D-06 | 30–60 minute walkthrough with the hiring panel | Four segments: presentation (15–20 min), panel Q&A (10–15 min), "what next" (10 min), candidate questions (5 min) |
