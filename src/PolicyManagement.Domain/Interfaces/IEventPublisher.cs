namespace PolicyManagement.Domain.Interfaces;

/// <summary>
/// Defines the contract for publishing domain events.
/// The in-memory implementation lives in <c>PolicyManagement.Infrastructure</c> and can be
/// swapped for a Kafka-backed implementation without any changes to handlers or domain logic.
/// Event payloads are plain C# records with no infrastructure dependencies.
/// </summary>
public interface IEventPublisher
{
    /// <summary>
    /// Publishes a domain event to all registered subscribers.
    /// </summary>
    /// <typeparam name="TEvent">
    /// The type of the domain event. Must be a plain C# record with no infrastructure dependencies.
    /// </typeparam>
    /// <param name="domainEvent">The event payload to publish.</param>
    /// <param name="cancellationToken">Token to cancel the asynchronous operation.</param>
    Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : class;
}
