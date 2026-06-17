# Review: Uncommitted Changes — 2026-06-18 11:30

## Scope

Branch: `feat/integration-tests`

**Files reviewed:**

| File | Change Type |
|---|---|
| `tests/PolicyManagement.API.Tests/Controllers/FlagPoliciesTests.cs` | Modified |
| `tests/PolicyManagement.API.Tests/Controllers/GetPoliciesTests.cs` | Modified |
| `tests/PolicyManagement.API.Tests/Helpers/PolicyApiFactory.cs` | Modified |
| `tests/PolicyManagement.API.Tests/Health/HealthCheckIntegrationTests.cs` | New (untracked) |

---

## Review Summary

**Overall assessment:** `REQUEST CHANGES`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 0 |
| Warning (should fix) | 5 |
| Suggestion (nice to have) | 2 |

---

## Critical Issues

None.

---

## Warnings

### [WARN-1] Misleading test method name — accepts 503 but name says `ShouldReturn200`

- **File:** `tests/PolicyManagement.API.Tests/Health/HealthCheckIntegrationTests.cs`
- **Line:** 63
- **Rule:** Test method names must be readable as plain English sentences and accurately describe the expected outcome (`.github/skills/testing-standards.md` — Naming Conventions)
- **Description:** `HealthReady_WhenNoTokenProvided_ShouldReturn200` asserts `response.StatusCode.Should().BeOneOf(new[] { HttpStatusCode.OK, HttpStatusCode.ServiceUnavailable }, ...)` — accepting 200 **or** 503. The `ShouldReturn200` suffix is factually incorrect and will mislead developers reading the test. The convention requires the name to describe the actual expected behaviour.
- **Suggested fix:** Rename to `HealthReady_WhenNoTokenProvided_ShouldBeReachableWithoutAuthError` and adjust the assertion body comment to match (see WARN-2 below for the deeper cause — after the factory is fixed, 200 can be asserted exclusively).

---

### [WARN-2] Test comment contradicts `PolicyApiFactory` behaviour after the new change

- **File:** `tests/PolicyManagement.API.Tests/Health/HealthCheckIntegrationTests.cs`
- **Lines:** 70–76
- **Rule:** Tests verify behaviour, not implementation details; comments must accurately describe the test environment (`.github/skills/testing-standards.md` — Guiding Principles)
- **Description:** The inline comment reads:  
  > *"The readiness probe (/health/ready) includes the 'ready' tagged sql-server check. For test purposes both probes must be reachable (not 401 / 404)."*  
  However, the companion change in `PolicyApiFactory.cs` **removes** the sql-server `HealthCheckRegistration` entirely from the test DI container. With that registration gone, the readiness check has no degraded dependency and will always return `200 OK`, not `503 Service Unavailable`. The comment describes a scenario that can no longer occur once both changes ship together.
- **Suggested fix:** Remove the qualifying comment and replace the `BeOneOf` assertion with a direct `Be(HttpStatusCode.OK)`. Rename the method per WARN-1.

---

### [WARN-3] Misleading comment on the health check removal code in `PolicyApiFactory`

- **File:** `tests/PolicyManagement.API.Tests/Helpers/PolicyApiFactory.cs`
- **Lines:** 63–70
- **Rule:** Code quality — comments must not misrepresent what the code does (`.github/copilot-instructions.md` — Code Generation Rules / Always)
- **Description:** The leading comment states:  
  > *"Replace it with a simple always-healthy check so that /health/live and /health/ready return 200 without a running SQL Server instance."*  
  The code only **removes** the sql-server registration — no replacement check is registered. The phrase "Replace it with a simple always-healthy check" implies a substitute check is added, which it is not.
- **Suggested fix:** Change the comment to accurately reflect the operation:
  ```csharp
  // ---- Remove the SQL Server health check (no SQL Server in test environment) ----
  // Removing the registration means the readiness probe has no degraded dependency
  // and will return 200 OK in the test environment.
  ```

---

### [WARN-4] Two unused `using` directives added to `PolicyApiFactory`

- **File:** `tests/PolicyManagement.API.Tests/Helpers/PolicyApiFactory.cs`
- **Lines:** 2 and 11
- **Rule:** Code quality — no unused `using` directives (`.github/copilot-instructions.md` — Code Generation Rules / Never: God classes / dead code)
- **Description:** Two new `using` statements are added by the diff but are not referenced by any code in the file:
  - `using Microsoft.AspNetCore.Diagnostics.HealthChecks;` — provides `HealthCheckOptions` (the middleware response-writer type). `HealthCheckServiceOptions` (used in the new code) lives in `Microsoft.Extensions.Diagnostics.HealthChecks`, which is already imported.
  - `using Microsoft.Extensions.Options;` — provides `IOptions<T>`, `OptionsBuilder<T>`, etc. None of these types appear in the new or existing code in this file. The `Configure<TOptions>(Action<TOptions>)` extension method is in `Microsoft.Extensions.DependencyInjection`, already imported.
- **Suggested fix:** Remove both unused `using` directives.

---

### [WARN-5] Second 400 test in `GetPoliciesTests` does not assert `errors` — inconsistent with the first

- **File:** `tests/PolicyManagement.API.Tests/Controllers/GetPoliciesTests.cs`
- **Lines:** 104–114 (pre-existing `GetPolicies_WhenSizeExceedsMaximum_ShouldReturn400ProblemDetails`)
- **Rule:** Integration tests must verify `ProblemDetails` format including field-level `errors` for all 400 validation-failure responses (`.github/skills/testing-standards.md` — Required Test Scenarios; `.github/skills/error-handling.md` — 400 responses include field-level `errors`)
- **Description:** This change correctly adds the `errors` key assertion to `GetPolicies_WhenPageIsZero_ShouldReturn400ProblemDetails`, but the equivalent 400 test `GetPolicies_WhenSizeExceedsMaximum_ShouldReturn400ProblemDetails` does not receive the same assertion. Both scenarios are triggered by `GetPoliciesQueryValidator` and will produce the same `errors` structure. Inconsistent coverage at this level weakens confidence.
- **Suggested fix:** Add the same `errors` assertion (with reason string) to `GetPolicies_WhenSizeExceedsMaximum_ShouldReturn400ProblemDetails`:
  ```csharp
  problem.Extensions.Should().ContainKey("errors",
      "400 responses from validation failures must include a field-level errors map");
  ```

---

## Suggestions

### [SUGG-1] `HealthReady_WhenAuthenticatedClient_ShouldBeReachableWithoutAuthError` only asserts negative conditions

- **File:** `tests/PolicyManagement.API.Tests/Health/HealthCheckIntegrationTests.cs`
- **Lines:** 91–100
- **Rule:** Tests should verify positive expected outcomes where possible (`.github/skills/testing-standards.md` — Guiding Principles)
- **Description:** The test asserts `NotBe(Unauthorized)` and `NotBe(NotFound)`, but never asserts what the status code positively IS. Once WARN-2 is addressed (sql-server check removed → readiness always returns 200), a positive assertion `Be(HttpStatusCode.OK)` can replace both negative assertions, making the test more precise and the failure messages more useful.
- **Suggested fix:**
  ```csharp
  response.StatusCode.Should().Be(HttpStatusCode.OK,
      "health check endpoints must not require authentication and must return 200 in the test environment");
  ```

---

### [SUGG-2] `HealthLive_WhenAuthenticatedClient_ShouldReturn200` can be collapsed into one parameterised test

- **File:** `tests/PolicyManagement.API.Tests/Health/HealthCheckIntegrationTests.cs`
- **Rule:** Avoid duplication in test cases; use `[Theory]` + `[InlineData]` for repeated logic (`.github/skills/testing-standards.md` — Guiding Principles)
- **Description:** `HealthLive_WhenNoTokenProvided_ShouldReturn200` and `HealthLive_WhenAuthenticatedClient_ShouldReturn200` test the same endpoint with two different clients and both assert the same outcome. A single `[Theory]` test parameterised by client type would be more concise, though this is purely stylistic and non-blocking.

---

## What Looks Good

- **Auth-free health check coverage added.** `HealthCheckIntegrationTests` correctly targets the core requirement from `authentication.md` Section 6 and `copilot-instructions.md` — health check endpoints must not require authentication. Both `/health/live` and `/health/ready` are covered with unauthenticated client scenarios.

- **`[Collection("ApiIntegration")]` applied correctly.** `HealthCheckIntegrationTests` uses the same `ApiIntegration` collection as the controller tests, preventing parallel execution conflicts across factory instances (`ApiIntegrationCollection.cs`).

- **`IAsyncLifetime` implemented correctly in `HealthCheckIntegrationTests`.** `InitializeAsync` calls `InitialiseDatabaseAsync()` and `DisposeAsync` calls `_factory.Dispose()`, consistent with the pattern used across all other integration test classes.

- **`errors` assertion additions in `FlagPoliciesTests` and `GetPoliciesTests` are correct.** The `ContainKey("errors", reason)` overload with a descriptive reason string is exactly the pattern prescribed in `testing-standards.md` for all 400 ProblemDetails responses from the `ValidationPipelineBehavior`. Both additions are targeted to the right validation-failure scenarios.

- **SQL Server health check removal in `PolicyApiFactory` is technically sound.** Using `services.Configure<HealthCheckServiceOptions>()` to mutate `opts.Registrations` before the application starts is the correct approach — it hooks into the `IOptions<HealthCheckServiceOptions>` post-configuration pipeline and runs before the first health check request.

- **`JwtTokenFactory.CreateExpiredToken()` used for expired-token 401 scenarios.** Both `FlagPoliciesTests` and `GetPoliciesTests` continue to use `JwtTokenFactory` — no Keycloak container dependency is introduced. Compliant with `authentication.md` Section 7: *"Integration tests cover all auth scenarios using `JwtTokenFactory` — no Keycloak container dependency".*

- **Test method naming follows `{Method}_When{Condition}_Should{Expected}` convention throughout.** All four new methods in `HealthCheckIntegrationTests` follow this order (with the exception noted in WARN-1).
