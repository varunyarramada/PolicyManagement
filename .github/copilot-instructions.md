# GitHub Copilot Instructions — PolicyManagement BFF Service

## Project Overview

**PolicyManagement** is a production-grade Backend-for-Frontend (BFF) service built for **Chubb APAC**. It acts as an orchestration and aggregation layer between the insurance policy management frontend dashboard and downstream systems (policy engines, claims services, customer data APIs, etc.).

- **Runtime:** .NET 10 / C#
- **Framework:** ASP.NET Core Web API
- **Database ORM:** EF Core with SQL Server
- **Testing:** xUnit
- **API Design:** Contract-first with OpenAPI 3.x
- **Caching:** In-memory (abstracted behind interface; must support future Redis swap)
- **Eventing:** Abstracted event publisher (must support future Kafka swap)
- **Data source:** SQL Server via EF Core. No downstream HTTP service calls are required in this implementation. The `HttpClients/` folder is reserved for future integrations only.

---

## Key Documentation & Decisions

### Skills (Implementation Guides)

- `.github/skills/authentication.md` — JWT Bearer authentication with Keycloak
- `.github/skills/clean-architecture.md` — Layer boundaries and dependency rules
- `.github/skills/contract-first-api.md` — OpenAPI 3.x spec-first development
- `.github/skills/error-handling.md` — ProblemDetails and global exception middleware
- `.github/skills/production-readiness.md` — Health checks, configuration, deployment
- `.github/skills/testing-standards.md` — xUnit conventions, FluentAssertions, test naming

### Architecture Decision Records

- **ADR-001:** Clean Architecture — Domain-centric layering with inward dependencies
- **ADR-002:** Logical CQRS with MediatR — Separated read/write handlers, single database
- **ADR-003:** Repository Pattern — Abstraction over data access in Domain layer
- **ADR-004:** ICacheService Abstraction — In-memory cache with future Redis swap
- **ADR-005:** IEventPublisher Abstraction — In-memory events with future Kafka swap
- **ADR-006:** Database Indexing Strategy — Composite indexes for query performance
- **ADR-007:** JWT Bearer Authentication — Keycloak as identity provider, JWT Bearer token validation, role-based access control

---

## Architecture — Clean Architecture

This project follows **Clean Architecture** with strict inward-pointing dependencies.

```
API  →  Application  →  Domain  ←  Infrastructure
```

### Layer Responsibilities

| Layer | Responsibility |
|---|---|
| `Domain` | Entities, value objects, domain events, repository interfaces, domain exceptions |
| `Application` | Use cases (commands/queries), DTOs, service interfaces, validation, mapping |
| `Infrastructure` | EF Core DbContext, repository implementations, caching, event publishing, external HTTP clients |
| `API` | Controllers, middleware, filters, request/response models, OpenAPI config, health checks |

### Dependency Rules (enforce strictly)

- `Domain` has **zero** dependencies on other layers or any infrastructure packages.
- `Application` depends only on `Domain`. Never reference EF Core, SQL Server, or any infrastructure concern here.
- `Infrastructure` depends on `Domain` and `Application` (for interface implementations only).
- `API` depends on `Application` and `Infrastructure` (for DI wiring only).

**Violations to reject:**
- EF Core types (`DbContext`, `DbSet`, LINQ-to-SQL) in `Domain` or `Application`
- Business logic in controllers
- `HttpContext` or ASP.NET types outside the `API` layer
- Concrete infrastructure types injected directly anywhere except `Program.cs` / DI composition root

---

## Engineering Standards

### SOLID & DRY

- Every class has a single, well-defined responsibility.
- Depend on abstractions (interfaces), not concrete implementations.
- Extend behaviour through composition and interfaces, not inheritance chains.
- Extract shared logic into named helpers or base classes — never duplicate logic across handlers.

### Contract-First API Design

- The OpenAPI 3.x specification is the **source of truth**. Write the spec before writing code.
- Controllers implement the contract; they do not define it.
- All request and response schemas must match the OpenAPI spec exactly.
- Use `[ProducesResponseType]` annotations on every endpoint.
- Version APIs under `/api/v{version}/` from day one.

### CQRS with MediatR
- Use logical CQRS via MediatR (single database, separated read/write handlers)
- Commands handle write operations (flag, update)
- Queries handle read operations (list, get by id, summary)
- Controllers only call MediatR Send() — no business logic in controllers
- Do not use physical CQRS (no separate read/write databases)

### Repository Pattern

- Repository interfaces live in the `Domain` layer.
- Repository implementations live in `Infrastructure`.
- Application layer uses only the interfaces — never `DbContext` directly.
- Repositories return domain entities or value objects, never EF Core tracking proxies passed up the stack.

### Caching

- All cache access goes through `ICacheService` (defined in `Application`).
- The in-memory implementation lives in `Infrastructure`.
- Cache keys must be deterministic, namespaced, and documented on the interface method.
- Design cache entries to be swappable to Redis without changing calling code.

### Event Publishing

- All event publishing goes through `IEventPublisher` (defined in `Application`).
- The in-memory/stub implementation lives in `Infrastructure`.
- Events are plain C# records in the `Domain` layer with no infrastructure dependencies.
- Design for future Kafka integration — event payloads must be serialisable without modification.

### Configuration

- No hardcoded connection strings, secrets, URLs, or environment-specific values anywhere in the codebase.
- All configuration is externalised via `appsettings.json`, environment variables, or secret stores.
- Bind configuration sections to strongly-typed options classes (`IOptions<T>`).
- Follow 12-factor app principles: configuration comes from the environment.

### Error Handling

- Register a global exception-handling middleware in `API`.
- All unhandled exceptions are caught, logged with full context, and returned as RFC 7807 `ProblemDetails` responses.
- Domain validation failures return `400 Bad Request` with field-level error details.
- Never expose stack traces or internal exception messages in API responses.
- Define custom domain exception types in `Domain` (e.g., `PolicyNotFoundException`, `InvalidPolicyStateException`).

### Authentication & Authorization

> **Implementation details:** See `.github/skills/authentication.md` and [ADR-007](docs/architecture/decisions/ADR-007-jwt-bearer-authentication.md).

#### JWT Bearer Authentication

- All four API endpoints require a valid JWT Bearer token (`401 Unauthorized` if missing or invalid).
- Use `Microsoft.AspNetCore.Authentication.JwtBearer` in the `API` project only. Never install auth packages in `Domain` or `Application`.
- **Keycloak** is the identity provider (self-hosted, Docker, Apache 2.0 license). The BFF validates tokens — it never issues them.
- Tokens are issued by Keycloak, validated by the BFF using the `Authority` (Keycloak realm URL) and `Audience` (client ID) from configuration.
- Health check endpoints (`/health/live`, `/health/ready`) do **not** require authentication — they are infrastructure endpoints for container orchestration.

#### Authorization

- Apply `[Authorize]` at the **controller class level** on `PoliciesController` — all actions require a valid JWT by default.
- Apply `[Authorize(Policy = "PolicyWrite")]` at the **action level** on the `PATCH /flag` endpoint — requires the `Policy.Write` role claim.
- Never use `[AllowAnonymous]` on any policy endpoint.
- **Roles:**
  - `Policy.Read` — implicit for any authenticated user with a valid token. Grants access to all `GET` endpoints.
  - `Policy.Write` — explicit role claim required. Grants access to `PATCH /api/v1/policies/flag`. Users without this role receive `403 Forbidden`.

#### ICurrentUserService

- Interface defined in `Application/Interfaces/ICurrentUserService.cs`.
- Properties: `UserId`, `Email`, `Roles` (all extracted from JWT claims).
- Method: `IsInRole(string role)`.
- Implementation lives in `API/Services/CurrentUserService.cs` using `IHttpContextAccessor`.
- Registered as `Scoped` in DI — one instance per HTTP request.
- Handlers that need user identity inject `ICurrentUserService` via constructor injection — they never access `HttpContext`, `ClaimsPrincipal`, or `IHttpContextAccessor` directly.
- `Domain` layer has **zero awareness** of `ICurrentUserService`.

#### JwtOptions Configuration

- Strongly-typed configuration class: `JwtOptions` with `Authority`, `Audience`, `RequireHttpsMetadata`.
- `SectionName = "Jwt"`.
- Validated at startup with `ValidateOnStart()` — misconfiguration fails before the first request.
- Values supplied via environment variables only (`Jwt__Authority`, `Jwt__Audience`, `Jwt__RequireHttpsMetadata`) — never hardcoded in `appsettings.json` or source code.
- No JWT secrets or signing keys committed to source control.

#### Error Responses for Auth Failures

- `401 Unauthorized` — token is missing, expired, has invalid signature, or wrong issuer/audience.
- `403 Forbidden` — token is valid and authenticated, but the user lacks the required role (`Policy.Write` on `PATCH /flag`).
- Both `401` and `403` must be returned as RFC 7807 `ProblemDetails` with `Content-Type: application/problem+json`.
- Override `JwtBearerEvents.OnChallenge` and `OnForbidden` to return `ProblemDetails` instead of bare status codes.
- Stack traces are never exposed in `401` or `403` responses.

#### Middleware Pipeline Order

The order of middleware registration in `Program.cs` is critical:

1. `app.UseMiddleware<CorrelationIdMiddleware>()` — first, so all log entries carry the correlation ID
2. `app.UseMiddleware<GlobalExceptionMiddleware>()` — before auth, so that 401/403 failures are caught and formatted as `ProblemDetails`
3. `app.UseAuthentication()` — validates JWT token, populates `HttpContext.User`
4. `app.UseAuthorization()` — evaluates `[Authorize]` policies
5. `app.MapControllers()` — routes requests to controllers
6. `app.MapHealthChecks(...)` — no authentication required

**Why `GlobalExceptionMiddleware` before `UseAuthentication()`?** ASP.NET Core's default auth challenge/forbid handling returns bare `401`/`403` with no body. Placing `GlobalExceptionMiddleware` first intercepts these and wraps them as `ProblemDetails`.

#### Prohibited Patterns

- `[AllowAnonymous]` on any policy endpoint — all policy data requires authentication.
- Reading `HttpContext.User` directly in handlers — violates Clean Architecture. Use `ICurrentUserService` instead.
- Hardcoding JWT signing keys in source code — security vulnerability.
- Cookie authentication — this is an API, not an MVC app. Use JWT Bearer only.
- Auth logic in `Domain` layer — violates Clean Architecture. Auth stays in `API`; identity abstracted via `ICurrentUserService` in `Application`.
- Creating a custom token issuer in the BFF — Keycloak issues all tokens. The BFF is a validator, not an issuer.
- Accessing `IHttpContextAccessor` outside the `API` layer — couples infrastructure to ASP.NET Core.

---

## Cross-Cutting Concerns (Apply to All Agents)

### Authentication & Authorization

- **All API endpoints require JWT Bearer authentication** — absent or invalid tokens return `401 Unauthorized` with `ProblemDetails` format
- **PATCH /api/v1/policies/flag requires Policy.Write role** — authenticated users without this role receive `403 Forbidden` with `ProblemDetails` format
- **401 and 403 responses use ProblemDetails format** — override `JwtBearerEvents.OnChallenge` and `OnForbidden` to return RFC 7807 `ProblemDetails` instead of bare status codes
- **Health check endpoints (`/health/live`, `/health/ready`) do NOT require authentication** — they are infrastructure endpoints for container orchestration; never apply `[Authorize]` or authentication middleware to these paths
- **No authentication logic in handlers** — apply `[Authorize]` attributes on controllers only; handlers remain unaware of auth and inject `ICurrentUserService` when user identity is needed
- **No JWT secrets in source code or committed config files** — all JWT configuration (`Jwt__Authority`, `Jwt__Audience`, `Jwt__RequireHttpsMetadata`) supplied via environment variables only

### Middleware Pipeline Order

The order of middleware registration in `Program.cs` must follow this exact sequence:

1. **CorrelationIdMiddleware** — first, so all log entries carry the correlation ID
2. **GlobalExceptionMiddleware** — before `UseAuthentication()`, so that 401/403 failures are caught and formatted as `ProblemDetails`
3. **UseAuthentication()** — validates JWT token, populates `HttpContext.User`
4. **UseAuthorization()** — evaluates `[Authorize]` policies
5. **MapControllers()** — routes requests to controllers
6. **MapHealthChecks(...)** — no authentication required

**Rationale:** Placing `GlobalExceptionMiddleware` before `UseAuthentication()` ensures that ASP.NET Core's default auth challenge/forbid responses (bare `401`/`403` with no body) are intercepted and wrapped as `ProblemDetails`.

### Structured Logging

- Use `ILogger<T>` everywhere. Never use `Console.Write*` or `Debug.Write*`.
- Log at appropriate levels: `Information` for normal flow, `Warning` for expected exceptional paths, `Error` for failures.
- Always include correlation IDs, policy IDs, and other relevant context in log scopes.
- Use structured (named) parameters in log messages, not string interpolation:
  ```csharp
  // Correct
  _logger.LogInformation("Policy {PolicyId} retrieved for customer {CustomerId}", policyId, customerId);

  // Wrong
  _logger.LogInformation($"Policy {policyId} retrieved");
  ```

### Health Checks

- Register health checks in `Program.cs` for all critical dependencies (SQL Server, downstream HTTP services).
- Expose `/health/live` (liveness) and `/health/ready` (readiness) endpoints.
- Health check results must not leak sensitive infrastructure details externally.

### Input Validation

- Validate all inputs at the `Application` layer using a dedicated validator (e.g., FluentValidation or custom validators).
- Do not rely on controller model binding alone as the validation boundary.
- Reject invalid requests with structured `400 Bad Request` responses before any domain logic executes.

---

## Testing Standards

### Framework & Organisation

- **xUnit** is the only test framework used.
- Test projects mirror the source project structure under `tests/`.
- Test class names: `{ClassUnderTest}Tests`
- Test method names: `{MethodName}_When{Condition}_Should{ExpectedBehaviour}`

### Coverage Requirements

| Scope | Requirement |
|---|---|
| Application layer (handlers, services, validators) | Unit tests — all public methods |
| API endpoints | Integration tests — all endpoints, all HTTP status code paths |
| Domain logic (entities, value objects) | Unit tests — all invariants and business rules |
| Infrastructure | Integration tests where external deps are involved |

### Test Rules

- Every feature branch must include tests before a PR can be raised.
- Use test doubles (mocks/stubs/fakes) for all external dependencies in unit tests.
- Integration tests for API endpoints use `WebApplicationFactory<Program>`.
- Do not share mutable state between tests.
- Test the behaviour, not the implementation details.

### Example Test Structure

```csharp
public class GetPolicyByIdHandlerTests
{
    private readonly Mock<IPolicyRepository> _repositoryMock = new();
    private readonly GetPolicyByIdHandler _handler;

    public GetPolicyByIdHandlerTests()
    {
        _handler = new GetPolicyByIdHandler(_repositoryMock.Object);
    }

    [Fact]
    public async Task Handle_WhenPolicyExists_ShouldReturnPolicyDto()
    {
        // Arrange
        // Act
        // Assert
    }

    [Fact]
    public async Task Handle_WhenPolicyNotFound_ShouldThrowPolicyNotFoundException()
    {
        // Arrange
        // Act & Assert
    }
}
```

---

## Code Generation Rules

### Always

- Follow the layer dependency rules without exception.
- Use `interface` abstractions for every external dependency (database, cache, events, HTTP clients).
- Validate all inputs before executing domain logic.
- Return structured `ProblemDetails` error responses.
- Log with context using structured parameters.
- Bind configuration to `IOptions<T>` — never call `IConfiguration["key"]` directly in business code.
- Apply `CancellationToken` parameters to all async controller actions and use case handlers.
- Use `async`/`await` throughout — no `.Result` or `.Wait()` calls.
- Dispose resources properly — prefer `using` declarations.

### Never

- Put business logic in controllers. Controllers orchestrate only.
- Reference EF Core, `DbContext`, or any ORM type in `Domain` or `Application`.
- Hardcode connection strings, secrets, API keys, or environment-specific URLs.
- Create God classes (classes with more than one responsibility).
- Skip writing tests for a feature.
- Use `string.Format` or string interpolation in log messages — use structured logging parameters.
- Return raw exception messages or stack traces in API responses.
- Use `static` mutable state.
- Ignore `CancellationToken` in async methods.

---

## Project Structure Reference

```
PolicyManagement/
├── src/
│   ├── PolicyManagement.Domain/
│   │   ├── Entities/
│   │   ├── ValueObjects/
│   │   ├── Events/
│   │   ├── Exceptions/
│   │   └── Interfaces/          # Repository, IEventPublisher (signatures only)
│   │
│   ├── PolicyManagement.Application/
│   │   ├── Features/
│   │   │   └── Policies/
│   │   │       ├── Commands/
│   │   │       └── Queries/
│   │   ├── DTOs/
│   │   ├── Interfaces/           # ICacheService, application service interfaces
│   │   ├── Validators/
│   │   └── Mappings/
│   │
│   ├── PolicyManagement.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── PolicyDbContext.cs
│   │   │   └── Repositories/
│   │   ├── Caching/              # InMemoryCacheService : ICacheService
│   │   ├── Events/               # InMemoryEventPublisher : IEventPublisher
│   │   └── HttpClients/          # Reserved for future downstream service integrations (not used in current implementation)
│   │
│   └── PolicyManagement.API/
│       ├── Controllers/
│       ├── Middleware/           # GlobalExceptionMiddleware
│       ├── Filters/
│       ├── HealthChecks/
│       └── Program.cs
│
├── tests/
│   ├── PolicyManagement.Domain.Tests/
│   ├── PolicyManagement.Application.Tests/
│   ├── PolicyManagement.Infrastructure.Tests/
│   └── PolicyManagement.API.IntegrationTests/
│
├── docs/
│   └── openapi/                  # OpenAPI 3.x spec files (source of truth)
│
└── .github/
    └── copilot-instructions.md
```

---

## Naming Conventions

| Element | Convention | Example |
|---|---|---|
| Commands | `{Verb}{Entity}Command` | `CreatePolicyCommand` |
| Queries | `Get{Entity}By{Key}Query` | `GetPolicyByIdQuery` |
| Handlers | `{CommandOrQuery}Handler` | `CreatePolicyCommandHandler` |
| DTOs | `{Entity}Dto` / `{Entity}Response` | `PolicyDto`, `PolicySummaryResponse` |
| Repository interfaces | `I{Entity}Repository` | `IPolicyRepository` |
| Service interfaces | `I{Name}Service` | `ICacheService` |
| Events | `{Entity}{PastTenseVerb}Event` | `PolicyCreatedEvent` |
| Exceptions | `{Condition}Exception` | `PolicyNotFoundException` |
| Options classes | `{Feature}Options` | `CacheOptions`, `SqlServerOptions` |
| Pipeline Behaviors | `{Name}PipelineBehavior` | `ValidationPipelineBehavior` |

---

## Pull Request Checklist (Copilot-enforced reminders)

When generating code for a feature, ensure the following are present before considering it complete:

- [ ] OpenAPI spec updated or created for new/changed endpoints
- [ ] Domain entities/value objects updated if domain model changed
- [ ] Application command/query handler written
- [ ] Input validator written
- [ ] Repository interface updated if new data access needed
- [ ] Infrastructure implementation updated
- [ ] Controller action written (thin — delegates to handler only)
- [ ] Unit tests for handler and validator
- [ ] Integration test for the API endpoint
- [ ] Structured logging added to handler
- [ ] No hardcoded configuration values
- [ ] `CancellationToken` threaded through all async calls
