# Review: src/PolicyManagement.Infrastructure — 2026-06-17 16:12

**Branch:** `feat/domain-layer`
**Scope:** All files under `src/PolicyManagement.Infrastructure/` plus related API wiring in `Program.cs` and `appsettings*.json`.

---

## Review Summary

**Overall assessment:** `REQUEST CHANGES`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 5 |
| Warning (should fix) | 4 |
| Suggestion (nice to have) | 3 |

---

## Critical Issues

### [CRIT-1] `appsettings.Development.json` contains a hardcoded SA password template — `${SA_PASSWORD}` is a literal string, not substitution

- **File:** `src/PolicyManagement.API/appsettings.Development.json`
- **Line:** 10
- **Rule:** Security — "No connection strings with plaintext passwords in `appsettings.json` or `appsettings.Development.json`" (`.github/copilot-instructions.md`; `.github/skills/authentication.md` OWASP reference)
- **Description:** The Development connection string is:
  ```json
  "DefaultConnection": "Server=localhost,1433;Database=PolicyManagement;User Id=sa;Password=${SA_PASSWORD};TrustServerCertificate=True;"
  ```
  The `${SA_PASSWORD}` token looks like a substitution variable, but **ASP.NET Core's `IConfiguration` does not perform shell-style variable substitution**. The literal string `${SA_PASSWORD}` will be passed to SQL Server as the password, causing a connection failure. This file is committed to source control and provides a false sense of safety — developers will assume the password is injected at runtime and not realise the connection will always fail.

  Additionally, even if this were a valid placeholder comment, committing a template connection string that includes a password field in `appsettings.Development.json` violates the requirement that no credentials appear in tracked files.
- **Suggested fix:** Remove the connection string from `appsettings.Development.json` entirely. Developers should supply the connection string via:
  - User Secrets (`dotnet user-secrets set "ConnectionStrings:DefaultConnection" "..."`) for local development
  - Environment variable `ConnectionStrings__DefaultConnection` for Docker
  Add a comment in the file pointing to the `.env.example` file:
  ```json
  {
    "Logging": { ... },
    "ConnectionStrings": {
      // Set via user-secrets: dotnet user-secrets set "ConnectionStrings:DefaultConnection" "<value>"
      // Or via environment variable: ConnectionStrings__DefaultConnection
      // See .env.example for the full template.
    }
  }
  ```

---

### [CRIT-2] `CacheOptions` is not registered with `ValidateOnStart()` — misconfiguration will not be caught at startup

- **File:** `src/PolicyManagement.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`
- **Lines:** 74–77
- **Rule:** Configuration — "Options classes are registered with `services.Configure<T>()`... `JwtOptions` registered with `ValidateOnStart()`" (`.github/copilot-instructions.md`); Production Readiness — validate configuration at startup (`.github/skills/production-readiness.md`)
- **Description:** The current registration is:
  ```csharp
  services.Configure<CacheOptions>(opts =>
  {
      configuration.GetSection(CacheOptions.SectionName).Bind(opts);
  });
  ```
  This pattern:
  1. Uses a manual `Bind` inside a `Configure` delegate rather than the idiomatic `.BindConfiguration()` fluent API.
  2. Does not call `ValidateOnStart()` — if `Cache:PolicyTtlSeconds` is missing or zero in production, the error will only surface when a handler first attempts to use the cache (runtime failure, not startup failure).
  3. Does not validate that `PolicyTtlSeconds > 0` and `SummaryTtlSeconds > 0`.
- **Suggested fix:** Replace with the standard options builder pattern:
  ```csharp
  services.AddOptions<CacheOptions>()
      .BindConfiguration(CacheOptions.SectionName)
      .ValidateDataAnnotations()
      .ValidateOnStart();
  ```
  Add `[Range(1, int.MaxValue)]` data annotations to `PolicyTtlSeconds` and `SummaryTtlSeconds` in `CacheOptions` to enforce positive values.

---

### [CRIT-3] `Program.cs` is missing the complete middleware pipeline — `GlobalExceptionMiddleware`, `CorrelationIdMiddleware`, authentication, and authorization are all absent

- **File:** `src/PolicyManagement.API/Program.cs`
- **Rule:** Middleware pipeline order — "CorrelationIdMiddleware → GlobalExceptionMiddleware → UseAuthentication() → UseAuthorization() → MapControllers()" (`copilot-instructions.md` — Middleware Pipeline Order; `.github/skills/authentication.md` — Section 2); Error Handling — "GlobalExceptionMiddleware is registered in Program.cs" (`.github/skills/error-handling.md`)
- **Description:** The current `Program.cs` registers only `MapControllers()` and health check endpoints — no middleware pipeline at all. Missing:
  - `app.UseMiddleware<CorrelationIdMiddleware>()` — all log entries lack correlation IDs
  - `app.UseMiddleware<GlobalExceptionMiddleware>()` — unhandled exceptions will return bare 500 HTML pages, not `ProblemDetails`
  - `app.UseAuthentication()` — all JWT Bearer validation is skipped; **all endpoints are effectively unauthenticated**
  - `app.UseAuthorization()` — `[Authorize]` attributes on the controller are never enforced
  - `builder.Services.AddApplication()` is registered but `LoggingPipelineBehavior` (still missing per the Application layer review) is not wired
  - `JwtOptions` binding and `ValidateOnStart()` is absent
  - `builder.Services.AddAuthorization(...)` with the `PolicyWrite` policy is absent

  The absence of `UseAuthentication()` and `UseAuthorization()` means every API endpoint is currently open to unauthenticated access — a **Critical security vulnerability**.
- **Suggested fix:** Follow the exact pipeline order from `.github/skills/authentication.md` Section 2:
  ```csharp
  app.UseMiddleware<CorrelationIdMiddleware>();
  app.UseMiddleware<GlobalExceptionMiddleware>();
  app.UseAuthentication();
  app.UseAuthorization();
  app.MapControllers();
  app.MapHealthChecks("/health/live", ...);
  app.MapHealthChecks("/health/ready", ...);
  ```
  Also add JWT service registration and authorization policy registration to the service configuration section.

---

### [CRIT-4] `Program.cs` missing `OpenAPI`/`Swagger` configuration gated behind `IsDevelopment()`

- **File:** `src/PolicyManagement.API/Program.cs`
- **Rule:** Security — "Swagger UI is gated behind `app.Environment.IsDevelopment()` — not enabled in production by default" (`.github/copilot-instructions.md`)
- **Description:** `Microsoft.AspNetCore.OpenApi` is referenced in `PolicyManagement.API.csproj`, `Serilog.AspNetCore` and other packages are installed, but none of them are configured in `Program.cs`. The Swagger UI is neither enabled nor gated. While not exposing Swagger in production is technically correct (it is simply absent), the API documentation feature that developers need in development is also not present. The `Microsoft.AspNetCore.OpenApi` package registration implies the intent to serve Swagger.
- **Suggested fix:** Add the Swagger/OpenAPI registration gated by environment:
  ```csharp
  if (app.Environment.IsDevelopment())
  {
      app.MapOpenApi();
      // or if using Swashbuckle: app.UseSwagger(); app.UseSwaggerUI();
  }
  ```
  Also configure Serilog via `builder.Host.UseSerilog(...)` since `Serilog.AspNetCore` is installed but not wired.

---

### [CRIT-5] No Infrastructure layer unit/integration tests

- **File:** `tests/PolicyManagement.Infrastructure.Tests/` (project exists; no `.cs` test files)
- **Rule:** Testing Standards — "Infrastructure: Integration tests where external deps are involved" (`.github/copilot-instructions.md` — Coverage Requirements; `.github/skills/testing-standards.md`)
- **Description:** The `PolicyManagement.Infrastructure.Tests` project was committed with only generated build files and no test code. The following behaviour requires integration test coverage:
  - `PolicyRepository.GetByIdAsync` — returns entity, returns null on miss
  - `PolicyRepository.GetPagedAsync` — all filter combinations, sort fields, pagination
  - `PolicyRepository.GetSummaryAsync` — correct aggregation values including `PremiumByCurrency`
  - `PolicyRepository.UpdateRangeAsync` — entities are persisted and `UpdatedAt` is set by `SaveChangesAsync`
  - `PolicyRepository.ExistAllAsync` — all found, partial match returns false
  - `PolicyDbContext.SaveChangesAsync` — `CreatedAt` set on insert, `UpdatedAt` updated on modify
  - `InMemoryCacheService` — get/set/remove round-trip; TTL expiry behaviour
  - `PolicySeeder.Generate()` — produces exactly 210 records; all GUIDs are unique; `expiryDate > effectiveDate` invariant

  These tests should use an in-memory SQLite or SQL Server LocalDB provider — not the production connection string.
- **Suggested fix:** Create integration tests under `tests/PolicyManagement.Infrastructure.Tests/Persistence/Repositories/PolicyRepositoryTests.cs` using EF Core's `UseInMemoryDatabase` or `UseSqlite`. Use `PolicyBuilder` from the Application test project (or a local copy) for test data construction.

---

## Warnings

### [WARN-1] `GetSummaryAsync` loads all policy rows into memory before aggregating

- **File:** `src/PolicyManagement.Infrastructure/Persistence/Repositories/PolicyRepository.cs`
- **Lines:** 72–118
- **Rule:** EF Core Conventions — read queries should be efficient; "AsNoTracking() is called on every read query" (`.github/skills/database-conventions.md`)
- **Description:** `GetSummaryAsync` materialises all rows from `Policies` into a .NET `List<anonymous>` before performing `GroupBy`, `Sum`, and `Count` operations in LINQ-to-objects. For the 200-row seed dataset this is acceptable, but as the table grows (thousands of rows), this approach:
  - Loads every row's `Status`, `LineOfBusiness`, `Region`, `Currency`, `PremiumAmount`, `FlaggedForReview`, and `ExpiryDate` across the network into the API process.
  - Performs all aggregation in-process rather than in the database engine where these operations are O(log N) with indexes.
  - Will cause performance degradation and increased memory pressure under production load.

  The summary query is cached (`policy:v1:summary`, TTL 60s), which mitigates frequency, but a single cache miss for a large table will be slow.
- **Suggested fix:** Push aggregation to the database using server-side `GroupBy` LINQ where possible. Note that EF Core can translate simple `GroupBy().Count()` and `Sum()` to SQL `GROUP BY`. For the multi-dimension grouping required here (by status, LOB, region, currency), consider multiple targeted queries or a SQL view. At minimum, document the known limitation with a `// TODO:` comment noting the in-memory aggregation approach and the table-size threshold at which it should be revisited.

---

### [WARN-2] `CacheOptions` is defined in `Infrastructure` layer — it should be in `Application`

- **File:** `src/PolicyManagement.Infrastructure/Options/CacheOptions.cs`
- **Rule:** Clean Architecture — "ICacheService interface is defined in Application... Bind configuration sections to strongly-typed options classes" (`ADR-004`; `.github/skills/clean-architecture.md`)
- **Description:** `CacheOptions` is in `PolicyManagement.Infrastructure/Options/`. The handlers (`GetPolicyByIdQueryHandler`, `GetPolicySummaryQueryHandler`) will need to know the TTL values to pass to `ICacheService.SetAsync()`. If `CacheOptions` lives in Infrastructure, the Application layer handlers cannot access it without adding a dependency on the Infrastructure project — which violates Clean Architecture.

  The correct placement is `Application/Options/CacheOptions.cs` so that handlers can inject `IOptions<CacheOptions>` without crossing layer boundaries. The Infrastructure `InMemoryCacheService` already receives `IOptions<CacheOptions>` — it can continue to do so regardless of which layer defines the class, as long as the class is in `Application` where both layers can reference it.
- **Suggested fix:** Move `CacheOptions` to `src/PolicyManagement.Application/Options/CacheOptions.cs` and update all `using` statements.

---

### [WARN-3] `PolicyDbContext.SeedAsync` calls `base.SaveChangesAsync` bypassing the audit interceptor — `CreatedAt`/`UpdatedAt` will be zero/default on seed records

- **File:** `src/PolicyManagement.Infrastructure/Persistence/PolicyDbContext.cs`
- **Line:** 62
- **Rule:** EF Core Conventions — "Set once on insert; never updated" (`docs/architecture/policy-management-architecture.md`; `.github/skills/database-conventions.md`)
- **Description:** `SeedAsync` calls `base.SaveChangesAsync(cancellationToken)` (bypassing the overridden `SaveChangesAsync`) to avoid the audit interceptor setting the timestamps to `DateTimeOffset.UtcNow`. The comment explains this is intentional: seed data carries its own timestamps from `PolicySeeder.BuildPolicy()` (e.g., `baseDate.AddDays(index)`).

  However, bypassing the override creates two risks:
  1. Future developers adding other `SaveChanges` overrides or interceptors may not realise seed data bypasses them.
  2. If the `Policy` entity's `CreatedAt` and `UpdatedAt` use `private set` and the EF Core property API is used to set them in the override (`entry.Property(nameof(...)).CurrentValue = now`), calling `base.SaveChangesAsync` will not set them at all — seed records need their audit fields set before `AddRangeAsync` is called.

  Inspection of `PolicySeeder.BuildPolicy()` confirms seed records do have `CreatedAt` and `UpdatedAt` set via `Policy.Create(now: createdAt)` and `policy.Flag(createdAt.AddDays(1))`, so the values are correct. The concern is the fragility of the `base.SaveChangesAsync` bypass pattern.
- **Suggested fix:** Instead of bypassing the override, explicitly set audit fields on seed entities in `PolicySeeder.BuildPolicy()` (already done) and call the normal `SaveChangesAsync` (the override). Since the seed entities already have `CreatedAt` and `UpdatedAt` set before being tracked, the `ChangeTracker.Entries<IAuditableEntity>()` loop in the override will overwrite them with `DateTimeOffset.UtcNow` — which may or may not be desired.

  The cleanest solution is to not use `base.SaveChangesAsync` and instead add a `bool skipAudit` parameter or use a flag field on the context. Alternatively, document the bypass explicitly with a `// INTENTIONAL: bypasses audit timestamp override to preserve seeder-generated timestamps` comment and add a regression test that verifies seed record timestamps match the seeder's values.

---

### [WARN-4] `InMemoryCacheService` is registered as `Singleton` but `IOptions<CacheOptions>` it depends on may be `Scoped` if `ValidateOnStart` is used

- **File:** `src/PolicyManagement.Infrastructure/Extensions/InfrastructureServiceExtensions.cs`
- **Line:** 78
- **Rule:** Configuration — options pattern registration; DI lifetime correctness (`.github/copilot-instructions.md`)
- **Description:** `InMemoryCacheService` is registered as `Singleton`:
  ```csharp
  services.AddSingleton<ICacheService, InMemoryCacheService>();
  ```
  It depends on `IOptions<CacheOptions>` in its constructor. `IOptions<T>` is registered as a `Singleton` by the ASP.NET Core options framework, so this lifetime is correct. However, if this is ever changed to `IOptionsSnapshot<T>` (scoped, for per-request config reloading), the singleton dependency will capture a scoped service — a classic captive dependency bug.

  Currently this is not a bug (both are Singleton), but the pattern creates a latent risk.
- **Suggested fix:** Add a comment in `InfrastructureServiceExtensions` noting that `ICacheService` is a Singleton because `IMemoryCache` is a Singleton and the cache state must be shared across requests. Add a note that `IOptions<T>` (not `IOptionsSnapshot<T>`) is required for constructor injection into Singleton services.

---

## Suggestions

### [SUGG-1] `PolicyDbContext` uses property API (`entry.Property(...).CurrentValue`) — the skill file example uses `entry.Entity.{Property}` directly

- **File:** `src/PolicyManagement.Infrastructure/Persistence/PolicyDbContext.cs`
- **Lines:** 43–46
- **Rule:** EF Core Conventions — audit column setting pattern (`.github/skills/database-conventions.md`)
- **Description:** The skill file shows:
  ```csharp
  entry.Entity.CreatedAt = now;
  entry.Entity.UpdatedAt = now;
  ```
  The implementation uses:
  ```csharp
  entry.Property(nameof(IAuditableEntity.CreatedAt)).CurrentValue = now;
  entry.Property(nameof(IAuditableEntity.UpdatedAt)).CurrentValue = now;
  ```
  The property API approach is actually safer for entities with `private set` properties (since `entry.Entity.CreatedAt = now` would not compile if the setter is private). The implementation is therefore more correct than the skill file example for this entity design. This is a style divergence from the skill file, not a bug — the implementation is preferable.
- **Suggested fix:** Update the skill file example to use the property API approach, or add a comment in `PolicyDbContext` explaining why the property API is used instead of direct assignment.

---

### [SUGG-2] `PolicySeeder` uses `int`-based deterministic GUIDs (`new Guid(index, 0, 0, ...)`) — first 256 policies will have version 0 GUIDs which are not RFC 4122 compliant

- **File:** `src/PolicyManagement.Infrastructure/Persistence/Seed/PolicySeeder.cs`
- **Line:** 162
- **Rule:** Code Quality — correctness of GUID generation (`copilot-instructions.md`)
- **Description:** `new Guid(index, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)` produces GUIDs like `00000001-0000-0000-0000-000000000000`. These are technically valid `Guid` values in .NET but are not RFC 4122 compliant (version and variant bits are 0). For a seed dataset in a development environment, this is acceptable. If these GUIDs appear in test assertions or API responses, they may cause confusion.
- **Suggested fix (optional):** Use `Guid.Parse($"00000000-0000-0000-0000-{index:D12}")` for more readable deterministic IDs, or document the current format explicitly.

---

### [SUGG-3] `GetPagedAsync` uses `.ToLower()` on `filter.Search` in-memory but the LIKE predicate runs in SQL

- **File:** `src/PolicyManagement.Infrastructure/Persistence/Repositories/PolicyRepository.cs`
- **Lines:** 50–56
- **Rule:** Code Quality — SQL vs in-memory execution clarity (`copilot-instructions.md`)
- **Description:**
  ```csharp
  var term = filter.Search.ToLower();
  query = query.Where(p =>
      p.PolicyNumber.ToLower().Contains(term) || ...);
  ```
  EF Core translates `.ToLower().Contains(term)` to `LOWER(column) LIKE '%term%'` in SQL Server. However, SQL Server string comparisons are case-insensitive by default with the standard `Latin1_General_CI_AS` collation. Calling `ToLower()` is redundant with a case-insensitive collation and adds a computed `LOWER()` call that prevents index usage on `policy_number` and `policyholder_name`.
- **Suggested fix:** Remove the `.ToLower()` calls and rely on SQL Server's default CI (case-insensitive) collation:
  ```csharp
  query = query.Where(p =>
      p.PolicyNumber.Contains(filter.Search) ||
      p.PolicyholderName.Contains(filter.Search) ||
      p.Underwriter.Contains(filter.Search));
  ```
  Add a comment noting that case-insensitivity is handled by the `Latin1_General_CI_AS` database collation.

---

## What Looks Good

- **`PolicyManagement.Infrastructure.csproj`** correctly references only `Domain` and `Application`. No API or ASP.NET Core project references. Only appropriate NuGet packages (EF Core SqlServer, EF Core Tools, Memory Cache, Logging Abstractions, Options). Fully compliant with `ADR-001`.
- **`PolicyConfiguration`** implements all 13 ADR-006 indexes: 8 single-column filtered + 3 composite filtered + `UQ_Policies_PolicyNumber` unique + `PK_Policies`. Each has the correct `HasFilter("is_deleted = 0")` clause. Index names match the ADR exactly.
- **`PolicyConfiguration`** uses the custom `ValueConverter<LineOfBusiness, string>` correctly — `AH` ↔ `"A&H"` mapping is explicit and resolves `CRIT-1` from the domain layer review. The `PolicyStatus` enum uses the simpler `HasConversion<string>()` which is correct (no special chars).
- **`PolicyConfiguration`** has **zero EF Core data annotations** on the entity — all mapping is Fluent API. Column types match the architecture spec exactly: `varchar(20)` for `policy_number`, `nvarchar(200)` for `policyholder_name`, `decimal(18,2)` for `premium_amount`, `date` for date columns, `datetimeoffset(7)` for audit timestamps.
- **Global query filter** `p => !p.IsDeleted` is applied in `PolicyConfiguration.Configure()` — correct placement per `ADR-001`. Soft-deleted records are automatically excluded.
- **`PolicyRepository.GetByIdAsync`** correctly uses `.AsNoTracking()`. **`GetPagedAsync`** correctly calls `CountAsync` before paging (counts the full filtered set, not just the current page). **`UpdateRangeAsync`** correctly omits `.AsNoTracking()` to enable tracking for the update.
- **`PolicyRepository.ExistAllAsync`** uses a single `CountAsync` with a `Contains` predicate — avoids N+1 queries.
- **`PolicyRepository.ApplySort`** uses a `switch` expression with `ToLowerInvariant()` matching — clean and exhaustive, with `createdAt` as the safe default for unknown sort fields.
- **`InMemoryEventPublisher`** uses structured logging parameters (`{EventType} {@Event}`) — not string interpolation. Compliant with logging standards.
- **`InMemoryEventPublisher`** is documented as Kafka-swappable via DI registration change only — correct per `ADR-005`.
- **`PolicySeeder.Generate()`** uses `Policy.Create()` factory method and `policy.Flag()` domain method — never bypasses domain encapsulation by setting properties directly. Deterministic index-based GUIDs ensure idempotency.
- **`IAuditableEntity`** is now implemented by `Policy` — resolves `WARN-2` from the domain layer review (`review-20260617-1612-uncommitted.md`).
- **`SortDirection` enum** and **`PolicySortFields` constants class** are now present in the Domain layer — resolves `SUGG-2` from the domain layer review.
- **`PolicySummaryData`** now includes `PremiumByCurrency: IReadOnlyDictionary<string, decimal>` — resolves `WARN-1` from the Application layer review (`review-20260617-1612-application-layer.md`) and fixes the compile error (`CRIT-1` in that review).
- **`InitialCreate` migration** matches the `PolicyConfiguration` Fluent API exactly — all 15 columns with correct SQL types, all 13 indexes with correct filter clauses. The `Down()` method correctly drops the entire `Policies` table.
- **`SA_PASSWORD`, `KEYCLOAK_ADMIN`, `KEYCLOAK_ADMIN_PASSWORD`** in `docker-compose.yml` all use `${...}` substitution — no hardcoded credentials in any infrastructure file.
