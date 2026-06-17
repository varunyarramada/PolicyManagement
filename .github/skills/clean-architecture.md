# Skill: Clean Architecture — PolicyManagement BFF

**Audience:** Architect agent, Backend Developer agents
**Project:** PolicyManagement BFF — Chubb APAC
**Runtime:** .NET 10 / C# · ASP.NET Core Web API · EF Core · MediatR

---

## What Is Clean Architecture and Why Is It Used Here

Clean Architecture (Robert C. Martin) organises code into concentric layers where **dependencies always point inward**. The innermost layer — the domain — knows nothing about frameworks, databases, HTTP, or any external concern. Outer layers depend on inner ones; inner layers never depend on outer ones.

For the PolicyManagement BFF this matters for several reasons:

- **Testability.** Business rules in `Domain` and `Application` have no dependency on ASP.NET Core or EF Core, so they can be unit-tested with no infrastructure setup.
- **Replaceability.** The caching implementation (currently in-memory) can be swapped for Redis, and the event publisher (currently in-memory) can be swapped for Kafka, without touching a single handler or entity.
- **Explicit boundaries.** Each layer has a clearly defined responsibility. New developers know exactly where to add a query handler, a repository method, or a controller action.
- **Longevity.** Infrastructure choices (ORM, cache, message broker) are implementation details, not core assumptions.

---

## The Four Layers

```
┌──────────────────────────────────────┐
│              API Layer               │  ← ASP.NET Core, controllers, middleware
├──────────────────────────────────────┤
│          Application Layer           │  ← Use cases, CQRS handlers, validators
├──────────────────────────────────────┤
│            Domain Layer              │  ← Entities, rules, interfaces (no deps)
├──────────────────────────────────────┤
│        Infrastructure Layer          │  ← EF Core, SQL, cache, events, HTTP
└──────────────────────────────────────┘
```

Allowed dependency directions:

```
API  →  Application  →  Domain  ←  Infrastructure
```

`Domain` is depended upon by everything. It depends on nothing.

---

## Layer Responsibilities

### Domain — `PolicyManagement.Domain`

The pure business core. **Zero** external dependencies — no NuGet packages beyond the .NET BCL.

| What lives here | Examples |
|---|---|
| Entities (identity + state + invariants) | `Policy`, `Policyholder`, `Underwriter` |
| Value objects (immutable, no identity) | `PolicyNumber`, `Money`, `DateRange` |
| Domain events (plain C# records) | `PolicyCreatedEvent`, `PolicyFlaggedEvent` |
| Domain exceptions | `PolicyNotFoundException`, `InvalidPolicyStateException` |
| Repository interfaces | `IPolicyRepository`, `IClaimRepository` |
| `IEventPublisher` interface signature | Publishing contract only — no implementation |

Illustrative signatures:

```csharp
// Domain/Interfaces/IPolicyRepository.cs
public interface IPolicyRepository
{
    Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct);
    Task<PagedResult<Policy>> GetPagedAsync(PolicyFilter filter, CancellationToken ct);
    Task UpdateAsync(Policy policy, CancellationToken ct);
}

// Domain/Interfaces/IEventPublisher.cs
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : class;
}
```

---

### Application — `PolicyManagement.Application`

Orchestrates use cases. Depends **only on `Domain`**. No EF Core, no SQL, no HTTP, no cache implementations — only interfaces and domain types.

| What lives here | Examples |
|---|---|
| MediatR commands (write operations) | `FlagPoliciesCommand`, `CreatePolicyCommand` |
| MediatR queries (read operations) | `GetPoliciesQuery`, `GetPolicyByIdQuery`, `GetPolicySummaryQuery` |
| Handlers (one per command / query) | `GetPolicyByIdQueryHandler`, `FlagPoliciesCommandHandler` |
| DTOs and response models | `PolicyDto`, `PolicySummaryResponse` |
| Input validators | `GetPoliciesQueryValidator`, `FlagPoliciesCommandValidator` |
| Mapping logic | `PolicyMappingProfile` |
| `ICacheService` interface | Cache abstraction — defined here, implemented in Infrastructure |
| `ICurrentUserService` interface | User identity abstraction for handlers — defined here, implemented in API |
| Other application service interfaces | Any application-level contract |

Illustrative signatures:

```csharp
// Application/Interfaces/ICacheService.cs
public interface ICacheService
{
    Task<T?> GetAsync<T>(string key, CancellationToken ct);
    Task SetAsync<T>(string key, T value, TimeSpan ttl, CancellationToken ct);
    Task RemoveAsync(string key, CancellationToken ct);
}

// Application/Features/Policies/Queries/GetPolicyById/GetPolicyByIdQuery.cs
public record GetPolicyByIdQuery(Guid PolicyId) : IRequest<PolicyDto>;

// Application/Features/Policies/Queries/GetPolicyById/GetPolicyByIdQueryHandler.cs
public sealed class GetPolicyByIdQueryHandler
    : IRequestHandler<GetPolicyByIdQuery, PolicyDto>
{
    private readonly IPolicyRepository _repository;   // Domain interface
    private readonly ICacheService _cache;            // Application interface

    public GetPolicyByIdQueryHandler(
        IPolicyRepository repository,
        ICacheService cache) { ... }

    public async Task<PolicyDto> Handle(
        GetPolicyByIdQuery query, CancellationToken ct) { ... }
}
```

---

### Infrastructure — `PolicyManagement.Infrastructure`

Implements every interface defined in `Domain` and `Application`. Depends on both those layers. All I/O lives here — database, cache, event bus, and future HTTP clients.

| What lives here | Implements |
|---|---|
| `PolicyDbContext` | EF Core DbContext for SQL Server |
| `PolicyRepository` | `IPolicyRepository` (Domain) |
| `InMemoryCacheService` | `ICacheService` (Application) |
| `InMemoryEventPublisher` | `IEventPublisher` (Domain) |
| EF entity configurations | `IEntityTypeConfiguration<T>` per entity |
| `HttpClients/` folder | Reserved for future downstream service integrations |

Infrastructure never exposes EF tracking proxies or framework-specific types to outer layers. Repositories always return domain entities or value objects.

```csharp
// Infrastructure/Persistence/Repositories/PolicyRepository.cs
public sealed class PolicyRepository : IPolicyRepository
{
    private readonly PolicyDbContext _context;

    public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct)
        => await _context.Policies
               .AsNoTracking()
               .FirstOrDefaultAsync(p => p.Id == id, ct);
}
```

---

### API — `PolicyManagement.API`

Entry point and composition root. Depends on `Application` (for command/query types) and `Infrastructure` (only in `Program.cs` for DI wiring). Controllers contain **no business logic** — they translate HTTP requests into MediatR commands or queries and return the result.

| What lives here | Examples |
|---|---|
| Controllers | `PoliciesController` |
| Global exception middleware | `GlobalExceptionMiddleware` → RFC 7807 `ProblemDetails` |
| DI composition root | `Program.cs` — registers all services, middleware, health checks |
| Pipeline filters | `ValidationFilter`, `CorrelationIdFilter` |
| Health checks | `/health/live`, `/health/ready` |
| OpenAPI / Swagger configuration | |
| `CurrentUserService` implementation | Implements `ICurrentUserService` using `IHttpContextAccessor` |
| JWT Bearer authentication registration | Registers authentication and authorization middleware in `Program.cs` |
| `JwtOptions` configuration class | Strongly-typed JWT configuration (`Authority`, `Audience`, `RequireHttpsMetadata`) |

Controller pattern:

```csharp
// API/Controllers/PoliciesController.cs
[HttpGet("{id:guid}")]
[ProducesResponseType(typeof(PolicyDto), StatusCodes.Status200OK)]
[ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
public async Task<IActionResult> GetById(
    Guid id, CancellationToken cancellationToken)
{
    var result = await _mediator.Send(new GetPolicyByIdQuery(id), cancellationToken);
    return Ok(result);
}
```

---

## Dependency Rules — Reference Table

| From → To | Allowed | Notes |
|---|---|---|
| `Domain` → anything | **No** | Domain is the innermost layer; zero outward dependencies |
| `Application` → `Domain` | **Yes** | Core dependency — entities, interfaces, exceptions |
| `Application` → `Infrastructure` | **No** | Application must not know about EF Core, SQL, etc. |
| `Application` → `API` | **No** | Application is not aware of HTTP |
| `Application` → `ClaimsPrincipal`, `HttpContext`, `IHttpContextAccessor` | **No** | Use `ICurrentUserService` abstraction instead |
| `Domain` → `ICurrentUserService` or any auth concept | **No** | Domain has zero awareness of authentication |
| `Infrastructure` → `Domain` | **Yes** | Implements repository interfaces; uses entities |
| `Infrastructure` → `Application` | **Yes** | Implements `ICacheService` and other application interfaces |
| `Infrastructure` → `API` | **No** | Infrastructure is not aware of HTTP |
| `API` → `Application` | **Yes** | Controllers dispatch commands/queries via MediatR |
| `API` → `Domain` | **Yes** | Direct reference to domain types is acceptable in controllers |
| `API` → `Infrastructure` | **Yes** (restricted) | Only in `Program.cs` for DI wiring — never in controllers |

---

## What Belongs Where — PolicyManagement Specifics

### Domain types

- `Policy` entity — ID, policy number (`POL-XXXXXX`), status (`Active / Expired / Pending / Cancelled`), premium, currency, line of business, region, holder, underwriter, dates
- `Policyholder` entity — ID, name, contact
- `Underwriter` entity — ID, name, region
- `PolicyNumber` value object — encapsulates and validates `POL-XXXXXX` format
- `Money` value object — amount + currency (USD, SGD, HKD, AUD, JPY, THB)
- `PolicyStatus` enumeration
- `PolicyNotFoundException` — thrown when a requested policy does not exist
- `InvalidPolicyStateException` — thrown when a state transition is illegal
- `PolicyFlaggedEvent` — raised when a policy is flagged for review

### Application types

- `GetPoliciesQuery` — pagination, filtering (status, region, line of business), sorting, full-text search params
- `GetPolicyByIdQuery`
- `GetPolicySummaryQuery` — aggregated stats (total count, by status, by region, premium totals)
- `FlagPoliciesCommand` — bulk flag a set of policy IDs
- Corresponding handlers for all of the above
- `PolicyDto`, `PolicySummaryResponse`, `PagedResponse<T>`
- `GetPoliciesQueryValidator`, `FlagPoliciesCommandValidator`

### Infrastructure types

- `PolicyDbContext` with `DbSet<Policy>`, `DbSet<Policyholder>`, `DbSet<Underwriter>`
- `PolicyRepository`, `PolicyholderRepository`
- `InMemoryCacheService` (implements `ICacheService`)
- `InMemoryEventPublisher` (implements `IEventPublisher`)

### API types

- `PoliciesController` with actions mapping to the four endpoints
- `GlobalExceptionMiddleware`
- Health check registrations for SQL Server

---

## Common Violations to Avoid

### EF Core types in Application or Domain

```csharp
// WRONG — DbContext injected into an Application handler
public class GetPolicyByIdQueryHandler
{
    private readonly PolicyDbContext _context; // infrastructure concern leaked inward
}

// CORRECT — depend on the repository interface from Domain
public class GetPolicyByIdQueryHandler
{
    private readonly IPolicyRepository _repository;
    private readonly ICacheService _cache;
}
```

---

### Business logic in controllers

```csharp
// WRONG — controller makes domain decisions
public async Task<IActionResult> Flag(Guid id)
{
    var policy = await _repository.GetByIdAsync(id); // direct repo access
    if (policy.Status == "Active")
        policy.Status = "Flagged";                   // domain mutation in HTTP layer
    await _repository.UpdateAsync(policy);
    return Ok();
}

// CORRECT — controller dispatches and returns
public async Task<IActionResult> Flag(
    [FromBody] FlagPoliciesRequest request, CancellationToken ct)
{
    await _mediator.Send(new FlagPoliciesCommand(request.PolicyIds), ct);
    return NoContent();
}
```

---

### Returning EF tracking proxies from repositories

```csharp
// WRONG — tracked entity escapes Infrastructure
public async Task<Policy> GetByIdAsync(Guid id)
    => await _context.Policies.FindAsync(id);

// CORRECT — detach or use AsNoTracking for queries
public async Task<Policy?> GetByIdAsync(Guid id, CancellationToken ct)
    => await _context.Policies
           .AsNoTracking()
           .FirstOrDefaultAsync(p => p.Id == id, ct);
```

---

### Hardcoded configuration values

```csharp
// WRONG — infrastructure detail embedded in code
optionsBuilder.UseSqlServer("Server=prod-sql;Database=PolicyDb;...");

// CORRECT — externalised and bound to a strongly-typed options class
services.Configure<SqlServerOptions>(configuration.GetSection("SqlServer"));
// then: inject IOptions<SqlServerOptions> where needed
```

---

### Raw `IConfiguration` access in business code

```csharp
// WRONG — raw key lookup in a handler
var ttl = int.Parse(_configuration["Cache:Ttl"]);

// CORRECT — inject strongly-typed options
public GetPoliciesQueryHandler(IOptions<CacheOptions> cacheOptions)
{
    _ttl = cacheOptions.Value.DefaultTtl;
}
```

---

### Ignoring `CancellationToken`

```csharp
// WRONG — token not forwarded to repository
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
    => await _repository.GetByIdAsync(query.PolicyId); // ct dropped

// CORRECT
public async Task<PolicyDto> Handle(GetPolicyByIdQuery query, CancellationToken ct)
    => await _repository.GetByIdAsync(query.PolicyId, ct);
```

---

### String interpolation in log messages

```csharp
// WRONG — loses structured log properties
_logger.LogInformation($"Policy {policyId} retrieved for {customerId}");

// CORRECT — named parameters for structured logging
_logger.LogInformation(
    "Policy {PolicyId} retrieved for customer {CustomerId}",
    policyId, customerId);
```

---

## How the Repository Pattern Supports Clean Architecture

The Repository Pattern enforces the dependency rule at the data access boundary:

1. **Interface in `Domain`** — `IPolicyRepository` declares what data operations are needed using only domain types as parameters and return types. `Domain` has no knowledge of SQL, EF Core, or any persistence technology.

2. **Implementation in `Infrastructure`** — `PolicyRepository` implements the interface using `PolicyDbContext`. EF Core is confined to this single layer.

3. **Application uses only the interface** — handlers receive `IPolicyRepository` via constructor injection. They never reference `PolicyDbContext` or any EF LINQ extension.

4. **Result:** Swapping the database engine (e.g., from SQL Server to PostgreSQL) requires changing only the Infrastructure project — no Application or Domain code changes.

---

## How MediatR Handlers Fit into the Application Layer

MediatR provides the in-process messaging mechanism for logical CQRS. Commands and queries are plain C# records (`IRequest<T>`). Handlers are the only place business orchestration occurs.

```
HTTP Request
     ↓
Controller (API layer)
     ↓  _mediator.Send(new GetPolicyByIdQuery(id), ct)
MediatR pipeline
     ↓  ValidationPipelineBehavior (Application)
     ↓  LoggingPipelineBehavior     (Application)
GetPolicyByIdQueryHandler (Application)
     ↓  _repository.GetByIdAsync(...)
IPolicyRepository (Domain interface)
     ↓
PolicyRepository (Infrastructure implementation)
     ↓
PolicyDbContext → SQL Server
```

Key rules for handlers:
- One handler per command or query — no shared handlers.
- Handlers may call repository interfaces, `ICacheService`, and `IEventPublisher`.
- Handlers never reference `DbContext`, `HttpContext`, or any infrastructure type directly.
- All handler methods accept and forward `CancellationToken`.
- Handlers raise domain events through `IEventPublisher` after a successful write.

Pipeline behaviours (e.g., `ValidationPipelineBehavior`, `LoggingPipelineBehavior`) are registered in the Application layer and apply cross-cutting concerns without modifying individual handlers.

---

## How Abstractions Enable Future Swaps

### Swapping in-memory cache → Redis

`ICacheService` is defined in `Application`. The current implementation `InMemoryCacheService` lives in `Infrastructure` and uses `IMemoryCache`. When Redis is introduced:

1. Add `RedisCacheService : ICacheService` in `Infrastructure` using `IDistributedCache` or `StackExchange.Redis`.
2. Change the DI registration in `Program.cs` from `InMemoryCacheService` to `RedisCacheService`.
3. Zero changes to any handler or domain type.

Cache key design must be deterministic and namespaced from the start (e.g., `"policy:v1:{id}"`) so Redis keys are meaningful and collision-free.

### Swapping in-memory event publisher → Kafka

`IEventPublisher` is defined in `Domain`. The current `InMemoryEventPublisher` in `Infrastructure` logs events locally. When Kafka is introduced:

1. Add `KafkaEventPublisher : IEventPublisher` in `Infrastructure` using the Confluent Kafka client.
2. Change the DI registration in `Program.cs`.
3. Zero changes to domain events, handlers, or entities.

Domain event records must be serialisable (no circular references, no EF proxies) to ensure they can be produced to Kafka topics without modification.

---

## Folder Structure Reference

```
PolicyManagement/
├── src/
│   ├── PolicyManagement.Domain/
│   │   ├── Entities/
│   │   │   ├── Policy.cs
│   │   │   ├── Policyholder.cs
│   │   │   └── Underwriter.cs
│   │   ├── ValueObjects/
│   │   │   ├── PolicyNumber.cs
│   │   │   └── Money.cs
│   │   ├── Events/
│   │   │   └── PolicyFlaggedEvent.cs
│   │   ├── Exceptions/
│   │   │   ├── PolicyNotFoundException.cs
│   │   │   └── InvalidPolicyStateException.cs
│   │   └── Interfaces/
│   │       ├── IPolicyRepository.cs
│   │       └── IEventPublisher.cs
│   │
│   ├── PolicyManagement.Application/
│   │   ├── Features/
│   │   │   └── Policies/
│   │   │       ├── Commands/
│   │   │       │   └── FlagPolicies/
│   │   │       │       ├── FlagPoliciesCommand.cs
│   │   │       │       ├── FlagPoliciesCommandHandler.cs
│   │   │       │       └── FlagPoliciesCommandValidator.cs
│   │   │       └── Queries/
│   │   │           ├── GetPolicies/
│   │   │           │   ├── GetPoliciesQuery.cs
│   │   │           │   ├── GetPoliciesQueryHandler.cs
│   │   │           │   └── GetPoliciesQueryValidator.cs
│   │   │           ├── GetPolicyById/
│   │   │           │   ├── GetPolicyByIdQuery.cs
│   │   │           │   └── GetPolicyByIdQueryHandler.cs
│   │   │           └── GetPolicySummary/
│   │   │               ├── GetPolicySummaryQuery.cs
│   │   │               └── GetPolicySummaryQueryHandler.cs
│   │   ├── DTOs/
│   │   │   ├── PolicyDto.cs
│   │   │   ├── PolicySummaryResponse.cs
│   │   │   └── PagedResponse.cs
│   │   ├── Interfaces/
│   │   │   ├── ICacheService.cs
│   │   │   └── ICurrentUserService.cs         # User identity abstraction
│   │   ├── Behaviours/
│   │   │   ├── ValidationPipelineBehavior.cs
│   │   │   └── LoggingPipelineBehavior.cs
│   │   ├── Validators/
│   │   │   ├── GetPoliciesQueryValidator.cs
│   │   │   └── FlagPoliciesCommandValidator.cs
│   │   └── Mappings/
│   │       └── PolicyMappingProfile.cs
│   │
│   ├── PolicyManagement.Infrastructure/
│   │   ├── Persistence/
│   │   │   ├── PolicyDbContext.cs
│   │   │   ├── Migrations/                      # EF Core generated migrations
│   │   │   ├── Seed/                            # Database seeder — 200+ policy records
│   │   │   │   └── PolicyDataSeeder.cs
│   │   │   ├── Configurations/
│   │   │   │   ├── PolicyConfiguration.cs
│   │   │   │   └── PolicyholderConfiguration.cs
│   │   │   └── Repositories/
│   │   │       └── PolicyRepository.cs
│   │   ├── Caching/
│   │   │   └── InMemoryCacheService.cs      # implements ICacheService
│   │   ├── Events/
│   │   │   └── InMemoryEventPublisher.cs    # implements IEventPublisher
│   │   └── HttpClients/                     # Reserved — not implemented yet
│   │
│   └── PolicyManagement.API/
│       ├── Controllers/
│       │   └── PoliciesController.cs
│       ├── Middleware/
│       │   └── GlobalExceptionMiddleware.cs
│       ├── Filters/
│       │   └── CorrelationIdFilter.cs
│       ├── Services/
│       │   └── CurrentUserService.cs        # Implements ICurrentUserService
│       ├── Configuration/
│       │   └── JwtOptions.cs                # JWT configuration class
│       ├── HealthChecks/
│       │   └── SqlServerHealthCheck.cs
│       └── Program.cs
│
├── tests/
│   ├── PolicyManagement.Domain.Tests/
│   ├── PolicyManagement.Application.Tests/
│   ├── PolicyManagement.Infrastructure.Tests/
│   └── PolicyManagement.API.IntegrationTests/
│
└── docs/
    └── openapi/                             # OpenAPI 3.x spec — source of truth
```
