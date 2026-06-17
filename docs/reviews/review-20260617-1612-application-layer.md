# Review: src/PolicyManagement.Application — 2026-06-17 16:12

**Branch:** `feat/domain-layer`
**Scope:** All files under `src/PolicyManagement.Application/` reviewed against the OpenAPI spec, architecture document, skill files, and `copilot-instructions.md`.

---

## Review Summary

**Overall assessment:** `REQUEST CHANGES`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 5 |
| Warning (should fix) | 3 |
| Suggestion (nice to have) | 2 |

---

## Critical Issues

### [CRIT-1] Compile error — `data.PremiumByCurrency` does not exist on `PolicySummaryData`

- **File:** `src/PolicyManagement.Application/Mappings/PolicyMappingExtensions.cs`
- **Line:** 62
- **Rule:** Clean Architecture — Application must depend only on Domain types as they actually exist (`ADR-001`; `.github/skills/clean-architecture.md`)
- **Description:** `ToPolicySummaryResponse()` calls `data.PremiumByCurrency` on a `PolicySummaryData` instance. The `PolicySummaryData` domain record (committed in `feat/domain-layer`) only has `TotalPremium: decimal` — it has no `PremiumByCurrency` property. This is a compile-time error; the solution will not build.
  
  The root cause is a domain model deficiency (see [WARN-1] below): `PolicySummaryData` needs an additional field `IReadOnlyDictionary<string, decimal> PremiumByCurrency` to support the per-currency premium totals required by the OpenAPI spec (`premiumTotalByCurrency`) and the `PolicySummaryResponse` DTO.
- **Suggested fix (two-step):**
  1. **Domain fix:** Add `IReadOnlyDictionary<string, decimal> PremiumByCurrency` as a parameter to the `PolicySummaryData` record in `src/PolicyManagement.Domain/Models/PolicySummaryData.cs`.
  2. **Application fix (already correct once domain is fixed):** The mapping code in `ToPolicySummaryResponse()` will compile once `data.PremiumByCurrency` exists. No change needed in the mapping extension itself.

---

### [CRIT-2] `PaginatedResponse<T>` does not match the OpenAPI `PagedPolicyResponse` schema

- **File:** `src/PolicyManagement.Application/DTOs/PaginatedResponse.cs`
- **Lines:** 1–24
- **Rule:** Contract-first API design — "All request and response schemas must match the OpenAPI spec exactly" (`.github/skills/contract-first-api.md`; `.github/copilot-instructions.md`)
- **Description:** The OpenAPI specification defines `PagedPolicyResponse` as:
  ```yaml
  PagedPolicyResponse:
    properties:
      data:           # array of PolicyDto
      pagination:     # nested PaginationMeta object
        properties:
          page, size, totalCount, totalPages
  ```
  The `PaginatedResponse<T>` DTO flattens all fields at the top level (`Items`, `Page`, `Size`, `TotalCount`, `TotalPages`). When serialised by `System.Text.Json` this produces:
  ```json
  { "items": [...], "page": 1, "size": 20, "totalCount": 200, "totalPages": 10 }
  ```
  The spec requires:
  ```json
  { "data": [...], "pagination": { "page": 1, "size": 20, "totalCount": 200, "totalPages": 10 } }
  ```
  Two violations:
  - `items` → must be `data` (OpenAPI `data` property)
  - Flat structure → must be nested under a `pagination` object
- **Suggested fix:** Redesign the DTO to match the OpenAPI shape:
  ```csharp
  // Application/DTOs/PaginationMeta.cs
  public sealed record PaginationMeta(int Page, int Size, int TotalCount, int TotalPages);

  // Application/DTOs/PagedPolicyResponse.cs
  public sealed record PagedPolicyResponse(
      IReadOnlyList<PolicyDto> Data,
      PaginationMeta Pagination);
  ```
  Also rename the file and class from `PaginatedResponse<T>` to `PagedPolicyResponse` to match both the OpenAPI schema name and the architecture document's `PagedResponse<T>` reference.

---

### [CRIT-3] All CQRS feature files are absent — no handlers, commands, queries, or validators

- **File:** `src/PolicyManagement.Application/Features/` (directory does not exist)
- **Rule:** Application layer responsibilities — "Contains all CQRS commands, queries, and their handlers" (`docs/architecture/policy-management-architecture.md`; `.github/skills/cqrs-mediator.md`)
- **Description:** The `Features/Policies/` folder and all its contents are missing. The following files are required and have not been created:

  | File | Status |
  |---|---|
  | `Features/Policies/Commands/FlagPolicies/FlagPoliciesCommand.cs` | Missing |
  | `Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandHandler.cs` | Missing |
  | `Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandValidator.cs` | Missing |
  | `Features/Policies/Queries/GetPolicies/GetPoliciesQuery.cs` | Missing |
  | `Features/Policies/Queries/GetPolicies/GetPoliciesQueryHandler.cs` | Missing |
  | `Features/Policies/Queries/GetPolicies/GetPoliciesQueryValidator.cs` | Missing |
  | `Features/Policies/Queries/GetPolicyById/GetPolicyByIdQuery.cs` | Missing |
  | `Features/Policies/Queries/GetPolicyById/GetPolicyByIdQueryHandler.cs` | Missing |
  | `Features/Policies/Queries/GetPolicySummary/GetPolicySummaryQuery.cs` | Missing |
  | `Features/Policies/Queries/GetPolicySummary/GetPolicySummaryQueryHandler.cs` | Missing |

  `ApplicationServiceExtensions.AddApplication()` uses `RegisterServicesFromAssembly(assembly)` which will find nothing to register. All four API endpoints will be unroutable once controllers are wired.

- **Suggested fix:** Implement all missing files per the folder structure in `.github/skills/cqrs-mediator.md`. Commands return `Unit` (via `IRequest` with no type parameter). Query handlers must follow the cache-aside pattern (`ADR-004`). `FlagPoliciesCommandHandler` must call `IPolicyRepository.UpdateRangeAsync`, publish one `PolicyFlaggedEvent` per flagged policy, and invalidate `policy:v1:summary` after the commit.

---

### [CRIT-4] `LoggingPipelineBehavior` class is absent and not registered

- **File:** `src/PolicyManagement.Application/Behaviours/` (only `ValidationPipelineBehavior.cs` exists; `LoggingPipelineBehavior.cs` is missing)
- **Rule:** Architecture requirement — "`LoggingPipelineBehavior<,>` is the outermost behaviour: logs handler entry, exit, and elapsed time" (`docs/architecture/policy-management-architecture.md` — MediatR pipeline behaviours table; `.github/skills/cqrs-mediator.md` — Execution Order)
- **Description:** The architecture mandates two pipeline behaviours in this order:
  1. `LoggingPipelineBehavior` (outermost — logs start, duration, errors)
  2. `ValidationPipelineBehavior` (inner — validates before handler runs)

  `LoggingPipelineBehavior` does not exist in the codebase. `ApplicationServiceExtensions.AddApplication()` only registers `ValidationPipelineBehavior`. Without `LoggingPipelineBehavior`:
  - No request/response timing is logged.
  - Exceptions from handlers are not logged with elapsed time context before propagating to the API middleware.
  - The behaviour pipeline is non-compliant with the documented architecture.
- **Suggested fix:** Create `src/PolicyManagement.Application/Behaviours/LoggingPipelineBehavior.cs` following the pattern in `.github/skills/cqrs-mediator.md` (using `Stopwatch`, structured logging parameters, re-throwing exceptions). Register it **before** `ValidationPipelineBehavior` in `ApplicationServiceExtensions`:
  ```csharp
  services.AddMediatR(cfg =>
  {
      cfg.RegisterServicesFromAssembly(assembly);
      cfg.AddOpenBehavior(typeof(LoggingPipelineBehavior<,>));   // first = outermost
      cfg.AddOpenBehavior(typeof(ValidationPipelineBehavior<,>)); // second = inner
  });
  ```

---

### [CRIT-5] No Application layer unit tests

- **File:** `tests/PolicyManagement.Application.Tests/` (project exists; no `.cs` test files)
- **Rule:** Testing Standards — "Application layer (handlers, services, validators): Unit tests — all public methods" (`.github/copilot-instructions.md` — Coverage Requirements; `.github/skills/testing-standards.md`)
- **Description:** The `PolicyManagement.Application.Tests` project contains only a `.csproj` file. Once the feature handlers and validators are implemented ([CRIT-3]), unit tests are required before merge for:
  - `GetPolicyByIdQueryHandler` — cache hit, cache miss, not found
  - `GetPoliciesQueryHandler` — paged results, empty results
  - `GetPolicySummaryQueryHandler` — cache hit, cache miss
  - `FlagPoliciesCommandHandler` — successful flag, policy not found, cache invalidation
  - `GetPoliciesQueryValidator` — `page`, `size`, `sort`, `status`, `lineOfBusiness` boundary cases
  - `FlagPoliciesCommandValidator` — empty list, over 100 IDs, duplicate GUIDs, empty GUIDs
  - `PolicyMappingExtensions` — `ToDto()` field mapping, `ToLineOfBusinessString()` for `AH` → `"A&H"`
- **Suggested fix:** Create the folder structure under `tests/PolicyManagement.Application.Tests/Features/` as documented in `.github/skills/testing-standards.md`. Use xUnit, FluentAssertions, and Moq. Add `PolicyBuilder.cs` under `tests/PolicyManagement.Application.Tests/Builders/`. Test method naming: `{Method}_When{Condition}_Should{Expected}`.

---

## Warnings

### [WARN-1] `PolicySummaryData` domain model is missing `PremiumByCurrency` field required by the OpenAPI spec

- **File:** `src/PolicyManagement.Domain/Models/PolicySummaryData.cs` (domain layer — reported here as it blocks the Application mapping)
- **Rule:** OpenAPI contract source of truth — `premiumTotalByCurrency` is a `required` field in the `PolicySummaryResponse` schema (`docs/openapi/policy-management-api.yaml`, line 444)
- **Description:** `PolicySummaryData` has `TotalPremium: decimal` (a single scalar). The OpenAPI spec and `PolicySummaryResponse` DTO require per-currency premium totals (`premiumTotalByCurrency`: a dictionary keyed by currency code). The repository implementation (`GetSummaryAsync`) will need to group and aggregate premiums by currency, but the domain return type cannot carry that data. This is the root cause of [CRIT-1].
- **Suggested fix:** Replace `TotalPremium: decimal` with `PremiumByCurrency: IReadOnlyDictionary<string, decimal>` in `PolicySummaryData`, or add it alongside `TotalPremium` if the scalar total is also useful internally:
  ```csharp
  public sealed record PolicySummaryData(
      int TotalPolicies,
      int FlaggedCount,
      int ExpiringSoonCount,
      IReadOnlyDictionary<PolicyStatus, int> CountByStatus,
      IReadOnlyDictionary<LineOfBusiness, int> CountByLineOfBusiness,
      IReadOnlyDictionary<string, int> CountByRegion,
      IReadOnlyDictionary<string, decimal> PremiumByCurrency);  // replaces TotalPremium
  ```

---

### [WARN-2] Architecture document specifies AutoMapper; implementation uses static extension methods without AutoMapper

- **File:** `src/PolicyManagement.Application/Mappings/PolicyMappingExtensions.cs`; `src/PolicyManagement.Application/PolicyManagement.Application.csproj`
- **Rule:** Architecture documentation accuracy — "Application layer contains... AutoMapper mapping profiles" (`docs/architecture/policy-management-architecture.md`; `.github/copilot-instructions.md`)
- **Description:** The `PolicyMappingExtensions` uses static extension methods rather than an AutoMapper `Profile` subclass. The `AutoMapper` NuGet package is not referenced in the `.csproj`. This diverges from the documented architecture, which states "AutoMapper mapping profiles" in the Application layer responsibilities table.

  The static extension approach is arguably preferable (zero reflection overhead, compile-time safety, explicit trace), but the decision to deviate from the documented architecture must be acknowledged and recorded.
- **Suggested fix (two options):**
  - **Option A (preferred):** Update the architecture document (`docs/architecture/policy-management-architecture.md`) and `copilot-instructions.md` to replace "AutoMapper mapping profiles" with "manual mapping extension methods". Add a brief ADR note or inline comment explaining the rationale (compile-time safety, no reflection).
  - **Option B:** Add `AutoMapper` to the `.csproj` and replace `PolicyMappingExtensions` with a `PolicyMappingProfile : Profile` class. This aligns with the documented approach but adds overhead.

---

### [WARN-3] `PaginatedResponse<T>` naming deviates from the architecture document and OpenAPI schema name

- **File:** `src/PolicyManagement.Application/DTOs/PaginatedResponse.cs`
- **Line:** 13 (class declaration)
- **Rule:** Naming Conventions — DTOs named `{Entity}Dto` or `{Entity}Response`; OpenAPI schema name is the source of truth (`docs/architecture/policy-management-architecture.md` — "`PagedResponse<T>`"; `docs/openapi/policy-management-api.yaml` — `PagedPolicyResponse`)
- **Description:** The architecture document refers to this type as `PagedResponse<T>`. The OpenAPI spec uses the schema name `PagedPolicyResponse`. The implementation uses `PaginatedResponse<T>`. This inconsistency across three documents will cause confusion for future developers and diverges from the contract-first principle. This warning is superseded in severity by [CRIT-2] which requires restructuring the DTO anyway — fixing [CRIT-2] should also resolve this naming issue.
- **Suggested fix:** Rename to `PagedPolicyResponse` when fixing [CRIT-2] (non-generic, matching the OpenAPI schema name). Update the architecture doc to reflect the final name.

---

## Suggestions

### [SUGG-1] `ICurrentUserService` does not define a constant for the `Policy.Write` role name

- **File:** `src/PolicyManagement.Application/Interfaces/ICurrentUserService.cs`
- **Rule:** Code Quality — "No magic strings or magic numbers — constants or enums used throughout" (`.github/copilot-instructions.md`)
- **Description:** `ICurrentUserService.IsInRole()` accepts a `string role` parameter. The calling site in any future handler will need to pass `"Policy.Write"` as a literal string. There is no constant defined in the Application layer to represent this role name. Multiple call sites will duplicate the string literal.
- **Suggested fix:** Add a nested static class or companion class:
  ```csharp
  /// <summary>Well-known role names used for authorization checks.</summary>
  public static class Roles
  {
      /// <summary>Grants write access to policy flagging operations.</summary>
      public const string PolicyWrite = "Policy.Write";
  }
  ```
  Place it in `src/PolicyManagement.Application/Interfaces/Roles.cs` or as a nested class within the interface file.

---

### [SUGG-2] `ValidationPipelineBehavior` uses `ValidateAsync` in parallel but shares a single `ValidationContext`

- **File:** `src/PolicyManagement.Application/Behaviours/ValidationPipelineBehavior.cs`
- **Lines:** 38–44
- **Rule:** Code Quality — correctness of async parallel validation (`.github/copilot-instructions.md`)
- **Description:** `Task.WhenAll(validators.Select(v => v.ValidateAsync(context, cancellationToken)))` creates one `ValidationContext<TRequest>` and passes it to all validators concurrently. FluentValidation's `ValidationContext<T>` is not documented as thread-safe for concurrent access. While most validators are stateless and this is unlikely to cause issues in practice, the shared context could cause data races if any validator modifies context state (e.g., `context.MessageFormatter`, `context.RootContextData`).
- **Suggested fix:** Create a new `ValidationContext<TRequest>` per validator invocation, or run validators sequentially rather than in parallel (validators are typically fast enough that parallelism provides no measurable benefit):
  ```csharp
  var failures = new List<ValidationFailure>();
  foreach (var validator in validators)
  {
      var result = await validator.ValidateAsync(
          new ValidationContext<TRequest>(request), cancellationToken);
      failures.AddRange(result.Errors.Where(f => f is not null));
  }
  ```

---

## What Looks Good

- **`PolicyManagement.Application.csproj`** correctly references only `PolicyManagement.Domain` and adds only Application-appropriate NuGet packages (`MediatR`, `FluentValidation`, `FluentValidation.DependencyInjectionExtensions`). No EF Core, no ASP.NET Core packages — fully compliant with `ADR-001`.
- **`ICacheService`** is correctly defined in `Application/Interfaces/` with deterministic, namespaced cache keys documented in the XML summary (`policy:v1:{policyId}`, `policy:v1:summary`). The list endpoint is explicitly documented as not cached. Fully compliant with `ADR-004`.
- **`ICurrentUserService`** is correctly defined in `Application/Interfaces/` — not in `Domain` or `API`. Properties (`UserId`, `Email`, `Roles`) and `IsInRole(string)` match the specification in `ADR-007` and `.github/skills/authentication.md` exactly.
- **`ValidationPipelineBehavior`** is `sealed`, uses a primary constructor, and is correctly typed with `where TRequest : notnull` (correct for MediatR 12.x — the `IRequest<TResponse>` constraint was removed in that version). File-scoped namespace used. XML doc comments present.
- **`PolicyDto`** is a `sealed record` with all required fields matching the OpenAPI `PolicyDto` schema exactly. `LineOfBusiness` is correctly typed as `string` in the DTO (not the enum) — the mapping layer handles the conversion.
- **`PolicySummaryResponse`** is a `sealed record`. The `PremiumTotalByCurrency` field correctly matches the OpenAPI schema name and type (`IReadOnlyDictionary<string, decimal>`).
- **`PolicyMappingExtensions.ToLineOfBusinessString()`** correctly maps `LineOfBusiness.AH` → `"A&H"` at the mapping layer, providing a clean workaround for the C# enum-to-string limitation flagged in the previous domain layer review (`CRIT-1` in `review-20260617-1612-uncommitted.md`).
- **`ApplicationServiceExtensions.AddApplication()`** correctly uses `AddValidatorsFromAssembly(assembly, includeInternalTypes: true)` — FluentValidation validators will be discovered automatically when the feature folders are implemented. The extension method pattern (returning `IServiceCollection`) enables clean chaining in `Program.cs`.
- **All `.cs` files** use file-scoped namespaces. XML doc comments are thorough throughout all public types and members.
- **`PaginatedResponse<T>.TotalPages`** correctly handles the zero-count edge case (`TotalCount == 0 ? 1 : ceil(TotalCount / Size)`), avoiding a divide-by-zero or zero-page response.
