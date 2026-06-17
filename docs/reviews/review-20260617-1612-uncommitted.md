# Review: Uncommitted Changes — 2026-06-17 16:12

**Branch:** `feat/domain-layer`
**Scope:** Full branch diff against `main` — Domain layer implementation (`fd1a0e2`) plus uncommitted agent file edits.

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

### [CRIT-1] `LineOfBusiness.AH` does not match the API contract value `A&H`

- **File:** `src/PolicyManagement.Domain/Enums/LineOfBusiness.cs`
- **Line:** 13
- **Rule:** Entity and Domain Model Correctness — enum values must match the OpenAPI spec and architecture document (`docs/architecture/policy-management-architecture.md` — Table: Policies; `docs/openapi/policy-management-api.yaml`)
- **Description:** The C# enum member is named `AH`, but the OpenAPI spec (`lineOfBusiness` enum) and architecture document (column comment: "Enum as string: Property, Casualty, A&H, Marine") both specify `A&H` as the serialised string value. When the Infrastructure layer applies `.HasConversion<string>()` (as required by `ADR-006` and `.github/skills/database-conventions.md`), EF Core will call `.ToString()` on the enum, yielding `"AH"` — not `"A&H"`. This will:
  - Store `"AH"` in the database column, breaking the schema contract.
  - Return `"AH"` in API responses, breaking the OpenAPI contract.
  - Cause API validation and OpenAPI client generation to diverge from the spec.
- **Suggested fix (two options):**
  - **Option A (recommended):** Add an XML `<remarks>` doc comment on the `AH` member noting the required string mapping, and implement a custom `EnumToStringConverter` in `PolicyConfiguration` (Infrastructure):
    ```csharp
    builder.Property(p => p.LineOfBusiness)
        .HasConversion(
            v => v == LineOfBusiness.AH ? "A&H" : v.ToString(),
            v => v == "A&H" ? LineOfBusiness.AH : Enum.Parse<LineOfBusiness>(v))
        .HasColumnType("varchar(50)");
    ```
    Document this requirement as a `<remarks>` comment on the `AH` member in the Domain enum.
  - **Option B:** Use a `[Description("A&H")]` attribute or a static `Display` name dictionary within the Domain and use it in the Infrastructure converter. Either way, this must be resolved before the Infrastructure layer is implemented.

---

### [CRIT-2] Domain layer has zero unit tests

- **File:** `tests/PolicyManagement.Domain.Tests/` (project exists; directory contains no `.cs` test files)
- **Rule:** Testing Standards — "Domain logic (entities, value objects): Unit tests — all invariants and business rules" (`.github/copilot-instructions.md` — Coverage Requirements table; `.github/skills/testing-standards.md`)
- **Description:** The `PolicyManagement.Domain.Tests` project was committed with only the `.csproj` file. The `Policy` entity has non-trivial domain behaviour that must be unit-tested:
  - `Policy.Create()` — must return a correctly initialised entity with `FlaggedForReview = false` and `IsDeleted = false`.
  - `Policy.Flag(now)` — must set `FlaggedForReview = true` and update `UpdatedAt`.
  - `Policy.SoftDelete(now)` — must set `IsDeleted = true` and update `UpdatedAt`.
  - `PolicyNotFoundException` — constructor must set `PolicyId` and produce the expected message.
  - `InvalidPolicyStateException` — constructor must set `PolicyId` and produce the expected message.
  - `Regions.IsValid()` and `Currencies.IsValid()` — boundary cases (known valid, unknown, case-insensitive match).
- **Suggested fix:** Create a `PolicyTests.cs`, `PolicyNotFoundExceptionTests.cs`, `InvalidPolicyStateExceptionTests.cs`, `RegionsTests.cs`, and `CurrenciesTests.cs` in `tests/PolicyManagement.Domain.Tests/`. Use xUnit, FluentAssertions, and the `PolicyBuilder` pattern as required by `.github/skills/testing-standards.md`. Test method naming: `{Method}_When{Condition}_Should{Expected}`.

---

## Warnings

### [WARN-1] `PolicyFlaggedEvent` is designed as a batch event; checklist requires one-per-policy

- **File:** `src/PolicyManagement.Domain/Events/PolicyFlaggedEvent.cs`
- **Line:** 11–14
- **Rule:** Event Publishing — "FlagPoliciesCommandHandler publishes one `PolicyFlaggedEvent` per flagged policy" (`docs/reviews` checklist section 13; `ADR-005`)
- **Description:** `PolicyFlaggedEvent` accepts `IReadOnlyList<Guid> PolicyIds`, which models a single batch event for all flagged policies. The review checklist and `ADR-005` require the handler to publish **one event per flagged policy**. The domain event as designed commits the Application layer to a batch-publication pattern. If the checklist requirement stands, the record should carry a single `Guid PolicyId` and the handler should loop and publish once per policy.
- **Suggested fix:** Decide the publication pattern before the Application layer is written. If one-per-policy is the intent, redesign the event:
  ```csharp
  public sealed record PolicyFlaggedEvent(
      Guid PolicyId,
      string FlaggedByUserId,
      DateTimeOffset FlaggedAt);
  ```
  If the batch design is intentional (one event for all), update the review checklist accordingly to avoid divergence.

---

### [WARN-2] `IAuditableEntity` marker interface referenced in architecture document is absent

- **File:** `src/PolicyManagement.Domain/` (missing file)
- **Rule:** Domain layer completeness — "Contains the `IAuditableEntity` marker interface" (`docs/architecture/policy-management-architecture.md` — Layer Responsibilities table, Domain row)
- **Description:** The architecture document explicitly lists `IAuditableEntity` as a Domain layer artefact. It is not present in the committed code. Without it, the Infrastructure layer's `PolicyDbContext.OnModelCreating` cannot use a type-safe marker to apply the `UpdatedAt` interception pattern (e.g., `ChangeTracker` entries for `IAuditableEntity`).
- **Suggested fix:** Add `src/PolicyManagement.Domain/Interfaces/IAuditableEntity.cs`:
  ```csharp
  namespace PolicyManagement.Domain.Interfaces;

  /// <summary>
  /// Marker interface for entities that carry audit timestamps.
  /// Implementations must expose <see cref="CreatedAt"/> and <see cref="UpdatedAt"/>.
  /// </summary>
  public interface IAuditableEntity
  {
      DateTimeOffset CreatedAt { get; }
      DateTimeOffset UpdatedAt { get; }
  }
  ```
  Then have `Policy` implement `IAuditableEntity`.

---

### [WARN-3] `Jwt__Authority` and `Jwt__Audience` hardcoded inline in `docker-compose.yml`

- **File:** `docker-compose.yml`
- **Lines:** 101–106
- **Rule:** Security — "No JWT secrets or Keycloak URLs hardcoded in source code" (`.github/copilot-instructions.md`; `.github/skills/authentication.md`)
- **Description:** The `.env.example` documents `Jwt__Authority` and `Jwt__Audience` as environment variables intended to be supplied via `.env`, but `docker-compose.yml` sets them as inline literals (`http://keycloak:8080/realms/policymanagement` and `policymanagement-api`) rather than using `${Jwt__Authority}` and `${Jwt__Audience}` substitution. The `.env.example` even comments "informational — set directly in docker-compose.yml", which contradicts the principle that configuration is externalised.
  - While the Docker network hostname `keycloak` is fixed by container naming, hardcoding the realm path (`/realms/policymanagement`) means a realm rename requires a compose file change tracked in source control rather than a `.env` change.
- **Suggested fix:** Replace the inline values with `${Jwt__Authority}` and `${Jwt__Audience}` in `docker-compose.yml` and update `.env.example` to mark them as required (not informational). This aligns with the 12-factor configuration principle.

---

## Suggestions

### [SUGG-1] `Policy.Create()` does not enforce domain invariants

- **File:** `src/PolicyManagement.Domain/Entities/Policy.cs`
- **Lines:** 84–117
- **Rule:** Domain layer responsibility — entities should enforce their own invariants (`.github/skills/clean-architecture.md` — Domain layer, "Entities (identity + state + invariants)")
- **Description:** The `Create` static factory accepts all parameters without validation. The following domain invariants are implied by the architecture but are not enforced at creation time:
  - `expiryDate` must be after `effectiveDate` — this is a domain rule, not just an input validation concern.
  - `premiumAmount` must be > 0 — the architecture states range 1,000.00–5,000,000.00.
  - Enforcing these in the factory would give the `Application` layer a guaranteed invariant and would make unit-testing the domain easier.
- **Suggested fix:** Add guard clauses to `Policy.Create()`:
  ```csharp
  if (expiryDate <= effectiveDate)
      throw new InvalidPolicyStateException(id, "Expiry date must be after effective date.");
  if (premiumAmount <= 0)
      throw new InvalidPolicyStateException(id, "Premium amount must be positive.");
  ```
  Note: FluentValidation in the Application layer should still validate these at the API boundary; the domain guard is a second line of defence.

---

### [SUGG-2] `PolicyFilter` sort fields use raw strings; no constant set defined in Domain

- **File:** `src/PolicyManagement.Domain/Filters/PolicyFilter.cs`
- **Lines:** 25–26 (`SortField`, `SortDirection` parameters)
- **Rule:** Code Quality — "No magic strings or magic numbers — constants or enums used throughout" (`.github/copilot-instructions.md`)
- **Description:** `SortField` and `SortDirection` are plain `string` parameters. The Application validator (`GetPoliciesQueryValidator`) will need to validate `SortField` against the allowed list (`policyNumber`, `status`, `premiumAmount`, `effectiveDate`, `expiryDate`, `createdAt`, `policyholderName`) and `SortDirection` against `"asc"` / `"desc"`. Without a constant set or enum defined in the Domain, the validator will contain magic strings duplicated from the OpenAPI spec.
- **Suggested fix:** Add a `PolicySortField` enum or a static `PolicySortFields.AllowedValues` constant set in the Domain (similar to `Regions.All` and `Currencies.All`). For `SortDirection`, consider a two-member enum `SortDirection { Asc, Desc }` in the Domain.

---

## What Looks Good

- **`PolicyManagement.Domain.csproj`** has zero `<ProjectReference>` entries and zero NuGet `<PackageReference>` entries beyond the .NET BCL — fully compliant with `ADR-001` and `.github/skills/clean-architecture.md`.
- **`Policy` entity property names** match the architecture table exactly: `Id`, `PolicyNumber`, `PolicyholderName`, `LineOfBusiness`, `Status`, `PremiumAmount`, `Currency`, `EffectiveDate`, `ExpiryDate`, `Region`, `Underwriter`, `FlaggedForReview`, `IsDeleted`, `CreatedAt`, `UpdatedAt`. No invented names (`StartDate`, `IsFlagged`, `FlagReason`, etc.).
- **`Regions.HongKong == "Hong Kong"`** (with space) — correct per the architecture doc and database schema.
- **`Regions` and `Currencies` classes** use string constants with an `IReadOnlySet<string> All` and `IsValid()` helper — exactly the pattern specified.
- **`Policy` entity** correctly uses `class` with `private set` properties and a `private` parameterless constructor for EF Core materialisation. Not declared as `record` — compliant with code quality rules.
- **`PolicyFilter`** is a `sealed record` in `Domain/Filters/` — it has no MediatR or Application dependencies, correctly passing the repository pattern requirement (`ADR-003`).
- **`PolicySummaryData`** is a `sealed record` in `Domain/Models/` — correctly decoupled from the Application DTO layer, matching the repository interface contract.
- **`IEventPublisher`** and **`IPolicyRepository`** are in `Domain/Interfaces/` — correct placement per `ADR-003` and `ADR-005`.
- **`IPolicyRepository.GetPagedAsync`** accepts `PolicyFilter` (a Domain type), not the MediatR query — clean architecture boundary correctly observed.
- **`IPolicyRepository.GetSummaryAsync`** returns `PolicySummaryData` (a Domain type), not the Application DTO — clean architecture boundary correctly observed.
- **`PolicyFlaggedEvent`** is a `sealed record` in `Domain/Events/` with no infrastructure dependencies — compliant with `ADR-005`.
- **All `.cs` files** use file-scoped namespaces (`namespace Foo.Bar;`) — compliant with code quality rules.
- **XML doc comments** are present on all public types and members throughout the Domain layer — thorough documentation.
- **`DomainException`** base class is `abstract` with protected constructors — correct pattern for domain exception hierarchy.
- **`InvalidPolicyStateException`** and **`PolicyNotFoundException`** are both `sealed` — compliant with code quality rules.
- **`Dockerfile`** correctly creates a non-root user (`appuser`, uid 1000) and switches to it before `ENTRYPOINT` — compliant with the Docker security requirement.
- **`SA_PASSWORD`**, **`KEYCLOAK_ADMIN`**, and **`KEYCLOAK_ADMIN_PASSWORD`** in `docker-compose.yml` all use `${...}` environment variable substitution — no hardcoded credentials.
- **Agent file changes** (`pr-writer.agent.md`, `reviewer.agent.md`) are well-structured: the reviewer agent now correctly includes the `edit` tool scoped to `docs/reviews/` only, and the three-step workflow (Determine → Pre-work → Write output) is clearly documented.
