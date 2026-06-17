namespace PolicyManagement.Domain.Events;

/// <summary>
/// Domain event raised when one or more policies are flagged for review.
/// This event is published by <c>FlagPoliciesCommandHandler</c> after a successful commit.
/// Consumers (e.g. audit log, notifications) subscribe via <c>IEventPublisher</c>.
/// </summary>
/// <param name="PolicyIds">The IDs of the policies that were flagged.</param>
/// <param name="FlaggedByUserId">The ID of the user who initiated the flag operation.</param>
/// <param name="FlaggedAt">The UTC timestamp at which the flag operation completed.</param>
public sealed record PolicyFlaggedEvent(
    IReadOnlyList<Guid> PolicyIds,
    string FlaggedByUserId,
    DateTimeOffset FlaggedAt);
