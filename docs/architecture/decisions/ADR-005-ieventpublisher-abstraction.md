# ADR-005: IEventPublisher Abstraction over Direct Kafka SDK

- **Date:** 2026-06-16
- **Status:** Accepted

## Context

The PolicyManagement BFF publishes a `PolicyFlaggedEvent` domain event when one or more policies are successfully flagged. The assessment describes a Kafka integration as a bonus requirement: a Kafka producer publishes events when policies are flagged, and a Kafka consumer listens for policy status change events with idempotent handling.

For the initial implementation, Kafka infrastructure is not a dependency. The service must be runnable locally without a Kafka broker. The question is how to introduce event publishing in a way that:

1. Satisfies the current requirement (events are published when policies are flagged).
2. Does not require Kafka to be available for local development or testing.
3. Allows a production-ready Kafka publisher to be substituted without any changes to the domain or application layers.

Three approaches were considered:

1. **Direct Confluent Kafka SDK usage in the command handler** — `FlagPoliciesCommandHandler` injects `IProducer<string, string>` (Confluent.Kafka) directly and calls `ProduceAsync`.
2. **ASP.NET Core `IHostedService` background queue** — Events are written to a `Channel<T>` (in-memory queue) by the handler. A background service (`IHostedService`) reads from the channel and calls Kafka or any other publisher.
3. **Custom `IEventPublisher` abstraction** — A purpose-built interface defined in `Domain` (or `Application`) that hides the publishing mechanism. The in-memory implementation logs the event. The Kafka implementation uses the Confluent SDK.

## Decision

The service defines a **custom `IEventPublisher` interface in `PolicyManagement.Domain/Interfaces/`**. The current in-memory implementation (`InMemoryEventPublisher`) lives in `PolicyManagement.Infrastructure/Events/`. `FlagPoliciesCommandHandler` depends only on `IEventPublisher`.

```
// Domain/Interfaces/IEventPublisher.cs
public interface IEventPublisher
{
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken ct)
        where TEvent : class;
}
```

Domain events are plain C# records defined in `Domain/Events/` with no infrastructure dependencies:

```
// Domain/Events/PolicyFlaggedEvent.cs
public record PolicyFlaggedEvent(
    Guid PolicyId,
    string PolicyNumber,
    DateTimeOffset FlaggedAt);
```

The `InMemoryEventPublisher` logs the event at `Information` level using `ILogger<InMemoryEventPublisher>`. A future `KafkaEventPublisher` will use the Confluent Kafka SDK to produce to a topic, without any change to the handler or the domain event definition.

**Kafka swap path:**

1. Add `PolicyManagement.Infrastructure/Events/KafkaEventPublisher.cs` implementing `IEventPublisher`.
2. Change the DI registration in `Program.cs` from `InMemoryEventPublisher` to `KafkaEventPublisher`.
3. Add Kafka connection options via `KafkaOptions : IOptions<KafkaOptions>` bound from `appsettings.json`.
4. No changes to `FlagPoliciesCommandHandler`, `PolicyFlaggedEvent`, or `Domain`.

## Alternatives Considered

| Option | Description | Why Rejected |
|--------|-------------|-------------|
| **Direct Confluent Kafka SDK in handler** | `FlagPoliciesCommandHandler` in `Application` directly references `Confluent.Kafka.IProducer<string, string>` and produces messages. | The `Confluent.Kafka` NuGet package must be referenced from `Application.csproj`. This introduces an infrastructure SDK dependency into the Application layer — a direct Clean Architecture violation. The handler cannot be unit-tested without a real or mocked Kafka producer. Swapping Kafka for a different message broker (e.g., Azure Service Bus) requires changing the handler. |
| **In-memory `Channel<T>` with background service** | The handler writes events to a `Channel<PolicyFlaggedEvent>`. A hosted background service reads from the channel and dispatches to whatever publisher is configured. | This adds an asynchronous dispatch hop that decouples publish from the request pipeline — but at the cost of losing the guarantee that "if `SaveChanges` succeeds, the event will be published". If the application crashes after `SaveChanges` but before the background service processes the channel, the event is lost silently. For the current in-memory implementation, this gap is acceptable; for the Kafka production implementation, it is not. The transactional outbox pattern is a better production solution. The background service approach also adds complexity (channel sizing, backpressure, graceful shutdown) with no benefit over the simple `IEventPublisher` abstraction at the current scale. |
| **Domain events via EF Core interceptors** | Use an EF Core `SaveChangesInterceptor` to detect `PolicyFlaggedEvent` additions to the `ChangeTracker` and publish them automatically after commit. | Introduces a tight coupling between the EF Core save pipeline and event publishing. It is less explicit than the command handler calling `PublishAsync` — the event publication is invisible to someone reading the handler code. EF Core interceptors live in `Infrastructure`, which creates an ordering dependency: events are dispatched from the infrastructure layer rather than the application layer. Testing the publication behaviour requires mocking the EF Core pipeline. |

## Consequences

### Positive

- **Clean Architecture compliance.** `IEventPublisher` is in `Domain` — the innermost layer. Domain events are plain records in `Domain/Events/`. `FlagPoliciesCommandHandler` in `Application` depends on `IEventPublisher` (a `Domain` interface). The Confluent Kafka SDK reference lives only in `Infrastructure`. No infrastructure SDK appears in `Domain` or `Application`.
- **Kafka swap is a single file addition and one DI change.** The `KafkaEventPublisher` is the only new code required to move from in-memory to Kafka publishing. All other code is unchanged.
- **Event schema stability.** `PolicyFlaggedEvent` is a plain C# record with no infrastructure attributes or serialisation annotations. Its properties define the event schema. The Kafka implementation serialises the record to JSON. Changing the serialisation format (e.g., from JSON to Avro) requires changes only in `KafkaEventPublisher`, not in the event definition.
- **Unit testability.** `Mock<IEventPublisher>` is trivial. The test asserts that `PublishAsync` was called with the expected event — no Kafka broker, no message queue, no background thread.
- **Failure visibility.** The `InMemoryEventPublisher` logs at `Warning` level if `PublishAsync` is called and the implementation encounters an error. For production, the `KafkaEventPublisher` implementation should handle delivery failure by logging at `Error` level and — if using the transactional outbox pattern — by marking the outbox entry as failed for retry.

### Negative / Trade-offs

- **In-memory implementation is not durable.** If the process crashes after the database commit but before `PublishAsync` completes, no event is published. This is an inherent limitation of synchronous in-process event publishing. For production, the transactional outbox pattern resolves this: events are written to an `PolicyOutboxEvents` table in the same SQL Server transaction, and a background worker reads and publishes them with at-least-once delivery guarantees. This is deferred for the current assessment implementation.
- **No delivery acknowledgement.** `PublishAsync` is fire-and-forget in the current in-memory implementation. The command handler does not know whether the event was successfully delivered. For the Kafka implementation, `IProducer.ProduceAsync` returns a `DeliveryResult` — the `KafkaEventPublisher` should check this and log failures. The interface signature (`Task PublishAsync`) does not surface delivery failures to the caller — failures are logged by the publisher implementation.
- **Single generic method for all event types.** The `PublishAsync<TEvent>` signature is generic. Topic routing (for Kafka) must be resolved by the publisher implementation based on the event type (e.g., via a type-to-topic dictionary or naming convention). This is internal to the publisher and invisible to the handler — by design.

## Compliance with Clean Architecture

`IEventPublisher` is in `PolicyManagement.Domain/Interfaces/` — the innermost layer with zero external dependencies. `PolicyFlaggedEvent` is in `PolicyManagement.Domain/Events/` — also zero external dependencies. `InMemoryEventPublisher` and the future `KafkaEventPublisher` live in `PolicyManagement.Infrastructure/Events/`. `FlagPoliciesCommandHandler` in `Application` references only the `Domain` interface. The Confluent Kafka NuGet package is referenced only from `Infrastructure.csproj`. The inward-dependency rule is fully observed.
