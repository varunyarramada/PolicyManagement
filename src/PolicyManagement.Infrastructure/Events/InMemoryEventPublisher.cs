using Microsoft.Extensions.Logging;
using PolicyManagement.Domain.Interfaces;

namespace PolicyManagement.Infrastructure.Events;

/// <summary>
/// In-memory implementation of <see cref="IEventPublisher"/>.
/// Logs every published event at <c>Information</c> level and no-ops otherwise.
/// <para>
/// This stub is suitable for development and testing. To swap for a Kafka-backed
/// implementation, create a <c>KafkaEventPublisher : IEventPublisher</c> in this
/// folder and change the DI registration in <c>InfrastructureServiceExtensions</c>
/// — no handler or domain code changes required (ADR-005).
/// </para>
/// </summary>
public sealed class InMemoryEventPublisher(
    ILogger<InMemoryEventPublisher> logger)
    : IEventPublisher
{
    /// <inheritdoc/>
    public Task PublishAsync<TEvent>(TEvent domainEvent, CancellationToken cancellationToken = default)
        where TEvent : class
    {
        logger.LogInformation(
            "Domain event published: {EventType} {@Event}",
            typeof(TEvent).Name,
            domainEvent);

        return Task.CompletedTask;
    }
}
