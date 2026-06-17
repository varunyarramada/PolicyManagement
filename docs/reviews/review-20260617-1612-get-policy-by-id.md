# Review: Uncommitted Changes — 2026-06-17 16:12

**Branch:** `feat/get-policy-by-id`
**Scope:** All files changed since `main` — `GetPolicyByIdQuery`, `GetPolicyByIdQueryHandler`, `GetPolicyByIdQueryHandlerTests`.

---

## Review Summary

**Overall assessment:** `APPROVE`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 0 |
| Warning (should fix) | 1 |
| Suggestion (nice to have) | 2 |

---

## Critical Issues

None.

---

## Warnings

### [WARN-1] `GetPoliciesQueryHandler` references `GetPoliciesQueryValidator.LobParseMap` — Application handler depends on a validator implementation detail

- **File:** `src/PolicyManagement.Application/Features/Policies/Queries/GetPolicies/GetPoliciesQueryHandler.cs`
- **Line:** (ParseLineOfBusiness method — `GetPoliciesQueryValidator.LobParseMap.TryGetValue(...)`)
- **Rule:** SOLID — Single Responsibility; Clean Architecture — no coupling between unrelated classes in the same layer (`.github/copilot-instructions.md`)
- **Description:** `GetPoliciesQueryHandler.ParseLineOfBusiness()` calls `GetPoliciesQueryValidator.LobParseMap.TryGetValue(...)` directly. This creates a dependency from a handler class onto a validator class — two unrelated responsibilities within the Application layer. This resolves the DRY finding from the previous review (WARN-1 in `review-20260617-1612-get-policies.md`), but the chosen placement — on the validator — means:
  1. The handler imports a reference to `GetPoliciesQueryValidator`. When `GetPolicyByIdQueryHandler` or `FlagPoliciesCommandHandler` need to parse a LOB string (e.g., for future filter-by-LOB scenarios), they will also need to reference the `GetPolicies` validator, creating cross-feature coupling.
  2. `GetPoliciesQueryValidator.LobParseMap` is `internal static readonly` — it is accessible only within the same assembly (Application), which is fine for now, but the coupling intent is wrong.
- **Suggested fix:** Promote `LobParseMap` to `PolicyMappingExtensions` (which already handles the `AH`→`"A&H"` conversion) or to a new `Application/Constants/LineOfBusinessMap.cs` static class. Both the validator and the handler reference a shared Application-layer constant rather than one referencing the other:
  ```csharp
  // Application/Constants/LineOfBusinessMap.cs
  internal static class LineOfBusinessMap
  {
      internal static readonly IReadOnlyDictionary<string, LineOfBusiness> DisplayToEnum =
          new Dictionary<string, LineOfBusiness>(StringComparer.OrdinalIgnoreCase)
          { ["Property"] = ..., ["Casualty"] = ..., ["A&H"] = LineOfBusiness.AH, ["Marine"] = ... };
  }
  ```
  The validator and handler both reference `LineOfBusinessMap.DisplayToEnum` independently.

---

## Suggestions

### [SUGG-1] `Handle_WhenPolicyNotFound_ShouldNotCallCacheSet` calls `act.Should().ThrowAsync` after the Verify assertion — the assertion order is inverted

- **File:** `tests/PolicyManagement.Application.Tests/Features/Policies/Queries/GetPolicyByIdQueryHandlerTests.cs`
- **Lines:** 143–153
- **Rule:** Testing Standards — Arrange/Act/Assert pattern with correct sequencing (`.github/skills/testing-standards.md`)
- **Description:**
  ```csharp
  // Act
  var act = () => _handler.Handle(...);
  await act.Should().ThrowAsync<PolicyNotFoundException>();

  // Assert — cache.SetAsync must never be called when the policy is not found
  _cacheMock.Verify(..., Times.Never);
  ```
  The `ThrowAsync` assertion is in the Act block (by placement) but also serves as the primary assertion — calling it "Act" and then adding a `Verify` after is slightly misleading. More importantly, the `Times.Never` `Verify` after a `ThrowAsync` could silently pass if the exception is thrown before `SetAsync` is even reached — which is always true here (the `throw` is on line 74 of the handler). The test does confirm the correct behaviour, but the `ThrowAsync` should be placed in the Assert block as one of two assertions.
- **Suggested fix:**
  ```csharp
  // Act
  var act = () => _handler.Handle(new GetPolicyByIdQuery(Guid.NewGuid()), CancellationToken.None);

  // Assert
  await act.Should().ThrowAsync<PolicyNotFoundException>();
  _cacheMock.Verify(
      c => c.SetAsync(...), Times.Never);
  ```

---

### [SUGG-2] `Handle_Always_ShouldUseCorrectCacheKeyFormat` forces a `PolicyNotFoundException` to verify the cache key — consider using a successful path instead

- **File:** `tests/PolicyManagement.Application.Tests/Features/Policies/Queries/GetPolicyByIdQueryHandlerTests.cs`
- **Lines:** 318–342
- **Rule:** Testing Standards — test behaviour, not implementation details; tests should not force exceptions to observe side effects (`.github/skills/testing-standards.md`)
- **Description:** The test deliberately returns `null` from the repository (causing `PolicyNotFoundException`) in order to observe that `cache.GetAsync` was called with the correct key. This means the test simultaneously exercises the "not found" path and the cache key format — two behaviours in one test. If the cache key format changes, this test fails for the wrong reason (the exception path, not the cache key).
  
  More importantly, `Handle_WhenCacheMiss_ShouldSetCacheWithCorrectKeyAndTtl` already verifies the exact cache key format via the `SetAsync` call. The `GetAsync` key can be verified through the "found" happy path without forcing an exception.
- **Suggested fix:** Change the test to use a successful repository response:
  ```csharp
  [Fact]
  public async Task Handle_Always_ShouldUseCorrectCacheKeyFormat()
  {
      // Arrange
      var policy = new PolicyBuilder().Build();
      var expectedKey = $"policy:v1:{policy.Id}";

      _cacheMock
          .Setup(c => c.GetAsync<PolicyDto>(It.IsAny<string>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync((PolicyDto?)null);

      _repositoryMock
          .Setup(r => r.GetByIdAsync(policy.Id, It.IsAny<CancellationToken>()))
          .ReturnsAsync(policy);

      // Act
      await _handler.Handle(new GetPolicyByIdQuery(policy.Id), CancellationToken.None);

      // Assert — both GetAsync and SetAsync use the same key
      _cacheMock.Verify(c => c.GetAsync<PolicyDto>(expectedKey, It.IsAny<CancellationToken>()), Times.Once);
      _cacheMock.Verify(c => c.SetAsync(expectedKey, It.IsAny<PolicyDto>(), It.IsAny<TimeSpan>(), It.IsAny<CancellationToken>()), Times.Once);
  }
  ```

---

## What Looks Good

- **`GetPolicyByIdQuery`** is a `sealed record` with a single `Guid Id` parameter — minimal, correct, compliant with naming conventions. XML doc references ADR-004 and the cache key convention.
- **`GetPolicyByIdQueryHandler`** is `sealed`, uses a primary constructor, and correctly implements the cache-aside pattern in the exact order specified: check cache → return on hit → query repository → throw `PolicyNotFoundException` on miss → map → write cache → return. Fully compliant with `ADR-004`.
- **Cache key** `policy:v1:{id}` matches the ADR-004 documented convention exactly and is defined as a private static method `CacheKey(Guid)` rather than an inline string — preventing typos across the class.
- **`_cacheOptions.PolicyTtl`** (a `TimeSpan`) is passed to `SetAsync` — never a hardcoded `TimeSpan.FromMinutes(5)` literal. Fully compliant with the configuration rule in `.github/copilot-instructions.md`.
- **`PolicyNotFoundException` log** uses `LogWarning` — correct severity for a business "not found" path (not an error, not informational).
- **`GetPolicyByIdQueryHandlerTests`** uses the constructor pattern for mock initialisation (fixing WARN-3 from the previous review `review-20260617-1612-get-policies.md`), not field initialisation — test isolation is explicit.
- **`PolicyBuilder`** is now in `tests/PolicyManagement.TestHelpers/` as a `public sealed class` — resolves `SUGG-2` from the previous review. The `TestHelpers` project references only `PolicyManagement.Domain`, keeping it clean. The Application test project correctly adds `TestHelpers` as a project reference.
- **`CacheOptions`** is now in `src/PolicyManagement.Application/Options/` with `[Range]` data annotations — resolves `WARN-2` and part of `CRIT-2` from the Infrastructure layer review (`review-20260617-1612-infrastructure-layer.md`).
- **`CRIT-2` from previous review resolved:** `PaginationMeta.Create(...)` is now computed once into a `var pagination` variable and reused — no double computation.
- **`WARN-1` from previous review partially resolved:** `ValidLineOfBusinessValues` duplicate set in the validator has been eliminated. The `LobParseMap` is now a single source of truth for both validator and handler (see WARN-1 above for the remaining placement concern).
- **10 handler tests** cover every meaningful execution path: happy-path mapping, `A&H` display string, `PolicyNotFoundException`, cache-hit short-circuit (no repository call, no `SetAsync`), cache-miss full flow (repository called once, `SetAsync` called with correct key and TTL, stored DTO equals returned DTO). All test names follow `{Method}_When{Condition}_Should{Expected}`. FluentAssertions and Moq used correctly throughout.
- **`Handle_WhenCacheMiss_ShouldSetCacheWithCorrectKeyAndTtl`** asserts both the exact cache key (`policy:v1:{id}`) and the exact TTL (`TimeSpan.FromSeconds(300)`) — strong regression protection against cache key drift or TTL hardcoding.
