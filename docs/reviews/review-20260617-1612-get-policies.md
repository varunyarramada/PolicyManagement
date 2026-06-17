# Review: Uncommitted Changes — 2026-06-17 16:12

**Branch:** `feat/get-policies`
**Scope:** All files changed since `main` — `GetPoliciesQuery`, `GetPoliciesQueryHandler`, `GetPoliciesQueryValidator`, `PolicyBuilder`, `GetPoliciesQueryHandlerTests`, `GetPoliciesQueryValidatorTests`.

---

## Review Summary

**Overall assessment:** `REQUEST CHANGES`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 2 |
| Warning (should fix) | 3 |
| Suggestion (nice to have) | 2 |

---

## Critical Issues

### [CRIT-1] `PolicySortFields.All` allows `lineOfBusiness`, `region`, `currency`, `underwriter`, `updatedAt` — but the OpenAPI spec permits only 7 sort fields

- **File:** `src/PolicyManagement.Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryValidator.cs` (line 42); `src/PolicyManagement.Domain/Constants/PolicySortFields.cs`
- **Rule:** Contract-first API design — "All request and response schemas must match the OpenAPI spec exactly" (`.github/skills/contract-first-api.md`; `.github/copilot-instructions.md`)
- **Description:** The OpenAPI spec (`docs/openapi/policy-management-api.yaml`, line 53) documents the following **7** allowed sort fields:
  > Allowed fields: `policyNumber`, `status`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, `policyholderName`.

  `PolicySortFields.All` contains **12** entries, including five fields not in the spec: `lineOfBusiness`, `region`, `currency`, `underwriter`, `updatedAt`. The validator calls `PolicySortFields.IsValid(parts[0])` which means all 12 fields pass validation. Clients sending `?sort=currency,asc` will receive a `200 OK` response rather than the `400 Bad Request` the spec implies for unsupported sort fields.

  The `PolicyRepository.ApplySort` switch expression also handles these extra fields, so they would work end-to-end — but they are undocumented in the contract and create an implicit API surface that cannot be broken without a breaking change.
- **Suggested fix (two options):**
  - **Option A (recommended):** Remove the five undocumented fields from `PolicySortFields.All` so it contains exactly the 7 fields from the OpenAPI spec. Keep them as `public const string` properties in `PolicySortFields` for use internally by the repository, but exclude them from the `All` validation set. Update the `PolicySortFields` XML doc to clarify the distinction between "fields usable in API queries" and "fields supported by the repository".
  - **Option B:** Update the OpenAPI spec to include all 12 fields and regenerate the spec. This is only valid if product has approved exposing all 12 sort fields.

---

### [CRIT-2] `GetPoliciesQueryHandler` computes `PaginationMeta.Create(...)` twice — once inside the log call and once in the return statement

- **File:** `src/PolicyManagement.Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryHandler.cs`
- **Lines:** 77–82 and 87
- **Rule:** Code Quality — no redundant computation; correctness (`.github/copilot-instructions.md`)
- **Description:**
  ```csharp
  logger.LogInformation(
      "...",
      PaginationMeta.Create(query.Page, query.Size, totalCount).TotalPages);  // ← first call

  var dtos = items.Select(p => p.ToDto()).ToList().AsReadOnly();

  return new PagedPolicyResponse(
      Data:       dtos,
      Pagination: PaginationMeta.Create(query.Page, query.Size, totalCount));  // ← second call
  ```
  `PaginationMeta.Create` is called twice with identical arguments. While `PaginationMeta` is a pure record with no side effects and the computation is cheap, this duplicates logic unnecessarily. More importantly, the two calls are not guaranteed to be identical if the inputs were ever mutable — creating a subtle correctness risk if the code is later refactored.
- **Suggested fix:** Compute the pagination metadata once and reuse it:
  ```csharp
  var pagination = PaginationMeta.Create(query.Page, query.Size, totalCount);

  logger.LogInformation(
      "{Query} returned {Count}/{Total} policies (page {Page}/{TotalPages})",
      nameof(GetPoliciesQuery), items.Count, totalCount, query.Page, pagination.TotalPages);

  var dtos = items.Select(p => p.ToDto()).ToList().AsReadOnly();

  return new PagedPolicyResponse(Data: dtos, Pagination: pagination);
  ```

---

## Warnings

### [WARN-1] `ValidLineOfBusinessValues` in the validator is a local duplicate of `LobParseMap.Keys` in the handler — single source of truth violated

- **File:** `src/PolicyManagement.Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryValidator.cs` (lines 18–25); `GetPoliciesQueryHandler.cs` (lines 40–47)
- **Rule:** Code Quality — DRY; "No magic strings or magic numbers — constants or enums used throughout" (`.github/copilot-instructions.md`)
- **Description:** The validator defines:
  ```csharp
  private static readonly IReadOnlySet<string> ValidLineOfBusinessValues =
      new HashSet<string>(StringComparer.OrdinalIgnoreCase)
      { "Property", "Casualty", "A&H", "Marine" };
  ```
  The handler defines:
  ```csharp
  private static readonly IReadOnlyDictionary<string, LineOfBusiness> LobParseMap =
      new Dictionary<string, LineOfBusiness>(StringComparer.OrdinalIgnoreCase)
      { ["Property"] = ..., ["Casualty"] = ..., ["A&H"] = ..., ["Marine"] = ... };
  ```
  Both encode the same four strings. If a new line of business is added, both must be updated independently. A developer updating the validator may not notice they also need to update the handler's parse map, resulting in a value that passes validation but is rejected by the handler (returning `null`, silently dropping the filter).
- **Suggested fix:** Move `LobParseMap` to a `static readonly` field on the validator (or on a shared `PolicyMappingExtensions` helper), and have the validator derive its allowed values from `LobParseMap.Keys`:
  ```csharp
  // In GetPoliciesQueryValidator or a shared helper
  internal static readonly IReadOnlyDictionary<string, LineOfBusiness> LobParseMap =
      new Dictionary<string, LineOfBusiness>(StringComparer.OrdinalIgnoreCase)
      { ["Property"] = LineOfBusiness.Property, ["Casualty"] = LineOfBusiness.Casualty,
        ["A&H"] = LineOfBusiness.AH, ["Marine"] = LineOfBusiness.Marine };

  // Validator uses:
  RuleFor(q => q.LineOfBusiness)
      .Must(lob => lob == null || LobParseMap.ContainsKey(lob))
      ...

  // Handler uses the same LobParseMap reference:
  return LobParseMap.TryGetValue(lob, out var value) ? value : null;
  ```

---

### [WARN-2] The exit log message calls `items.Count` but `items` is `IReadOnlyList<Policy>` — the `Count` property is efficient, but the log fires before `.ToList()` conversion and `dtos` is computed after

- **File:** `src/PolicyManagement.Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryHandler.cs`
- **Lines:** 76–88
- **Rule:** Code Quality — logging correctness (`.github/copilot-instructions.md`; `.github/skills/production-readiness.md`)
- **Description:** The exit log fires at line 76 and `var dtos = items.Select(...).ToList()...` is at line 84. This is correct sequencing — `items` is already populated from the repository at line 73, so `items.Count` in the log is accurate. However, the log message says "returned {Count}/{Total} policies (page {Page}/{TotalPages})" before the DTO mapping has completed. If the mapping throws an exception (e.g., due to a null value in the entity), the log entry will be misleading — it will say the query returned N policies when the response actually failed.

  The correct pattern is to log at exit **after** the response is fully constructed, or to rely on `LoggingPipelineBehavior` (which is already registered as the outermost behaviour and logs completion/failure) and remove the exit log from the handler entirely.
- **Suggested fix:** Either move the exit log after `return` is ready (but before the `return` statement), or remove the exit log from the handler entirely and rely on `LoggingPipelineBehavior` to log request completion. The entry log (recording page/size/sort) should be kept as it provides useful context `LoggingPipelineBehavior` cannot.

---

### [WARN-3] `GetPoliciesQueryHandlerTests` uses a shared `_repositoryMock` field across all tests — tests that call `Verify` after tests that `Setup` could pass for the wrong reason

- **File:** `tests/PolicyManagement.Application.Tests/Features/Policies/Queries/GetPoliciesQueryHandlerTests.cs`
- **Line:** 20
- **Rule:** Testing Standards — "No shared mutable state between tests (no `static` mutable fields)" (`.github/skills/testing-standards.md`; `.github/copilot-instructions.md`)
- **Description:** `_repositoryMock` is an instance field initialised at class level:
  ```csharp
  private readonly Mock<IPolicyRepository> _repositoryMock = new();
  ```
  xUnit creates a new test class instance per test, so the mock is fresh for each test. This is technically safe in xUnit's default (no class fixture) configuration. However:
  1. The pattern diverges from the standard xUnit/Moq practice of creating mocks in the constructor or in the `Arrange` block, making it harder for readers to confirm isolation.
  2. Any future refactor to use `IClassFixture<T>` or `CollectionFixture` would introduce shared state silently.
  3. `CreateHandler()` is a factory that always uses `_repositoryMock.Object` — a helper that takes the mock as a parameter would make the dependency explicit.

  This is a Warning (not Critical) because xUnit's per-test instantiation prevents actual state sharing under the current setup.
- **Suggested fix:** Move mock creation into the constructor (or keep it as a field but initialise it in a constructor explicitly):
  ```csharp
  public sealed class GetPoliciesQueryHandlerTests
  {
      private readonly Mock<IPolicyRepository> _repositoryMock;
      private readonly GetPoliciesQueryHandler _handler;

      public GetPoliciesQueryHandlerTests()
      {
          _repositoryMock = new Mock<IPolicyRepository>();
          _handler = new GetPoliciesQueryHandler(
              _repositoryMock.Object, NullLogger<GetPoliciesQueryHandler>.Instance);
      }
  }
  ```

---

## Suggestions

### [SUGG-1] `BeAValidSortExpression` validator returns `false` for an empty string, but the error message says "value '...' is invalid" — should say the value is required or use `.NotEmpty()` separately

- **File:** `src/PolicyManagement.Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryValidator.cs`
- **Lines:** 82–86
- **Rule:** Code Quality — validator error messages should be field-specific and accurate (`.github/copilot-instructions.md`)
- **Description:** `BeAValidSortExpression("")` returns `false`, so an empty `sort` string triggers: `"'sort' value '' is invalid. Expected format: 'fieldName[,asc|desc]'. Allowed fields: ..."`. This message implies the empty string is an invalid format, rather than a missing required value. The `sort` parameter has a default of `"createdAt,desc"` in the query record, so in practice it can only be empty if explicitly passed as an empty string by a client.
- **Suggested fix:** Add a `.NotEmpty()` rule before the `.Must(...)` rule with a distinct message:
  ```csharp
  RuleFor(q => q.Sort)
      .NotEmpty()
      .WithMessage("'sort' must not be empty. Default: 'createdAt,desc'.")
      .Must(BeAValidSortExpression)
      .When(q => !string.IsNullOrWhiteSpace(q.Sort))
      .WithMessage(...);
  ```

---

### [SUGG-2] `PolicyBuilder` is declared `internal` — consider making it `public` for reuse in integration tests

- **File:** `tests/PolicyManagement.Application.Tests/Helpers/PolicyBuilder.cs`
- **Lines:** 12, 27–37 (all `internal` modifiers)
- **Rule:** Testing Standards — "`PolicyBuilder` used for test data construction" across all test projects (`.github/skills/testing-standards.md`)
- **Description:** `PolicyBuilder` is `internal sealed class` with `internal` fluent methods. The skill file states `PolicyBuilder` should be used for test data construction in all test projects (Domain, Application, Infrastructure, API). With `internal` visibility it cannot be referenced from `PolicyManagement.Infrastructure.Tests` or `PolicyManagement.API.IntegrationTests`. Once those projects need to construct `Policy` instances (e.g., for seeding a test database), they will have to duplicate the builder.
- **Suggested fix:** Either:
  - Move `PolicyBuilder` to a shared test helper project (e.g., `tests/PolicyManagement.TestHelpers/`) with `public` visibility — the cleanest solution for cross-project reuse.
  - Or add `[assembly: InternalsVisibleTo("PolicyManagement.Infrastructure.Tests")]` and similar attributes, but this is fragile and creates coupling between test projects.

---

## What Looks Good

- **`GetPoliciesQuery`** is a `sealed record` implementing `IRequest<PagedPolicyResponse>`. All parameters correctly default to the OpenAPI-specified values (`Page=1`, `Size=20`, `Sort="createdAt,desc"`). File-scoped namespace used. Full XML doc comments present.
- **`GetPoliciesQueryHandler`** is `sealed`, uses a primary constructor (two injected dependencies only — correct use case), and depends only on `IPolicyRepository` and `ILogger<T>`. No `ICacheService` reference — correct per `ADR-004` (list endpoint is never cached).
- **`LobParseMap`** in the handler correctly uses `StringComparer.OrdinalIgnoreCase` and maps `"A&H"` → `LineOfBusiness.AH` — the correct workaround for the C# enum naming constraint.
- **`ParseSort`** falls back to `PolicySortFields.CreatedAt` / `SortDirection.Desc` for unknown fields — safe default matching the OpenAPI spec default.
- **`GetPoliciesQueryValidator`** is `sealed`, uses `AbstractValidator<GetPoliciesQuery>`, and all rules produce field-specific error messages using the `q =>` message factory. No generic "validation failed" messages.
- **Effective date range rule** correctly uses `.When(q => q.EffectiveDateFrom.HasValue && q.EffectiveDateTo.HasValue)` — the rule only fires when both dates are present, so single-date filters pass validation correctly.
- **`PagedPolicyResponse`** and **`PaginationMeta`** match the OpenAPI `PagedPolicyResponse` / `PaginationMeta` schema exactly (`data` array, nested `pagination` object). Resolves `CRIT-2` from the Application layer review.
- **`PaginationMeta.Create()`** static factory correctly handles the `totalCount == 0` edge case (`TotalPages = 1`). Three `[Theory]` test cases in `GetPoliciesQueryHandlerTests` cover this case explicitly.
- **`PolicyBuilder`** correctly uses `Policy.Create()` factory — never sets properties directly. `BuildMany(n)` produces distinct GUIDs and sequential policy numbers. Fully compliant with `.github/skills/testing-standards.md`.
- **All 65 tests** follow the `{Method}_When{Condition}_Should{Expected}` naming convention. Arrange/Act/Assert blocks are cleanly separated. FluentAssertions used throughout — no bare `Assert.*` calls. Moq with `Times.Once` used for interaction verification in the repository-called-once test.
- **`GetPoliciesQueryValidatorTests`** explicitly tests `"AH"` as an invalid value (`Validate_WhenLineOfBusinessIsInvalid_ShouldFail("AH")`) — correctly capturing the `AH` vs `A&H` distinction.
- **Test project `.csproj`** correctly references `FluentAssertions`, `Moq`, and `xunit`. No infrastructure or EF Core packages — clean boundary. References only `Application` and `Domain` projects.
