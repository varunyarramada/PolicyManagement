# Review: Uncommitted Changes — 2026-06-17 16:12

**Branch:** `feat/flag-policies`
**Scope:** All files changed since `main` — `FlagPoliciesCommand`, `FlagPoliciesCommandHandler`, `FlagPoliciesCommandValidator`, `FlagPoliciesCommandHandlerTests`, `FlagPoliciesCommandValidatorTests`.

---

## Review Summary

**Overall assessment:** `REQUEST CHANGES`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 1 |
| Warning (should fix) | 2 |
| Suggestion (nice to have) | 1 |

---

## Critical Issues

### [CRIT-1] Handler loads policies N×1 via `GetByIdAsync` in a loop — N round-trips to the database for a batch operation

- **File:** `src/PolicyManagement.Application/Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandHandler.cs`
- **Lines:** 57–73
- **Rule:** Repository Pattern — "Application handlers depend only on `IPolicyRepository`" with efficient data access (`ADR-003`); Clean Architecture / performance — no N+1 query patterns (`docs/architecture/policy-management-architecture.md`)
- **Description:** The handler iterates `command.PolicyIds` and calls `repository.GetByIdAsync(id, ...)` once per ID in a `foreach` loop:
  ```csharp
  foreach (var id in command.PolicyIds)
  {
      var policy = await repository.GetByIdAsync(id, cancellationToken);
      if (policy is null) throw new PolicyNotFoundException(id);
      policies.Add(policy);
  }
  ```
  With up to 100 IDs per request (per the validator), this issues up to 100 sequential database round-trips before a single write occurs. Each `GetByIdAsync` call executes a `SELECT WHERE id = @p` query with `.AsNoTracking()`. This is an N+1 pattern.

  `IPolicyRepository` already defines `ExistAllAsync(IEnumerable<Guid> ids)` which issues a single `CountAsync` query. Additionally, a batch `GetByIdsAsync(IEnumerable<Guid> ids)` method could retrieve all entities in one `WHERE id IN (...)` query. The split into "load" and "validate existence" phases creates the N+1; combining them removes it.
- **Suggested fix (two options):**
  - **Option A (preferred — minimal interface change):** Add `Task<IReadOnlyList<Policy>> GetByIdsAsync(IEnumerable<Guid> ids, CancellationToken ct)` to `IPolicyRepository` with an `EF Core` implementation using `.Where(p => ids.Contains(p.Id)).ToListAsync()`. This is a single SQL `IN` query. The handler then:
    ```csharp
    var policies = await repository.GetByIdsAsync(command.PolicyIds, cancellationToken);
    var missingId = command.PolicyIds.FirstOrDefault(id => policies.All(p => p.Id != id));
    if (missingId != default) throw new PolicyNotFoundException(missingId);
    ```
  - **Option B (no interface change):** Use the existing `ExistAllAsync` first (single COUNT query), then call `GetByIdAsync` per policy only after existence is confirmed. This reduces the failure case to 1 query, but the success case is still N+1 + 1. Not recommended — Option A is cleaner.

---

## Warnings

### [WARN-1] Handler uses `ICurrentUserService` without handler-level auth enforcement — `currentUser.UserId` could be `null` at runtime if called outside the authenticated HTTP pipeline

- **File:** `src/PolicyManagement.Application/Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandHandler.cs`
- **Line:** 103: `var userId = currentUser.UserId ?? string.Empty;`
- **Rule:** Authentication — "No auth logic in handlers — handlers use `ICurrentUserService` only when user identity is needed" (`.github/skills/authentication.md`; `ADR-007`); the null-coalescing to `string.Empty` silently suppresses a missing user ID
- **Description:** The handler guards `currentUser.UserId` with `?? string.Empty`, which means if the handler is ever invoked without an authenticated user (e.g. in a background service, a test without a mock, or a future admin tool), the `PolicyFlaggedEvent` will be published with `FlaggedByUserId = ""`. An empty user ID in an audit event is misleading — it looks like a valid (anonymous) user rather than a missing authentication context.

  The correct pattern per `ADR-007` is: the controller enforces auth via `[Authorize(Policy = "PolicyWrite")]` before MediatR dispatches; the handler trusts that `ICurrentUserService.UserId` is non-null when reached via the normal HTTP path. However, defensive null-handling in the handler is a code smell that implies uncertainty about the auth contract.
- **Suggested fix:** Replace the silent fallback with an explicit guard:
  ```csharp
  var userId = currentUser.UserId
      ?? throw new InvalidOperationException(
          "FlagPoliciesCommandHandler requires an authenticated user. " +
          "Ensure [Authorize(Policy = \"PolicyWrite\")] is applied to the controller action.");
  ```
  This causes an immediate, diagnosable failure (surfaced as 500 by `GlobalExceptionMiddleware`) if the auth contract is broken, rather than silent data corruption in audit events. Also update the handler's XML `<remarks>` to state that `ICurrentUserService.UserId` is expected to be non-null when the command is dispatched via the authenticated HTTP pipeline.

---

### [WARN-2] `FlagPoliciesCommandValidatorTests` uses synchronous `TestValidate` — all other validator test classes in the project use `async/await ValidateAsync`

- **File:** `tests/PolicyManagement.Application.Tests/Features/Policies/Commands/FlagPoliciesCommandValidatorTests.cs`
- **Lines:** 30, 43, 62, etc. (all test methods)
- **Rule:** Testing Standards — consistency across test project (`.github/skills/testing-standards.md`); FluentValidation's `TestHelper` methods are available for both sync and async
- **Description:** `FlagPoliciesCommandValidatorTests` uses `FluentValidation.TestHelper.TestValidate(command)` (synchronous extension method) and `ShouldHaveValidationErrorFor` / `ShouldNotHaveAnyValidationErrors`. Previous validator tests (`GetPoliciesQueryValidatorTests`) used `await _validator.ValidateAsync(query)` with FluentAssertions.

  Both approaches are valid, but the project now has two different testing patterns for validators. The `TestHelper` approach is actually more idiomatic for FluentValidation unit tests and provides the `ShouldHaveValidationErrorFor` / `ShouldNotHaveValidationErrorFor` DSL which is more readable. However, the inconsistency with the existing `async` pattern in the test project is worth noting for future contributors.
- **Suggested fix:** Either standardise on `FluentValidation.TestHelper` (the new pattern) across all validator tests — updating `GetPoliciesQueryValidatorTests` to use `TestValidate` — or add a note in the test class XML doc explaining the intentional choice. The former is cleaner but touches existing passing tests.

---

## Suggestions

### [SUGG-1] Per-policy cache invalidation loops over `command.PolicyIds` but could consolidate with the policies loop from Step 4

- **File:** `src/PolicyManagement.Application/Features/Policies/Commands/FlagPolicies/FlagPoliciesCommandHandler.cs`
- **Lines:** 107–108 and 113–114
- **Rule:** Code Quality — DRY; no redundant iteration (`.github/copilot-instructions.md`)
- **Description:** Steps 4 and 5 each iterate the policies/IDs separately:
  ```csharp
  // Step 4 — iterate policies
  foreach (var policy in policies)
      await eventPublisher.PublishAsync(new PolicyFlaggedEvent(policy.Id, userId, now), ct);

  // Step 5 — iterate command.PolicyIds separately
  await cache.RemoveAsync(SummaryCacheKey, ct);
  foreach (var id in command.PolicyIds)
      await cache.RemoveAsync(PolicyCacheKey(id), ct);
  ```
  The per-policy cache invalidation could be combined with the event-publishing loop since both iterate over the same set of IDs. This eliminates a second loop:
  ```csharp
  foreach (var policy in policies)
  {
      await eventPublisher.PublishAsync(new PolicyFlaggedEvent(policy.Id, userId, now), ct);
      await cache.RemoveAsync(PolicyCacheKey(policy.Id), ct);
  }
  await cache.RemoveAsync(SummaryCacheKey, ct);
  ```
  This is a minor style improvement — the current code is correct and readable with the comments.

---

## What Looks Good

- **`FlagPoliciesCommand`** is a `sealed record` implementing `IRequest` (no type parameter — correctly models a void command). `PolicyIds` typed as `IReadOnlyList<Guid>` enforces immutability at the API boundary. XML doc fully documents the five-step execution sequence and the cache invalidation contract with `GetPolicySummaryQueryHandler`.
- **`FlagPoliciesCommandHandler`** is `sealed`, uses primary constructor with five injected dependencies. Execution is clearly structured into five labelled steps with inline comments. `now = DateTimeOffset.UtcNow` captured once and reused for both `Policy.Flag(now)` and `PolicyFlaggedEvent(FlaggedAt: now)` — timestamp consistency across the operation.
- **`PolicyFlaggedEvent`** now carries a single `Guid PolicyId` (not a batch list) — correctly resolving the WARN-1 from the domain layer review (`review-20260617-1612-uncommitted.md`). The handler publishes one event per policy in a loop — compliant with `ADR-005`.
- **Cache invalidation order is correct**: occurs in Step 5, after `UpdateRangeAsync` succeeds in Step 3. If the DB write fails, the cache is never touched — no inconsistency window.
- **`SummaryCacheKey` (`"policy:v1:summary"`)** and **`PolicyCacheKey(Guid id)` (`$"policy:v1:{id}"`)** match the ADR-004 documented conventions exactly and match the keys used in `GetPolicySummaryQueryHandler` and `GetPolicyByIdQueryHandler` respectively.
- **`FlagPoliciesCommandValidator`** uses `public const int MaxPolicyIds = 100` — no magic number. All four rules have descriptive error messages. The `When(c => c.PolicyIds is { Count: > 0 })` guard on rules 2–4 prevents NullReferenceExceptions when `PolicyIds` is empty (the `NotEmpty` rule already fires, so the guards avoid redundant errors).
- **`FlagPoliciesCommandValidatorTests`** uses `FluentValidation.TestHelper` (`TestValidate`, `ShouldHaveValidationErrorFor`, `ShouldNotHaveAnyValidationErrors`) — the most idiomatic FluentValidation test pattern. `ValidIds(int count)` helper produces clean test data without duplicating `Guid.NewGuid()` calls inline.
- **`FlagPoliciesCommandHandlerTests`** creates all mocks in the constructor — consistent with the pattern established in `GetPolicyByIdQueryHandlerTests`. Default setups in the constructor (`UpdateRangeAsync`, `PublishAsync`, `RemoveAsync`) avoid boilerplate repetition across tests while still allowing per-test overrides.
- **`Handle_WhenAllIdsExistAndNotFlagged_ShouldFlagAllPolicies`** uses a `Callback` to capture the `IEnumerable<Policy>` passed to `UpdateRangeAsync` and asserts `AllSatisfy(p => p.FlaggedForReview.Should().BeTrue())` — correctly testing the domain mutation, not just the repository call.
- **`Handle_WhenAllIdsExistAndNotFlagged_ShouldPublishEventsWithCorrectPolicyIds`** captures the published `PolicyFlaggedEvent` via a `Callback` and asserts both `PolicyId` and `FlaggedByUserId` — strong regression protection against event payload drift.
- **`Handle_WhenPolicyAlreadyFlagged_ShouldNotInvalidateCache`** verifies `Times.Never` on `cache.RemoveAsync` for any key — correctly asserting no side-effects on the state-validation failure path.
- **109/109 tests passing** — zero regressions against all prior handler and validator tests.
