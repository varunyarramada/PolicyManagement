# ADR-003: Repository Pattern over Direct DbContext Usage

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The PolicyManagement BFF requires a data access strategy for reading and writing `Policy` records in SQL Server via EF Core. The key question is where the EF Core `DbContext` should be visible, and whether a repository abstraction layer should sit between the Application layer and the data store.

Two principal approaches were considered:

1. **Direct `DbContext` usage in handlers** — MediatR handlers in `Application` inject `PolicyDbContext` directly and compose LINQ queries inline.
2. **Repository Pattern** — An `IPolicyRepository` interface is defined in `Domain`. Its EF Core implementation lives in `Infrastructure`. Handlers in `Application` depend only on the interface.

## Decision

The service uses the **Repository Pattern** with the following placement:

- `IPolicyRepository` is declared in `PolicyManagement.Domain/Interfaces/`.
- `PolicyRepository` (EF Core implementation) lives in `PolicyManagement.Infrastructure/Persistence/Repositories/`.
- All MediatR handlers in `Application` depend on `IPolicyRepository` — never on `PolicyDbContext` directly.
- `PolicyDbContext` is never referenced outside `Infrastructure`.

The repository interface exposes only the operations required by the current use cases:

```
IPolicyRepository
├── GetByIdAsync(Guid id, CancellationToken ct) → Policy?
├── GetPagedAsync(PolicyFilter filter, CancellationToken ct) → PagedResult<Policy>
├── GetSummaryAsync(CancellationToken ct) → PolicySummaryData
├── UpdateRangeAsync(IReadOnlyList<Policy> policies, CancellationToken ct) → void
└── ExistAllAsync(IReadOnlyList<Guid> ids, CancellationToken ct) → bool
```

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **Direct `DbContext` injection in handlers** | Handlers inject `PolicyDbContext` directly. LINQ queries are composed inline in handler code. No separate repository class or interface. | Places EF Core as a core dependency of the Application layer. `Application.csproj` must reference `Microsoft.EntityFrameworkCore`, violating the rule that Application has no infrastructure concern. Handlers cannot be unit-tested without a real or in-memory database — `DbContext` is a concrete class with complex setup requirements. Replacing EF Core with a different ORM (Dapper, NHibernate) requires changes to every handler. |
| **Generic repository `IRepository<T>`** | A single generic interface `IRepository<T>` provides CRUD methods for any entity. All entities share the same interface. | Generic repositories often become leaky abstractions — they expose methods (e.g., `Add`, `Delete`) that have no business meaning for `Policy` (hard deletes are prohibited; there is no create endpoint in the current API). They also tend to re-expose `IQueryable<T>`, which leaks EF Core's query model into the Application layer via LINQ expression trees — the very leakage the repository is meant to prevent. A purpose-built `IPolicyRepository` exposes exactly the operations the domain needs and nothing more. |
| **CQRS-style read service with direct DbContext for reads** | Use the repository only for write operations. Read handlers call `PolicyDbContext` directly with `.AsNoTracking()` for query performance, avoiding repository overhead on reads. | Creates two inconsistent data access patterns — some handlers use the repository, others use `DbContext` directly. The `DbContext` reference in Application still violates the Clean Architecture rule. The `.AsNoTracking()` convention and query composition rules are better enforced uniformly in the repository than scattered across handlers. |

## Consequences

### Positive

- **Clean Architecture compliance.** `Domain` defines the repository interface; `Application` depends on the interface; `Infrastructure` provides the implementation. `DbContext` never appears in `Domain` or `Application`. The dependency rule is preserved.
- **Unit testability.** Handlers can be unit-tested with a `Mock<IPolicyRepository>` (Moq). No database, no EF Core InMemory provider, no seed data — just a mock that returns test fixtures. Tests run in milliseconds.
- **ORM replaceability.** To replace EF Core with Dapper, only `PolicyRepository.cs` changes. All handlers, validators, and DTOs are untouched. The interface is the stable contract.
- **Enforced query discipline.** The `GetPagedAsync` method on `IPolicyRepository` accepts a `PolicyFilter` value object rather than an `IQueryable<Policy>`. This means LINQ query composition — including `.AsNoTracking()`, the soft-delete filter, and index-friendly predicates — is owned by the repository. Handlers cannot accidentally compose inefficient queries.
- **Explicit data access contract.** The interface documents exactly what database operations the domain needs. Adding a new operation requires an explicit interface change — it is never accidentally introduced by a handler calling `dbContext.Policies.Where(...)` inline.

### Negative / Trade-offs

- **Additional abstraction layer.** For a simple CRUD service, the repository adds a layer of indirection between the handler and the database call. A developer must look at the interface and then the implementation to understand what SQL is being generated. This is a discoverability cost.
- **Mapping between EF tracked entities and returned domain objects.** The repository must ensure it does not return EF Core change-tracking proxies to callers. For read operations, `.AsNoTracking()` is used. For the flag command, entities are fetched with tracking enabled, mutated, and saved in the same `SaveChangesAsync` call. The repository implementation must be careful not to return partially-hydrated proxies.
- **Risk of thin repository anti-pattern.** A poorly implemented repository is just a wrapper around `dbContext.Policies.Where(...)` that provides no real abstraction. To avoid this, query composition logic (filtering, sorting, pagination) belongs in the repository, not in the handler.

## Compliance with Clean Architecture

`IPolicyRepository` is in `Domain` — the innermost layer with no external dependencies. `PolicyRepository` is in `Infrastructure` — the outermost infrastructure layer. `Application` handlers reference only `IPolicyRepository` (a `Domain` interface). `PolicyDbContext` is never referenced outside `Infrastructure`. This arrangement is the canonical expression of the inward-dependency rule for data access.
