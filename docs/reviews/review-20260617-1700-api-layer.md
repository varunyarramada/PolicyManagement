# Review: Uncommitted Changes — 2026-06-17 17:00

**Branch:** `feat/api-layer`
**Scope:** All files changed since `main`. New files: `PoliciesController.cs`, `FlagPoliciesRequest.cs`. Pre-existing files read for full checklist coverage: `Program.cs`, `GlobalExceptionMiddleware.cs`, `CorrelationIdMiddleware.cs`, `CurrentUserService.cs`, `JwtOptions.cs`, `appsettings.json`, `appsettings.Development.json`.

---

## Review Summary

**Overall assessment:** `REQUEST CHANGES`

| Severity | Count |
|---|---|
| Critical (must fix before merge) | 2 |
| Warning (should fix) | 2 |
| Suggestion (nice to have) | 2 |

---

## Critical Issues

### [CRIT-1] `AddApiVersioning()` is not registered — the `{version:apiVersion}` route constraint is unresolved, making all controller routes non-functional at runtime

- **File:** `src/PolicyManagement.API/Program.cs`
- **Rule:** Contract-first API — "Version APIs under `/api/v{version}/` from day one" (`.github/skills/contract-first-api.md`); API versioning requires the Asp.Versioning middleware to be registered (`.github/copilot-instructions.md` — no hardcoded config, working routes required)
- **Description:** `PolicyManagement.API.csproj` references `Asp.Versioning.Mvc` v8.1.0 and `Asp.Versioning.Mvc.ApiExplorer` v8.1.0. `PoliciesController` is decorated with `[ApiVersion("1.0")]` and uses the route template `api/v{version:apiVersion}/policies`. The `{version:apiVersion}` segment is a custom route constraint provided by the `Asp.Versioning` middleware.

  `Program.cs` calls `builder.Services.AddControllers()` but **never calls `builder.Services.AddApiVersioning()`** (or `.AddMvc()` which is the Asp.Versioning-specific MVC integration). Without this registration:
  - The `{version:apiVersion}` route constraint is not added to ASP.NET Core's constraint map.
  - On startup, ASP.NET Core will either throw an `InvalidOperationException` ("The constraint reference 'apiVersion' could not be resolved to a type") or silently treat the segment as an unmatched literal, making all four controller actions unreachable (404 for every request).
  - The `[ApiVersion("1.0")]` attribute on the controller has no effect without the middleware.

- **Suggested fix:** Add the API versioning registration to `Program.cs` in the "API" service registration block, immediately before `AddControllers()`:
  ```csharp
  // ---- API ----
  builder.Services.AddHttpContextAccessor();
  builder.Services.AddScoped<ICurrentUserService, CurrentUserService>();
  builder.Services.AddApiVersioning(opts =>
  {
      opts.DefaultApiVersion = new ApiVersion(1, 0);
      opts.AssumeDefaultVersionWhenUnspecified = true;
      opts.ReportApiVersions = true;
  })
  .AddMvc();
  builder.Services.AddControllers();
  ```
  Add the corresponding `using Asp.Versioning;` directive.

---

### [CRIT-2] No integration tests exist in `PolicyManagement.API.Tests` — the API layer branch is complete but delivers zero integration test coverage

- **File:** `tests/PolicyManagement.API.Tests/` (directory is empty — no `.cs` files outside `obj/`)
- **Rule:** Testing Standards — "Integration tests for API endpoints use `WebApplicationFactory<Program>`"; "Every HTTP status code declared in the architecture document has at least one integration test"; "Every feature branch must include tests before a PR can be raised" (`.github/skills/testing-standards.md`, `.github/copilot-instructions.md`)
- **Description:** The PR commit message states "Tests: 110/110 passed across Domain, Application test projects." The `PolicyManagement.API.Tests` project exists but contains zero test files. The API layer branch is the branch where integration tests are expected — prior PRs (`feat/get-policy-by-id`, `feat/get-policy-summary`, `feat/flag-policies`) all deferred integration tests to this branch explicitly.

  The following HTTP status code paths are declared in `docs/openapi/policy-management-api.yaml` and have no integration test coverage:

  | Endpoint | Uncovered status codes |
  |---|---|
  | `GET /api/v1/policies` | 200, 400, 401, 500 |
  | `GET /api/v1/policies/{id}` | 200, 401, 404, 500 |
  | `GET /api/v1/policies/summary` | 200, 401, 500 |
  | `PATCH /api/v1/policies/flag` | 204, 400, 401, 403, 404, 409, 500 |

  Authentication scenarios (401 / 403) cannot be tested by the Application-layer unit tests since they require the full HTTP pipeline and JWT validation.

- **Suggested fix:** Implement integration tests in `tests/PolicyManagement.API.Tests/` using `WebApplicationFactory<Program>` with:
  - A unique in-memory or SQLite database per factory instance (or a test double for `IPolicyRepository` via `WebApplicationFactory.WithWebHostBuilder`)
  - A `JwtTokenFactory` helper (no running Keycloak required — sign tokens locally with a test key, configure test JWT Bearer to use the matching signing key)
  - One test class per controller action; one test method per declared HTTP status code
  - Verify `Content-Type: application/problem+json` on all error responses
  - Verify `correlationId` is present in all `ProblemDetails` error responses

---

## Warnings

### [WARN-1] `GetPolicyByIdAsync` declares `[ProducesResponseType(StatusCodes.Status400BadRequest)]` but 400 is unreachable from this action

- **File:** `src/PolicyManagement.API/Controllers/PoliciesController.cs`
- **Line:** 89
- **Rule:** Contract-first API — `[ProducesResponseType]` annotations must match actual possible HTTP status codes; misleading annotations generate incorrect OpenAPI metadata (`.github/skills/contract-first-api.md`)
- **Description:** The route template `[HttpGet("{id:guid}")]` uses the `{guid}` route constraint. ASP.NET Core evaluates this constraint before the action is selected — if the segment is not a valid GUID (e.g. `/api/v1/policies/not-a-guid`), routing produces a 404 "no matching route found". The action method is never invoked, so model binding never runs, and 400 cannot be returned by this action. The declared `[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]` is inaccurate and will cause the generated OpenAPI document to declare a 400 response schema for an endpoint that cannot produce one via any code path.

  Cross-reference: the OpenAPI spec at `docs/openapi/policy-management-api.yaml` for `GET /api/v1/policies/{id}` should be checked — if it does not list 400, the annotation contradicts the source of truth.
- **Suggested fix:** Remove `[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]` from `GetPolicyByIdAsync`. The reachable status codes for this action are: `200`, `401`, `404`, `500`.

---

### [WARN-2] `Program.cs` reads `JwtOptions` via raw `IConfiguration.GetSection().Get<T>()` to configure `AddJwtBearer` — bypasses `ValidateOnStart()` validation for the JWT Bearer configuration

- **File:** `src/PolicyManagement.API/Program.cs`
- **Lines:** 43–47
- **Rule:** Configuration — "Bind configuration sections to strongly-typed options classes (`IOptions<T>`)" (`.github/copilot-instructions.md`); production-readiness — misconfiguration should fail at startup, not silently (`.github/skills/production-readiness.md`)
- **Description:** The `AddJwtBearer` callback reads JWT config directly:
  ```csharp
  var jwt = builder.Configuration
      .GetSection(JwtOptions.SectionName)
      .Get<JwtOptions>() ?? new JwtOptions();

  opts.Authority            = jwt.Authority;
  opts.Audience             = jwt.Audience;
  opts.RequireHttpsMetadata = jwt.RequireHttpsMetadata;
  ```
  `JwtOptions.Authority` defaults to `string.Empty`. If the `Jwt__Authority` environment variable is not set, `Get<JwtOptions>()` returns an instance with `Authority = ""`. `AddJwtBearer` is then configured with `opts.Authority = ""` — an invalid value. The `ValidateOnStart()` validation (which would catch the missing `[Required]` field) fires when the DI container is first used, which is after `AddJwtBearer` has already been configured. The JWT middleware will have been configured with an empty `Authority` before validation kicks in.

  In practice, `ValidateOnStart()` fires during `app.Run()` startup and prevents the app from serving requests. So the net result is the same (app fails to start), but the failure mode is: "JWT middleware configured with empty Authority → `ValidateOnStart()` fails → app terminates" rather than the cleaner "validation fails before any service is configured."

  This is the correct pattern for this scenario (since `IOptions<T>` is not available during `builder.Services.Add*` registration), but the fallback `?? new JwtOptions()` silently produces an object with `Authority = string.Empty` which then silently configures JWT with an invalid Authority. Without the fallback, a null dereference would occur at this line, making the missing config more obvious.
- **Suggested fix:** Remove the null-coalescing fallback:
  ```csharp
  var jwt = builder.Configuration
      .GetSection(JwtOptions.SectionName)
      .Get<JwtOptions>()
      ?? throw new InvalidOperationException(
          $"Configuration section '{JwtOptions.SectionName}' is missing or invalid. " +
          "Ensure Jwt__Authority and Jwt__Audience are set via environment variables.");
  ```
  This makes a missing `Jwt` configuration section an immediate, diagnosable startup failure rather than a silent misconfiguration that `ValidateOnStart()` catches slightly later.

---

## Suggestions

### [SUGG-1] `GlobalExceptionMiddleware` remarks state it wraps "ALL exceptions (including 401/403)" — this comment is inaccurate; 401/403 are handled by `JwtBearerEvents`, not by the middleware

- **File:** `src/PolicyManagement.API/Middleware/GlobalExceptionMiddleware.cs`
- **Lines:** 17–21 (class-level `<remarks>`)
- **Rule:** Code Quality — XML doc comments must accurately describe the code (`.github/copilot-instructions.md`)
- **Description:** The XML `<remarks>` block states:
  > "Must be registered **before** `UseAuthentication()` so that ASP.NET Core's default bare 401/403 challenge responses are also intercepted and wrapped as `ProblemDetails`."

  `GlobalExceptionMiddleware` catches only unhandled C# exceptions (via `try { await next(context); } catch`). ASP.NET Core's auth challenge/forbid handling works by short-circuiting the pipeline — it sets `context.Response.StatusCode` and calls `context.Response.WriteAsJsonAsync()` directly, without throwing an exception. The `GlobalExceptionMiddleware` try/catch does not intercept short-circuit responses.

  The actual 401/403 `ProblemDetails` wrapping is implemented in `Program.cs` via `JwtBearerEvents.OnChallenge` and `JwtBearerEvents.OnForbidden`. The comment gives the impression that `GlobalExceptionMiddleware` handles 401/403 directly, which is misleading.

- **Suggested fix:** Update the `<remarks>`:
  ```xml
  /// <remarks>
  /// <para>
  /// Catches all unhandled C# exceptions thrown during request processing.
  /// Must be registered <strong>before</strong> <c>UseAuthentication()</c> so that any
  /// unexpected exceptions thrown by authentication or authorization middleware are also
  /// captured and formatted as <c>ProblemDetails</c>.
  /// </para>
  /// <para>
  /// Note: 401 and 403 auth challenge/forbid responses are handled separately via
  /// <c>JwtBearerEvents.OnChallenge</c> and <c>JwtBearerEvents.OnForbidden</c> in
  /// <c>Program.cs</c> — those are short-circuit responses, not exceptions.
  /// </para>
  /// ...
  /// </remarks>
  ```

---

### [SUGG-2] `CurrentUserService` captures `HttpContext.User` at construction time — consider per-property lazy access for defensive robustness

- **File:** `src/PolicyManagement.API/Services/CurrentUserService.cs`
- **Line:** 32
- **Rule:** Code Quality — defensive design (`.github/copilot-instructions.md`)
- **Description:** The constructor captures `ClaimsPrincipal` eagerly:
  ```csharp
  private readonly ClaimsPrincipal? _user =
      httpContextAccessor.HttpContext?.User;
  ```
  Because `CurrentUserService` is Scoped (one per HTTP request), this works correctly for the normal request lifecycle — by the time a handler is executed, `HttpContext.User` is fully populated by `UseAuthentication()`. However, if the service is ever resolved in a context where the `HttpContext` exists but authentication has not yet run (e.g., in middleware registered before `UseAuthentication()`), `HttpContext.User` would be an unauthenticated `ClaimsPrincipal` and `_user` would be captured with no claims. Per-property access (`httpContextAccessor.HttpContext?.User?.FindFirst(...)`) would always see the current state of `HttpContext.User`.

  For the current middleware pipeline order (auth runs before controllers/handlers), this is safe. The suggestion is defensive.
- **Suggested fix:** Replace the constructor field with per-property lazy access via the stored accessor:
  ```csharp
  private readonly IHttpContextAccessor _httpContextAccessor = httpContextAccessor;

  private ClaimsPrincipal? User => _httpContextAccessor.HttpContext?.User;

  public string? UserId => User?.FindFirst("sub")?.Value;
  public string? Email  => User?.FindFirst("email")?.Value;
  ```
  Remove the `private readonly ClaimsPrincipal? _user` field entirely.

---

## What Looks Good

- **`PoliciesController`** is `sealed`, uses primary constructor with `IMediator`, and has `[Authorize]` at the class level — all four actions require a valid JWT by default with no exceptions.
- **`[Authorize(Policy = "PolicyWrite")]`** at action level on `FlagPoliciesAsync` — correctly placed at the action level (not class level), so the other three read-only actions are not over-privileged.
- **No `[AllowAnonymous]`** on any action — compliant with the authentication rules in `ADR-007`.
- **`PATCH /flag` returns `NoContent()`** (`204`) — correct per the OpenAPI spec and the architecture decision.
- **`FlagPoliciesRequest`** is a `sealed record` in `API/Models/` — correctly placed in the API layer, separate from the application `FlagPoliciesCommand`. The controller maps the request model to the command (`new FlagPoliciesCommand(request.PolicyIds)`) without leaking API concerns into the Application layer.
- **`[ProducesResponseType]` annotations on `PATCH /flag`** cover all six declared status codes: `204`, `400`, `401`, `403`, `404`, `409`, `500` — complete and accurate.
- **`{id:guid}` route constraint** on `GetPolicyByIdAsync` — prevents non-GUID segments from reaching the action, avoiding unnecessary handler invocations on malformed IDs.
- **Middleware pipeline order in `Program.cs`** is exactly correct: `CorrelationIdMiddleware` → `GlobalExceptionMiddleware` → `UseAuthentication()` → `UseAuthorization()` → `MapControllers()` — compliant with the mandatory ordering in `ADR-007` and `.github/copilot-instructions.md`.
- **`OnChallenge` and `OnForbidden` overrides** in `JwtBearerEvents` return `ProblemDetails` with `Content-Type: application/problem+json` and include the `correlationId` extension — satisfying the auth error format requirement in `ADR-007`.
- **`app.MapHealthChecks("/health/live")` and `app.MapHealthChecks("/health/ready")`** have no `.RequireAuthorization()` call — health check endpoints are correctly accessible without a JWT token.
- **Swagger gated**: `app.MapOpenApi()` is inside `if (app.Environment.IsDevelopment())` — not exposed in production.
- **`JwtOptions.ValidateOnStart()`** registered in `Program.cs` — missing `Jwt__Authority` or `Jwt__Audience` env vars cause an immediate startup failure before any request is served.
- **`appsettings.json`** has no `Jwt` section — all JWT config must be supplied via environment variables. `ConnectionStrings.DefaultConnection` is empty string — no credentials committed to source control.
- **`appsettings.Development.json`** contains only log-level overrides — no development secrets.
- **`docker-compose.yml`** reads `SA_PASSWORD`, `KEYCLOAK_ADMIN`, `KEYCLOAK_ADMIN_PASSWORD` from environment variables (`${VAR}`) — no hardcoded credentials.
- **Dockerfile** runs the process as a non-root user (`USER appuser`, `uid 1000`) — compliant with production security requirements.
- **`CurrentUserService`** is in `API/Services/` and `ICurrentUserService` is in `Application/Interfaces/` — correct layering per `ADR-007`. Handlers never reference `IHttpContextAccessor` directly.
- **`GlobalExceptionMiddleware`** maps all four exception types correctly: `PolicyNotFoundException → 404`, `InvalidPolicyStateException → 409`, `ValidationException → 400`, any other → 500. Stack traces are never included in responses. `correlationId` is present in every `ProblemDetails` response. Field-level `errors` extension populated for 400 responses.
- **`CorrelationIdMiddleware`** generates a new GUID when `X-Correlation-ID` header is absent, stores it in `HttpContext.Items["CorrelationId"]`, and echoes it back in the response header — satisfying the correlation ID requirement for all log entries.
- **`ICurrentUserService`** uses `UserId`, `Email`, `Roles`, `IsInRole(string)` — matches the interface contract defined in `Application/Interfaces/ICurrentUserService.cs`.
- **Keycloak `realm_access.roles` parsing** in `CurrentUserService.ExtractRoles()` is wrapped in a try/catch for `JsonException` — malformed claims silently return an empty roles list rather than crashing the request.
- **110/110 tests passing** (Domain + Application) — zero regressions.
