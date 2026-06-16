# ADR-002: Logical CQRS with MediatR over Alternatives

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The PolicyManagement BFF needs a mechanism to organise and dispatch its use cases — list policies, get a single policy, get summary statistics, and bulk-flag policies. The service has a clear read/write asymmetry: three of four operations are read-only queries, and one is a write command that produces domain events and invalidates cache.

Three structural approaches were considered:

1. **No CQRS** — single service classes handle both reads and writes. Controller calls a `PolicyService` which has methods for all operations.
2. **Logical CQRS with MediatR** — read and write operations are separated into distinct handler classes. Both use the same single SQL Server database. MediatR acts as the in-process mediator.
3. **Physical CQRS** — a separate read-optimised database (read model) is maintained alongside the write database. Write commands update the write store; a projection pipeline synchronises the read model asynchronously.

## Decision

The service uses **logical CQRS via MediatR** with a single SQL Server database and no separate read model.

Commands (writes):

| Command | Handler | What it does |
|---|---|---|
| `FlagPoliciesCommand` | `FlagPoliciesCommandHandler` | Bulk-flags policies; publishes `PolicyFlaggedEvent`; invalidates summary cache |

Queries (reads):

| Query | Handler | What it returns |
|---|---|---|
| `GetPoliciesQuery` | `GetPoliciesQueryHandler` | Paged, filtered, sorted list of `PolicyDto` with `.AsNoTracking()` |
| `GetPolicyByIdQuery` | `GetPolicyByIdQueryHandler` | Single `PolicyDto` by ID; cached via `ICacheService` |
| `GetPolicySummaryQuery` | `GetPolicySummaryQueryHandler` | Aggregated statistics; cached via `ICacheService` |

Controllers call `_mediator.Send(request, cancellationToken)` only. No business logic in controllers.

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **No CQRS — single service class per entity** | A `PolicyService` class has methods for all operations: `GetPoliciesAsync`, `GetByIdAsync`, `FlagPoliciesAsync`, `GetSummaryAsync`. Controllers call `PolicyService` directly. | A single service class accumulates all policy-related logic. It cannot be cached selectively — the entire class is one DI registration. Read and write methods share the same class, making it harder to enforce that reads never mutate state. Adding cross-cutting concerns (caching, logging, validation) requires decorators or AOP, which add complexity without the composability that a pipeline provides. Most importantly, the service becomes a God class as features are added. |
| **Physical CQRS** | Write operations go to a SQL Server write database. An asynchronous projection pipeline (Kafka, background worker, or change data capture) maintains a separate read-optimised store (e.g., a denormalised read model or a document store). Queries hit the read store. | Physical CQRS introduces eventual consistency — the read model may lag behind the write model by seconds or more. For an insurance dashboard where an agent flags a policy and immediately expects to see it flagged in the list, eventual consistency is a user experience problem without a clear mitigation at reasonable cost. Physical CQRS also requires infrastructure that does not exist in this service (a projection pipeline, a separate read database, synchronisation monitoring). This overhead is not justified for a BFF with four endpoints and moderate load. |
| **Logical CQRS without MediatR (direct handler injection)** | Controllers inject handler classes directly rather than using an in-process mediator. Handler classes implement a custom `ICommandHandler<T>` or `IQueryHandler<T>` interface. | Without MediatR, cross-cutting pipeline behaviours (validation, logging) must be implemented as decorators per handler type or inside each handler directly. MediatR's `IPipelineBehavior<,>` allows a single `ValidationPipelineBehavior` to apply to all commands and queries automatically. Removing MediatR makes the pipeline composition more verbose and less consistent. The only real cost of MediatR is a thin reflection-based dispatch — negligible at this scale. |

## Consequences

### Positive

- **Separated concerns by intent.** A `GetPoliciesQuery` cannot accidentally mutate state. A `FlagPoliciesCommand` cannot return a list. The type system enforces the read/write split at compile time.
- **Composable pipeline.** `ValidationPipelineBehavior` and `LoggingPipelineBehavior` attach once and apply to every handler. Adding request tracing, rate-limit enforcement, or idempotency checks requires one new `IPipelineBehavior<,>` registration — no changes to existing handlers.
- **Independent testability.** Each handler has a single, well-defined input record and a single output type. Unit tests are trivial: construct the handler with mocked dependencies, send the input, assert the output. No shared mutable state between tests.
- **Query optimisation without write-side impact.** Query handlers use `.AsNoTracking()` throughout. The command handler uses tracking. These are separate classes with separate responsibilities — there is no risk of a careless developer adding `.AsNoTracking()` to a write path and losing change detection.
- **Incremental extensibility.** Adding a new use case (e.g., `CreatePolicyCommand`) requires creating one new folder with a command record, a handler, and a validator. No existing class is modified. The Open/Closed Principle is structurally enforced.
- **Decoupled controller from handler.** The controller has no reference to the handler class type. This means the handler can be refactored, split, or replaced without touching the controller.

### Negative / Trade-offs

- **MediatR indirection.** The dispatch from controller to handler goes through MediatR's reflection-based resolution. The call site is `_mediator.Send(query, ct)` rather than `_handler.Handle(query, ct)`. For developers unfamiliar with MediatR, tracing a request from controller to handler requires knowing to look for `IRequestHandler<TRequest, TResponse>` in the DI registrations. This is a discoverability cost paid once.
- **Command/query proliferation.** Each use case is its own folder with its own message, handler, and validator. For a four-endpoint BFF, this is more files than a single service class would produce. The cost is proportional to the number of features, not the complexity of each one.
- **No physical read-model optimisation.** Logical CQRS still reads and writes from the same SQL Server database. If the read side grows to require denormalised projections or full-text search, the schema must be evolved in-place rather than moving reads to a separate optimised store. This is a deferred complexity trade-off, not a permanent limitation.

## Compliance with Clean Architecture

MediatR commands and queries are defined in `Application`. Handler classes reside in `Application`. The `IMediator` interface is resolved in `API` controllers. The MediatR DI registration happens in `Program.cs`. No MediatR type leaks into `Domain`. The `IPipelineBehavior<,>` implementations live in `Application`. This is fully consistent with the inward-dependency rule: `API → Application → Domain ← Infrastructure`.
