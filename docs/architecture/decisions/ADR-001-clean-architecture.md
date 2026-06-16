# ADR-001: Clean Architecture over N-Tier or Vertical Slice

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The PolicyManagement BFF requires a structural pattern that governs how its four layers — domain logic, use cases, infrastructure, and HTTP delivery — relate to one another. Three patterns were viable for a .NET 10 ASP.NET Core Web API of this scale:

1. **N-Tier (layered)** — traditional top-to-bottom layered architecture where each tier depends on the one below it (UI → Business Logic → Data Access → Database).
2. **Clean Architecture** — concentric layer model where the innermost domain depends on nothing; all outer layers point inward.
3. **Vertical Slice Architecture** — organises code by feature/use-case rather than by layer; each slice contains all the code for a single feature (controller, handler, repository, validation, query).

The assessment's `copilot-instructions.md` mandates Clean Architecture explicitly. This ADR documents why that mandate is architecturally sound for this service and what alternatives were considered and rejected.

## Decision

The service is structured using **Clean Architecture** with the dependency rule strictly enforced:

```
API  →  Application  →  Domain  ←  Infrastructure
```

Four projects implement the four layers:

| Project | Layer |
|---|---|
| `PolicyManagement.Domain` | Domain — zero external dependencies |
| `PolicyManagement.Application` | Application — depends only on Domain |
| `PolicyManagement.Infrastructure` | Infrastructure — implements interfaces from Domain and Application |
| `PolicyManagement.API` | Delivery — depends on Application and Infrastructure (DI wiring only) |

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **N-Tier (layered architecture)** | Three layers: Presentation → Business Logic → Data Access. Each layer depends on the one directly below it. Widely used in enterprise .NET applications. | In N-Tier, the Data Access layer is the foundation. Business logic depends on it, which means business logic cannot be tested without a database. Swapping EF Core for a different ORM requires changes throughout the business layer. The dependency direction makes infrastructure a core assumption rather than a replaceable detail. |
| **Vertical Slice Architecture** | Each feature is a self-contained vertical slice. A single folder contains the controller action, MediatR handler, validator, DTO, and repository query for that feature. No horizontal layers. | Vertical slices work well for large teams where different teams own different features. For a single BFF service with shared cross-cutting concerns (caching, event publishing, global error handling), vertical slices create duplication of infrastructure wiring and make it harder to enforce that the domain has no infrastructure dependencies. The assessment explicitly mandates layering. |
| **Anemic service layer (no explicit architecture)** | Controllers call service classes, which call repository classes. No formal layer separation. | Produces God classes over time. Business logic migrates into controllers or services with no clear boundary. Infrastructure details (EF Core types, `DbContext`) leak into business methods. Makes unit testing impossible without mocking the entire persistence stack. Rejected immediately. |

## Consequences

### Positive

- **Testability.** `Domain` and `Application` have no dependency on EF Core, ASP.NET Core, or any I/O library. All handler logic can be unit-tested with mocked interfaces and no database setup.
- **Replaceability.** `ICacheService` can be replaced with a Redis implementation in `Infrastructure` without touching any handler. `IEventPublisher` can be replaced with Kafka without touching any domain event. The swap requires zero changes to `Domain` or `Application`.
- **Clear ownership.** Every new class has an unambiguous home. A query handler goes in `Application/Features/Policies/Queries/`. An EF Core configuration goes in `Infrastructure/Persistence/Configurations/`. A new domain rule goes in `Domain/Entities/`. New developers can navigate the codebase without tribal knowledge.
- **Stable domain.** The `Policy` entity's invariants (valid status transitions, flagging rules) are expressed in `Domain` independently of how policies are stored, served, or cached. Business rules survive infrastructure changes.
- **Compiler-enforced boundaries.** C# project references enforce the dependency rule at compile time. `PolicyManagement.Domain.csproj` has no `ProjectReference` to any other project. If a developer accidentally references `PolicyDbContext` from `Application`, the build fails.

### Negative / Trade-offs

- **More projects and files for a small service.** A BFF with four endpoints does not intrinsically need four projects. The ceremony of Clean Architecture is non-trivial relative to the domain complexity of this service. A simpler service might be over-engineered by this structure.
- **Mapping overhead.** Entities must be mapped to DTOs at the Application boundary. This requires mapping profiles (AutoMapper or manual) that add code without business value. The trade-off is explicit: the mapping cost is paid once; the testability and replaceability benefits are paid back continuously.
- **DI composition in `Program.cs` is verbose.** All interface-to-implementation registrations must be wired in `Program.cs` or extension methods. This is a fixed cost that scales with the number of services, not with the number of endpoints.

## Compliance with Clean Architecture

This decision defines Clean Architecture as the structural pattern. All subsequent decisions (CQRS, Repository, Caching, Eventing) are made within this constraint. Any decision that would require `Domain` or `Application` to reference an infrastructure concern is explicitly rejected. The four mandatory ADRs that follow all confirm compliance with the inward-dependency rule.
