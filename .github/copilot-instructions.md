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
